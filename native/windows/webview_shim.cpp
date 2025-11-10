/*
  webview_shim.cpp
  Improved native Windows shim that hosts Microsoft Edge WebView2 in a child window
  and exposes a minimal C API to managed code.

  Improvements over the basic shim:
  - Better error/logging using OutputDebugStringA and stderr
  - More robust UI thread startup and graceful shutdown
  - Optionally returns the created child HWND as part of the WebViewInstance for diagnostics
  - CMake-friendly comments and suggestions for locating the WebView2 SDK

  Requirements:
  - Visual Studio 2019/2022 with C++ toolset (MSVC)
  - WebView2 SDK installed (headers and import library). If you install the WebView2 SDK via NuGet or the WebView2 installer,
    point CMake variable WEBVIEW2_SDK_PATH to the SDK root where include/ and lib/ are located.
  - WebView2 runtime installed on the target machine at runtime (Edge WebView2).
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

// Attempt to include WebView2 header. CMake should configure include paths; if not, adjust include path or set WEBVIEW2_SDK_PATH.
#include "WebView2.h"

using namespace Microsoft::WRL;

struct WebViewInstance {
    HWND parentHwnd;
    HWND childHwnd;
    ICoreWebView2Controller* controller;
    ICoreWebView2* webview;
};

static std::thread g_uiThread;
static std::mutex g_mutex;
static std::condition_variable g_cv;
static bool g_threadReady = false;
static std::atomic<bool> g_running{ false };
static std::queue<std::function<void()>> g_tasks;

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
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) {
        log_debug("webview_shim: CoInitializeEx failed");
        return;
    }

    {
        std::lock_guard<std::mutex> lk(g_mutex);
        g_threadReady = true;
    }
    g_cv.notify_one();

    MSG msg;
    while (g_running.load()) {
        // process tasks
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

        // Windows messages
        while (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }

    CoUninitialize();
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
        // The controller will be resized by resize calls; keep default behaviour
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

extern "C" {

__declspec(dllexport) void* create_webview(void* parent_hwnd_ptr, int x, int y, int width, int height) {
    ensure_ui_thread();

    HWND parentHwnd = nullptr;
    if (parent_hwnd_ptr) parentHwnd = (HWND)parent_hwnd_ptr;

    WebViewInstance* inst = new WebViewInstance();
    inst->parentHwnd = parentHwnd;
    inst->childHwnd = nullptr;
    inst->controller = nullptr;
    inst->webview = nullptr;

    run_sync([&] {
        ensure_window_class();
        HWND hwndParent = parentHwnd ? parentHwnd : nullptr;
        DWORD style = WS_CHILD | WS_VISIBLE;
        if (!hwndParent) style = WS_OVERLAPPEDWINDOW | WS_VISIBLE;

        inst->childHwnd = CreateWindowEx(0, L"WebViewHostWindowClass", L"WebViewHost",
            style, x, y, width, height, hwndParent, NULL, GetModuleHandle(NULL), NULL);

        if (!inst->childHwnd) {
            log_debug("webview_shim: CreateWindowEx failed");
            return;
        }

        // Initialize WebView2 environment and controller
        HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(nullptr, nullptr, nullptr,
            Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
                [inst, width, height](HRESULT envResult, ICoreWebView2Environment* env) -> HRESULT {
                    if (FAILED(envResult) || env == nullptr) {
                        log_debug("webview_shim: CreateCoreWebView2Environment failed");
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
                                } else {
                                    log_debug("webview_shim: CreateCoreWebView2Controller failed");
                                }
                                return S_OK;
                            }).Get());
                    return S_OK;
                }).Get());
    });

    // Return opaque pointer (managed side can keep it for later calls).
    return (void*)inst;
}

__declspec(dllexport) void destroy_webview(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    run_sync([&] {
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
    });
    delete inst;
}

__declspec(dllexport) void navigate_webview(void* handle, const char* url) {
    if (!handle || !url) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    std::string s(url);
    // convert UTF-8 to wide
    int len = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, NULL, 0);
    std::wstring wurl(len, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), -1, &wurl[0], len);

    run_sync([&] {
        if (inst->webview) {
            inst->webview->Navigate(wurl.c_str());
        }
    });
}

__declspec(dllexport) void resize_webview(void* handle, int x, int y, int width, int height) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    run_sync([&] {
        if (inst->childHwnd) {
            MoveWindow(inst->childHwnd, x, y, width, height, TRUE);
        }
        if (inst->controller) {
            RECT rc; GetClientRect(inst->childHwnd, &rc);
            inst->controller->put_Bounds(rc);
        }
    });
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
            // fire-and-forget; no result returned
            inst->webview->ExecuteScript(wscript.c_str(), nullptr);
        }
    });
}

} // extern "C"
