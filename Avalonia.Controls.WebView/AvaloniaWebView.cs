using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Avalonia.Controls.WebView.Platforms.Linux;

namespace Avalonia.Controls.WebView
{
    /// <summary>
    /// Cross-platform Avalonia WebView control. Supports Windows (WebView2) and Linux (native shim using WebKitGTK/X11).
    /// MacOS can be added similarly via a native shim or managed binding.
    /// </summary>
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

        #region Windows Fields
        private WebView2? _webView;
        private bool _isInitialized;
        private TaskCompletionSource<bool>? _initializationTask;
        #endregion

        #region Linux Fields
        private IntPtr _linuxHandle = IntPtr.Zero; // opaque pointer returned by shim
        #endregion

        public AvaloniaWebView()
        {
            SourceProperty.Changed.AddClassHandler<AvaloniaWebView>((sender, e) => sender.OnSourceChanged(e));
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            // Windows path: WebView2
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Create the WebView2 control
                _webView = new WebView2
                {
                    Width = (int)Bounds.Width,
                    Height = (int)Bounds.Height
                };

                _initializationTask = new TaskCompletionSource<bool>();
                InitializeWebView2Async();

                return new PlatformHandle(_webView.Handle, "HWND");
            }

            // Linux path: use native shim (X11 + WebKitGTK)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IntPtr parentXid = parent?.Handle ?? IntPtr.Zero;
                int x = 0;
                int y = 0;
                int width = Math.Max(1, (int)Bounds.Width);
                int height = Math.Max(1, (int)Bounds.Height);

                _linuxHandle = NativeWebViewShim.CreateWebView(parentXid, x, y, width, height);

                // Return the parent XID as the platform handle descriptor "XID" so Avalonia can attach.
                // The shim manages the actual child window and will reparent it into the provided parent.
                return new PlatformHandle(parentXid, "XID");
            }

            throw new PlatformNotSupportedException("Unsupported platform for AvaloniaWebView");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_webView != null)
                {
                    _webView.Dispose();
                    _webView = null;
                }

                _isInitialized = false;
                base.DestroyNativeControlCore(control);
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    NativeWebViewShim.DestroyWebView(_linuxHandle);
                    _linuxHandle = IntPtr.Zero;
                }

                return;
            }

            base.DestroyNativeControlCore(control);
        }

        #region WebView2 Initialization (Windows)
        private async void InitializeWebView2Async()
        {
            try
            {
                if (_webView == null)
                    return;

                var environment = await CoreWebView2Environment.CreateAsync(null, null, null);
                await _webView.EnsureCoreWebView2Async(environment);

                _isInitialized = true;
                _initializationTask?.SetResult(true);

                SubscribeToWebViewEvents();

                if (Source != null)
                {
                    Navigate(Source);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");
                _initializationTask?.SetException(ex);
            }
        }

        private void SubscribeToWebViewEvents()
        {
            if (_webView?.CoreWebView2 == null)
                return;

            _webView.CoreWebView2.NavigationStarting += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Navigating to: {e.Uri}");
            };

            _webView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                if (e.IsSuccess) System.Diagnostics.Debug.WriteLine("Navigation completed successfully");
                else System.Diagnostics.Debug.WriteLine($"Navigation failed: {e.WebErrorStatus}");
            };

            _webView.CoreWebView2.DOMContentLoaded += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("DOM content loaded");
            };
        }
        #endregion

        #region Navigation
        public void Navigate(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                Navigate(uri);
            }
            else
            {
                throw new ArgumentException("Invalid URL format", nameof(url));
            }
        }

        public async void Navigate(Uri uri)
        {
            if (uri == null) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!_isInitialized && _initializationTask != null)
                    await _initializationTask.Task;

                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.Navigate(uri.ToString());

                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                    NativeWebViewShim.NavigateWebView(_linuxHandle, uri.ToString());

                return;
            }
        }

        public async void NavigateToString(string html)
        {
            if (string.IsNullOrEmpty(html)) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!_isInitialized && _initializationTask != null)
                    await _initializationTask.Task;

                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.NavigateToString(html);

                return;
            }

            // Linux shim does not currently implement NavigateToString; you can load about:blank and then execute JS to write content.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    NativeWebViewShim.NavigateWebView(_linuxHandle, "about:blank");
                    // Optionally run script to set document.body.innerHTML
                }
                return;
            }
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!_isInitialized && _initializationTask != null)
                    await _initializationTask.Task;

                if (_webView?.CoreWebView2 != null)
                    return await _webView.CoreWebView2.ExecuteScriptAsync(script);

                return string.Empty;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                {
                    NativeWebViewShim.ExecuteJs(_linuxHandle, script);
                }

                // Shim is fire-and-forget; no return value bridge implemented
                return string.Empty;
            }

            return string.Empty;
        }

        public void Reload()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _webView?.CoreWebView2?.Reload();
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (_linuxHandle != IntPtr.Zero)
                    NativeWebViewShim.NavigateWebView(_linuxHandle, "about:blank");
                return;
            }
        }

        public void GoBack()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (_webView?.CoreWebView2?.CanGoBack == true)
                    _webView.CoreWebView2.GoBack();
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Not implemented in shim example
                return;
            }
        }

        public void GoForward()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                if (_webView?.CoreWebView2?.CanGoForward == true)
                    _webView.CoreWebView2.GoForward();
                return;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Not implemented in shim example
                return;
            }
        }

        public bool CanGoBack => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (_webView?.CoreWebView2?.CanGoBack ?? false) : false;
        public bool CanGoForward => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (_webView?.CoreWebView2?.CanGoForward ?? false) : false;
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