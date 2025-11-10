using System;
using System.Runtime.InteropServices;

namespace Avalonia.Controls.WebView.Platforms.Linux
{
    /// <summary>
    /// P/Invoke wrapper for the native webview shim on Linux.
    /// The native shared library is expected to be named libwebview_shim.so and located on LD_LIBRARY_PATH
    /// or next to the application executable.
    /// </summary>
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
    }
}