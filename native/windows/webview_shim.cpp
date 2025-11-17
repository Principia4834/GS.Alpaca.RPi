/* 
  webview_shim.cpp
  Improved native Windows shim that hosts Microsoft Edge WebView2 in a child window
  and exposes a minimal C API to managed code.
*/

#include <windows.h>
#include <wrl.h>
#include <string>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <atomic>
#include <queue>
#include <functional>
#include <iostream>

#include "WebView2.h"

using namespace Microsoft::WRL;

typedef void (__cdecl *NavigationStateChangedCallback)(int canGoBack, int canGoForward, void* userData);

struct WebViewInstance {
    HWND parentHwnd;
    HWND childHwnd;
    ICoreWebView2Controller* controller;
    ICoreWebView2* webview;
    DWORD ownerThreadId;
    bool createdOnUiThread;
};

static std::thread g_uiThread;
static std::mutex g_mutex;
static std::condition_variable g_cv;
static bool g_threadReady = false;
static std::atomic<bool> g_running{ false };
static std::queue<std::function<void()>> g_tasks;
static DWORD g_uiThreadId = 0;

// Navigation callback storage
static NavigationStateChangedCallback g_navStateCallback = nullptr;
static void* g_navStateUserData = nullptr;
static std::mutex g_navCallbackMutex;

static void log_debug(const char* msg) {
    OutputDebugStringA(msg);
    fprintf(stderr, "%s\n", msg);
}

static void post_to_ui_thread(std::function<void()> f) {
    {
        std::lock_guard<std::mutex> lk(g_mutex);
        g_tasks.push(std::move(f));
    }
    g_cv.notify_one();
}

static void ui_thread_func() {
    g_uiThreadId = GetCurrentThreadId();

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) {
        log_debug("webview_shim: CoInitializeEx failed on UI thread");
        return;
    }

    {
        std::lock_guard<std::mutex> lk(g_mutex);
        g_threadReady = true;
    }
    g_cv.notify_one();

    MSG msg;
    while (g_running.load()) {
        std::function<void()> task;
        {
            std::unique_lock<std::mutex> lk(g_mutex);
            if (g_tasks.empty()) {
                g_cv.wait_for(lk, std::chrono::milliseconds(50));
            }
            if (!g_tasks.empty()) {
                task = std::move(g_tasks.front());
                g_tasks.pop();
            }
        }
        if (task) {
            try { task(); } catch (...) { log_debug("webview_shim: task threw exception"); }
        }

        while (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }

    CoUninitialize();
    g_uiThreadId = 0;
}

static void ensure_ui_thread() {
    std::unique_lock<std::mutex> lk(g_mutex);
    if (!g_threadReady) {
        g_running.store(true);
        g_uiThread = std::thread(ui_thread_func);
        g_cv.wait(lk, [] { return g_threadReady; });
    }
}

static LRESULT CALLBACK HostWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
    case WM_SIZE:
        break;
    case WM_DESTROY:
        PostQuitMessage(0);
        break;
    default:
        return DefWindowProc(hwnd, msg, wp, lp);
    }
    return 0;
}

static ATOM ensure_window_class() {
    static ATOM atom = 0;
    if (atom) return atom;

    WNDCLASSEX wc = {};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = HostWndProc;
    wc.hInstance = GetModuleHandle(NULL);
    wc.lpszClassName = L"WebViewHostWindowClass";
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    atom = RegisterClassEx(&wc);
    if (!atom) {
        log_debug("webview_shim: RegisterClassEx failed");
    }
    return atom;
}

template<typename F>
void run_sync(F &&f) {
    std::mutex m;
    std::condition_variable v;
    bool done = false;
    std::exception_ptr eptr = nullptr;

    post_to_ui_thread([&] {
        try { f(); } catch (...) { eptr = std::current_exception(); }
        {
            std::lock_guard<std::mutex> lk(m);
            done = true;
        }
        v.notify_one();
    });

    std::unique_lock<std::mutex> lk(m);
    v.wait(lk, [&] { return done; });
    if (eptr) std::rethrow_exception(eptr);
}

static void notify_navigation_state_changed(WebViewInstance* inst) {
    std::lock_guard<std::mutex> lk(g_navCallbackMutex);
    if (!g_navStateCallback || !inst || !inst->webview) return;
    BOOL canBack = FALSE;
    BOOL canForward = FALSE;
    HRESULT hr1 = inst->webview->get_CanGoBack(&canBack);
    HRESULT hr2 = inst->webview->get_CanGoForward(&canForward);
    int cb = (SUCCEEDED(hr1) && canBack) ? 1 : 0;
    int cf = (SUCCEEDED(hr2) && canForward) ? 1 : 0;
    g_navStateCallback(cb, cf, g_navStateUserData);
}

extern "C" {

__declspec(dllexport) void register_navigation_state_callback(NavigationStateChangedCallback cb, void* userData) {
    std::lock_guard<std::mutex> lk(g_navCallbackMutex);
    g_navStateCallback = cb;
    g_navStateUserData = userData;
}

__declspec(dllexport) void* create_webview(void* parent_hwnd_ptr, int x, int y, int width, int height) {
    HWND parentHwnd = nullptr;
    if (parent_hwnd_ptr) parentHwnd = (HWND)parent_hwnd_ptr;

    DWORD parentThreadId = 0;
    if (parentHwnd) {
        parentThreadId = GetWindowThreadProcessId(parentHwnd, NULL);
    }

    ensure_window_class();

    if (parentHwnd != NULL && parentThreadId == GetCurrentThreadId()) {
        WebViewInstance* inst = new WebViewInstance();
        inst->parentHwnd = parentHwnd;
        inst->childHwnd = nullptr;
        inst->controller = nullptr;
        inst->webview = nullptr;
        inst->ownerThreadId = GetCurrentThreadId();
        inst->createdOnUiThread = false;

        DWORD style = WS_CHILD | WS_VISIBLE;
        inst->childHwnd = CreateWindowEx(0, L"WebViewHostWindowClass", L"WebViewHost",
            style, x, y, width, height, inst->parentHwnd, NULL, GetModuleHandle(NULL), NULL);

        if (!inst->childHwnd) {
            log_debug("webview_shim: CreateWindowEx failed (same-thread parent creation)");
            delete inst;
            return NULL;
        }

        HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        bool coInitializedHere = SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE;
        CreateCoreWebView2EnvironmentWithOptions(nullptr, nullptr, nullptr,
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [inst, width, height](HRESULT envResult, ICoreWebView2Environment* env) -> HRESULT {
                    if (FAILED(envResult) || env == nullptr) {
                        log_debug("webview_shim: CreateCoreWebView2Environment failed (same-thread)");
                        return S_OK;
                    }
                    env->CreateCoreWebView2Controller(inst->childHwnd,
                        Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                            [inst, width, height](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT {
                                if (SUCCEEDED(result) && controller != nullptr) {
                                    inst->controller = controller;
                                    controller->get_CoreWebView2(&inst->webview);
                                    RECT rc; GetClientRect(inst->childHwnd, &rc);
                                    controller->put_Bounds(rc);
                                    // Notify initial navigation state
                                    notify_navigation_state_changed(inst);
                                } else {
                                    log_debug("webview_shim: CreateCoreWebView2Controller failed (same-thread)");
                                }
                                return S_OK;
                            }).Get());
        (void)coInitializedHere;

        return (void*)inst;
    }

    ensure_ui_thread();

    WebViewInstance* inst = new WebViewInstance();
    inst->parentHwnd = parentHwnd;
    inst->childHwnd = nullptr;
    inst->controller = nullptr;
    inst->webview = nullptr;
    inst->ownerThreadId = g_uiThreadId;
    inst->createdOnUiThread = true;

    run_sync([&] {
        HWND hwndParent = nullptr;
        DWORD style = WS_OVERLAPPEDWINDOW | WS_VISIBLE;
        if (parentHwnd && parentThreadId == g_uiThreadId) {
            hwndParent = parentHwnd;
            style = WS_CHILD | WS_VISIBLE;
        }

        inst->childHwnd = CreateWindowEx(0, L"WebViewHostWindowClass", L"WebViewHost",
            style, x, y, width, height, hwndParent, NULL, GetModuleHandle(NULL), NULL);

        if (!inst->childHwnd) {
            log_debug("webview_shim: CreateWindowEx failed (ui-thread mode)");
            return;
        }

        HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, nullptr, nullptr,
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [inst, width, height](HRESULT envResult, ICoreWebView2Environment* env) -> HRESULT {
                    if (FAILED(envResult) || env == nullptr) {
                        log_debug("webview_shim: CreateCoreWebView2Environment failed (ui-thread)");
                        return S_OK;
                    }
                    env->CreateCoreWebView2Controller(inst->childHwnd,
                        Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
                            [inst, width, height](HRESULT result, ICoreWebView2Controller* controller) -> HRESULT {
                                if (SUCCEEDED(result) && controller != nullptr) {
                                    inst->controller = controller;
                                    controller->get_CoreWebView2(&inst->webview);
                                    RECT rc; GetClientRect(inst->childHwnd, &rc);
                                    controller->put_Bounds(rc);
                                    // attach history/navigation callbacks if available and notify initial state
                                    notify_navigation_state_changed(inst);
                                } else {
                                    log_debug("webview_shim: CreateCoreWebView2Controller failed (ui-thread)");
                                }
                                return S_OK;
                            }).Get());
    });

    return (void*)inst;
}

__declspec(dllexport) void destroy_webview(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;

    auto destroyLambda = [&] {
        if (inst->controller) {
            inst->controller->Close();
            inst->controller->Release();
            inst->controller = nullptr;
        }
        if (inst->webview) {
            inst->webview->Release();
            inst->webview = nullptr;
        }
        if (inst->childHwnd) {
            DestroyWindow(inst->childHwnd);
            inst->childHwnd = nullptr;
        }
    };

    if (inst->createdOnUiThread && inst->ownerThreadId == g_uiThreadId) {
        run_sync(destroyLambda);
    } else {
        destroyLambda();
    }

    delete inst;
}

__declspec(dllexport) void navigate_webview(void* handle, const char* url) {
    if (!handle || !url) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    std::string s(url);
    int len = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, NULL, 0);
    std::wstring wurl(len, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, &wurl[0], len);

    auto nav = [&] {
        if (inst->webview) {
            inst->webview->Navigate(wurl.c_str());
        }
    };

    if (inst->createdOnUiThread && inst->ownerThreadId == g_uiThreadId) run_sync(nav);
    else nav();
}

__declspec(dllexport) void resize_webview(void* handle, int x, int y, int width, int height) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    auto rs = [&] {
        if (inst->childHwnd) {
            MoveWindow(inst->childHwnd, x, y, width, height, TRUE);
        }
        if (inst->controller) {
            RECT rc; GetClientRect(inst->childHwnd, &rc);
            inst->controller->put_Bounds(rc);
        }
    };
    if (inst->createdOnUiThread && inst->ownerThreadId == g_uiThreadId) run_sync(rs);
    else rs();
}

__declspec(dllexport) void execute_js(void* handle, const char* script) {
    if (!handle || !script) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    std::string s(script);
    int len = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, NULL, 0);
    std::wstring wscript(len, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, &wscript[0], len);

    post_to_ui_thread([inst, wscript]() {
        if (inst->webview) {
            inst->webview->ExecuteScript(wscript.c_str(), nullptr);
        }
    });
}

__declspec(dllexport) int can_go_back(void* handle) {
    if (!handle) return 0;
    WebViewInstance* inst = (WebViewInstance*)handle;
    int result = 0;
    auto cb = [&] {
        if (inst->webview) {
            BOOL b = FALSE;
            if (SUCCEEDED(inst->webview->get_CanGoBack(&b)) && b) result = 1;
        }
    };
    if (inst->createdOnUiThread && inst->ownerThreadId == g_uiThreadId) run_sync(cb);
    else cb();
    return result;
}

__declspec(dllexport) int can_go_forward(void* handle) {
    if (!handle) return 0;
    WebViewInstance* inst = (WebViewInstance*)handle;
    int result = 0;
    auto cf = [&] {
        if (inst->webview) {
            BOOL b = FALSE;
            if (SUCCEEDED(inst->webview->get_CanGoForward(&b)) && b) result = 1;
        }
    };
    if (inst->createdOnUiThread && inst->ownerThreadId == g_uiThreadId) run_sync(cf);
    else cf();
    return result;
}

__declspec(dllexport) void go_back(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    post_to_ui_thread([inst]() {
        if (inst->webview) inst->webview->GoBack();
    });
}

__declspec(dllexport) void go_forward(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    post_to_ui_thread([inst]() {
        if (inst->webview) inst->webview->GoForward();
    });
}

__declspec(dllexport) void reload_webview(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    post_to_ui_thread([inst]() {
        if (inst->webview) inst->webview->Reload();
    });
}

} // extern "C"