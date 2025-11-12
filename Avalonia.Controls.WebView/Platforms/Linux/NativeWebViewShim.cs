using System;
using System.Runtime.InteropServices;

namespace Avalonia.Controls.WebView.Platforms.Linux
{
    internal static class NativeWebViewShim
    {
        private const string LibName = "libwebview_shim.so";

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "create_webview")]
        public static extern IntPtr CreateWebView(IntPtr parentXid, int x, int y, int width, int height);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "destroy_webview")]
        public static extern void DestroyWebView(IntPtr handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "navigate_webview")]
        public static extern void NavigateWebView(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string url);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "resize_webview")]
        public static extern void ResizeWebView(IntPtr handle, int x, int y, int width, int height);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "execute_js")]
        public static extern void ExecuteJs(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string script);

        // New navigation helpers
        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "can_go_back")]
        public static extern int CanGoBack(IntPtr handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "can_go_forward")]
        public static extern int CanGoForward(IntPtr handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "go_back")]
        public static extern void GoBack(IntPtr handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "go_forward")]
        public static extern void GoForward(IntPtr handle);

        [DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "reload_webview")]
        public static extern void Reload(IntPtr handle);
    }
}