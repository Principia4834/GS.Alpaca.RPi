// Cross-platform Avalonia WebView control.
// This revision removes any compile-time dependency on Microsoft.Web.WebView2.WinForms
// and instead selects a native shim at runtime for Windows and Linux.
// Platform-specific native shims are invoked via P/Invoke wrappers located in:
//  - Avalonia.Controls.WebView.Platforms.Windows.NativeWebViewShim
//  - Avalonia.Controls.WebView.Platforms.Linux.NativeWebViewShim
//
// Platform conditional behavior:
//  - Windows: RuntimeInformation.IsOSPlatform(OSPlatform.Windows) -> call Windows native shim (webview_shim.dll).
//  - Linux:   RuntimeInformation.IsOSPlatform(OSPlatform.Linux)   -> call Linux native shim (libwebview_shim.so).
//  - Other:   throws PlatformNotSupportedException.
//
// Notes:
//  - The shims are responsible for creating native child windows/widgets and managing the platform webview.
//  - The managed control only holds opaque handles returned by the shim and forwards navigate/resize/execute commands.
//  - JS execution via the shim is currently fire-and-forget; no result is returned (can be extended).
//  - Ensure the native libraries are built and copied next to the app executable (or on system PATH/LD_LIBRARY_PATH).

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using Avalonia.Controls.WebView.Platforms.Linux;
using Avalonia.Controls.WebView.Platforms.Windows;

namespace Avalonia.Controls.WebView
{
    public class AvaloniaWebView : NativeControlHost
    {
        #region Styled Properties
        public static readonly StyledProperty<Uri?> SourceProperty =
            AvaloniaProperty.Register<AvaloniaWebView, Uri?>(nameof(Source));

        public Uri? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }
        #endregion

        // Opaque handles returned by the native shims.
        // Only one will be non-zero depending on the platform.
        private IntPtr _linuxHandle = IntPtr.Zero;
        private IntPtr _windowsHandle = IntPtr.Zero;

        public AvaloniaWebView()
        {
            SourceProperty.Changed.AddClassHandler<AvaloniaWebView>((sender, e) => sender.OnSourceChanged(e));
        }

        /// <summary>
        /// Create the native control using the platform-specific shim.
        /// Returns a PlatformHandle that represents the parent/native handle that Avalonia will use to attach.
        /// - Windows: returns PlatformHandle(parentHwnd, "HWND")
        /// - Linux:   returns PlatformHandle(parentXid, "XID")
        /// </summary>
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            // Windows: call native Windows shim (webview_shim.dll)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                IntPtr parentHwnd = parent?.Handle ?? IntPtr.Zero;
                int x = 0, y = 0;
                int width = Math.Max(1, (int)Bounds.Width);
                int height = Math.Max(1, (int)Bounds.Height);

                try
                {
                    _windowsHandle = Platforms.Windows.NativeWebViewShim.CreateWebView(parentHwnd, x, y, width, height);
                }
                catch (DllNotFoundException dnfe)
                {
                    Debug.WriteLine($"Windows webview shim not found: {dnfe.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to create Windows native webview: {ex}");
                    throw;
                }

                // Return the parent HWND so Avalonia can attach; the shim reparents or hosts its child window.
                return new PlatformHandle(parentHwnd, "HWND");
            }

            // Linux: call native Linux shim (libwebview_shim.so)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IntPtr parentXid = parent?.Handle ?? IntPtr.Zero;
                int x = 0, y = 0;
                int width = Math.Max(1, (int)Bounds.Width);
                int height = Math.Max(1, (int)Bounds.Height);

                try
                {
                    _linuxHandle = Platforms.Linux.NativeWebViewShim.CreateWebView(parentXid, x, y, width, height);
                }
                catch (DllNotFoundException dnfe)
                {
                    Debug.WriteLine($"Linux webview shim not found: {dnfe.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to create Linux native webview: {ex}");
                    throw;
                }

                // Return the parent XID so Avalonia can attach; the shim will reparent its widget into this parent.
                return new PlatformHandle(parentXid, "XID");
            }

            throw new PlatformNotSupportedException("Unsupported platform for AvaloniaWebView");
        }

        /// <summary>
        /// Destroy the native control via the appropriate shim.
        /// </summary>
        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_windowsHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Windows.NativeWebViewShim.DestroyWebView(_windowsHandle);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error destroying Windows webview shim: {ex}");
                    }
                    finally
                    {
                        _windowsHandle = IntPtr.Zero;
                    }
                }
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Linux.NativeWebViewShim.DestroyWebView(_linuxHandle);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error destroying Linux webview shim: {ex}");
                    }
                    finally
                    {
                        _linuxHandle = IntPtr.Zero;
                    }
                }
                return;
            }

            base.DestroyNativeControlCore(control);
        }

        #region Navigation and Scripting API (cross-platform thin wrappers)

        // Navigate by string
        public void Navigate(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("Invalid URL format", nameof(url));

            Navigate(uri);
        }

        // Navigate by Uri
        public void Navigate(Uri uri)
        {
            if (uri == null) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_windowsHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Windows.NativeWebViewShim.NavigateWebView(_windowsHandle, uri.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Navigate (Windows shim) failed: {ex}");
                    }
                }
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Linux.NativeWebViewShim.NavigateWebView(_linuxHandle, uri.ToString());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Navigate (Linux shim) failed: {ex}");
                    }
                }
                return;
            }
        }

        // Execute script (fire-and-forget in current shims)
        public Task<string> ExecuteScriptAsync(string script)
        {
            if (string.IsNullOrEmpty(script)) return Task.FromResult(string.Empty);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_windowsHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Windows.NativeWebViewShim.ExecuteJs(_windowsHandle, script);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ExecuteJs (Windows shim) failed: {ex}");
                    }
                }
                return Task.FromResult(string.Empty);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Linux.NativeWebViewShim.ExecuteJs(_linuxHandle, script);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ExecuteJs (Linux shim) failed: {ex}");
                    }
                }
                return Task.FromResult(string.Empty);
            }

            return Task.FromResult(string.Empty);
        }

        // Resize helper: call shim to adjust native child bounds
        internal void ResizeNative(int x, int y, int width, int height)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_windowsHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Windows.NativeWebViewShim.ResizeWebView(_windowsHandle, x, y, width, height);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Resize (Windows shim) failed: {ex}");
                    }
                }
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    try
                    {
                        Platforms.Linux.NativeWebViewShim.ResizeWebView(_linuxHandle, x, y, width, height);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Resize (Linux shim) failed: {ex}");
                    }
                }
                return;
            }
        }

        #endregion

        private void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is Uri newUri)
            {
                Navigate(newUri);
            }
        }
    }
}