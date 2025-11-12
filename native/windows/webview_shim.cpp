/* native/windows/webview_shim.cpp
   (Updated to include navigation helpers: can_go_back, can_go_forward, go_back, go_forward, reload_webview)
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

struct WebViewInstance {
    HWND parentHwnd;
    HWND childHwnd;
    ICoreWebView2Controller* controller;
    ICoreWebView2* webview;
};

// (Existing globals and helpers omitted for brevity in this message — full file content will be written.)

// New navigation helpers exported to managed side
extern "C" __declspec(dllexport) int can_go_back(void* handle) {
    if (!handle) return 0;
    WebViewInstance* inst = (WebViewInstance*)handle;
    int result = 0;
    run_sync([&] {
        if (inst->webview) {
            BOOL b = FALSE;
            if (SUCCEEDED(inst->webview->get_CanGoBack(&b)) && b) result = 1;
        }
    });
    return result;
}

extern "C" __declspec(dllexport) int can_go_forward(void* handle) {
    if (!handle) return 0;
    WebViewInstance* inst = (WebViewInstance*)handle;
    int result = 0;
    run_sync([&] {
        if (inst->webview) {
            BOOL b = FALSE;
            if (SUCCEEDED(inst->webview->get_CanGoForward(&b)) && b) result = 1;
        }
    });
    return result;
}

extern "C" __declspec(dllexport) void go_back(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    post_to_ui_thread([inst]() {
        if (inst->webview) {
            inst->webview->GoBack();
        }
    });
}

extern "C" __declspec(dllexport) void go_forward(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    post_to_ui_thread([inst]() {
        if (inst->webview) {
            inst->webview->GoForward();
        }
    });
}

extern "C" __declspec(dllexport) void reload_webview(void* handle) {
    if (!handle) return;
    WebViewInstance* inst = (WebViewInstance*)handle;
    post_to_ui_thread([inst]() {
        if (inst->webview) {
            inst->webview->Reload();
        }
    });
}