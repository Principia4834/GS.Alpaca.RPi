// Cross-platform Avalonia WebView control.
// Uses native shims on Windows and Linux for navigation and scripting.

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Controls.WebView.Platforms.Linux;
using Avalonia.Controls.WebView.Platforms.Windows;

namespace Avalonia.Controls.WebView
{
    public class AvaloniaWebView : NativeControlHost
    {
        public static readonly StyledProperty<Uri?> SourceProperty =
            AvaloniaProperty.Register<AvaloniaWebView, Uri?>(nameof(Source));

        public Uri? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        private IntPtr _linuxHandle = IntPtr.Zero;
        private IntPtr _windowsHandle = IntPtr.Zero;

        public AvaloniaWebView()
        {
            SourceProperty.Changed.AddClassHandler<AvaloniaWebView>((sender, e) => sender.OnSourceChanged(e));
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                IntPtr parentHwnd = parent?.Handle ?? IntPtr.Zero;
                int x = 0, y = 0;
                int width = Math.Max(1, (int)Bounds.Width);
                int height = Math.Max(1, (int)Bounds.Height);

                _windowsHandle = Platforms.Windows.NativeWebViewShim.CreateWebView(parentHwnd, x, y, width, height);
                return new PlatformHandle(parentHwnd, "HWND");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IntPtr parentXid = parent?.Handle ?? IntPtr.Zero;
                int x = 0, y = 0;
                int width = Math.Max(1, (int)Bounds.Width);
                int height = Math.Max(1, (int)Bounds.Height);

                _linuxHandle = Platforms.Linux.NativeWebViewShim.CreateWebView(parentXid, x, y, width, height);
                return new PlatformHandle(parentXid, "XID");
            }

            throw new PlatformNotSupportedException("Unsupported platform for AvaloniaWebView");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_windowsHandle != IntPtr.Zero)
                {
                    try { Platforms.Windows.NativeWebViewShim.DestroyWebView(_windowsHandle); } catch { }
                    _windowsHandle = IntPtr.Zero;
                }
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    try { Platforms.Linux.NativeWebViewShim.DestroyWebView(_linuxHandle); } catch { }
                    _linuxHandle = IntPtr.Zero;
                }
                return;
            }

            base.DestroyNativeControlCore(control);
        }

        // Navigation API required by the tests

        public bool CanGoBack
        {
            get
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
                        return Platforms.Windows.NativeWebViewShim.CanGoBack(_windowsHandle) != 0;

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
                        return Platforms.Linux.NativeWebViewShim.CanGoBack(_linuxHandle) != 0;
                }
                catch (DllNotFoundException) { }
                catch (Exception ex) { Debug.WriteLine($"CanGoBack check failed: {ex}"); }
                return false;
            }
        }

        public bool CanGoForward
        {
            get
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
                        return Platforms.Windows.NativeWebViewShim.CanGoForward(_windowsHandle) != 0;

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
                        return Platforms.Linux.NativeWebViewShim.CanGoForward(_linuxHandle) != 0;
                }
                catch (DllNotFoundException) { }
                catch (Exception ex) { Debug.WriteLine($"CanGoForward check failed: {ex}"); }
                return false;
            }
        }

        public void GoBack()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
                {
                    Platforms.Windows.NativeWebViewShim.GoBack(_windowsHandle);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
                {
                    Platforms.Linux.NativeWebViewShim.GoBack(_linuxHandle);
                    return;
                }
            }
            catch (DllNotFoundException) { }
            catch (Exception ex) { Debug.WriteLine($"GoBack failed: {ex}"); }
        }

        public void GoForward()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
                {
                    Platforms.Windows.NativeWebViewShim.GoForward(_windowsHandle);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
                {
                    Platforms.Linux.NativeWebViewShim.GoForward(_linuxHandle);
                    return;
                }
            }
            catch (DllNotFoundException) { }
            catch (Exception ex) { Debug.WriteLine($"GoForward failed: {ex}"); }
        }

        public void Reload()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
                {
                    Platforms.Windows.NativeWebViewShim.Reload(_windowsHandle);
                    return;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
                {
                    Platforms.Linux.NativeWebViewShim.Reload(_linuxHandle);
                    return;
                }
            }
            catch (DllNotFoundException) { }
            catch (Exception ex) { Debug.WriteLine($"Reload failed: {ex}"); }
        }

        public void Navigate(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new ArgumentException("Invalid URL format", nameof(url));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
            {
                Platforms.Windows.NativeWebViewShim.NavigateWebView(_windowsHandle, uri.ToString());
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
            {
                Platforms.Linux.NativeWebViewShim.NavigateWebView(_linuxHandle, uri.ToString());
                return;
            }
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (string.IsNullOrEmpty(script)) return string.Empty;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_windowsHandle != IntPtr.Zero)
                    Platforms.Windows.NativeWebViewShim.ExecuteJs(_windowsHandle, script);
                return string.Empty;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                    Platforms.Linux.NativeWebViewShim.ExecuteJs(_linuxHandle, script);
                return string.Empty;
            }

            return string.Empty;
        }

        private void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is Uri newUri)
            {
                Navigate(newUri);
            }
        }
    }
}