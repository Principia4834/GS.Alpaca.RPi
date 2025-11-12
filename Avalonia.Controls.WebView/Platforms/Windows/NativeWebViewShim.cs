// P/Invoke wrapper for the native Windows webview shim (webview_shim.dll).
// The native DLL must be built and placed next to the app executable (or on PATH).
// The wrapper avoids any compile-time dependency on Microsoft.Web.WebView2.
using System;
using System.Runtime.InteropServices;

namespace Avalonia.Controls.WebView.Platforms.Windows
{
    internal static class NativeWebViewShim
    {
        private const string LibName = "webview_shim.dll";

        // Create a webview. parentHwnd may be IntPtr.Zero for a top-level window.
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "create_webview")]
        public static extern IntPtr CreateWebView(IntPtr parentHwnd, int x, int y, int width, int height);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "destroy_webview")]
        public static extern void DestroyWebView(IntPtr handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "navigate_webview")]
        public static extern void NavigateWebView(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string url);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "resize_webview")]
        public static extern void ResizeWebView(IntPtr handle, int x, int y, int width, int height);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "execute_js")]
        public static extern void ExecuteJs(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string script);
    }
}