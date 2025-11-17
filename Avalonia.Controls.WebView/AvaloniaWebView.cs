// Cross-platform Avalonia WebView control.
// Uses native shims on Windows and Linux for navigation and scripting.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.WebView.Platforms.Linux;
using Avalonia.Controls.WebView.Platforms.Windows;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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

        public event EventHandler? NavigationStateChanged;

        // native callback delegates & pinned userdata
        private Platforms.Windows.NativeWebViewShim.NavigationStateChangedCallback? _winNavCallback;
        private Platforms.Linux.NativeWebViewShim.NavigationStateChangedCallback? _linuxNavCallback;
        private GCHandle _nativeCallbackUserData;
        private bool _nativeCallbackRegistered = false;

        private void RegisterNativeNavigationCallback()
        {
            // Pass a GCHandle to 'this' as userData (so native can return it)
            _nativeCallbackUserData = GCHandle.Alloc(this);
            IntPtr userData = GCHandle.ToIntPtr(_nativeCallbackUserData);

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHandle != IntPtr.Zero)
                {
                    _winNavCallback = new Platforms.Windows.NativeWebViewShim.NavigationStateChangedCallback(NativeNavStateHandler);
                    Platforms.Windows.NativeWebViewShim.RegisterNavigationStateCallback(_winNavCallback, userData);
                    _nativeCallbackRegistered = true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _linuxHandle != IntPtr.Zero)
                {
                    _linuxNavCallback = new Platforms.Linux.NativeWebViewShim.NavigationStateChangedCallback(NativeNavStateHandler);
                    Platforms.Linux.NativeWebViewShim.RegisterNavigationStateCallback(_linuxNavCallback, userData);
                    _nativeCallbackRegistered = true;
                }
            }
            catch (DllNotFoundException) { }
            catch (Exception ex) { Debug.WriteLine($"RegisterNativeNavigationCallback failed: {ex}"); }
        }

        private static void NativeNavStateHandler(int canGoBack, int canGoForward, IntPtr userData)
        {
            try
            {
                var handle = GCHandle.FromIntPtr(userData);
                if (handle.Target is AvaloniaWebView instance)
                {
                    // Marshal to Avalonia UI thread
                    Dispatcher.UIThread.Post(() => instance.OnNavigationStateChanged(canGoBack != 0, canGoForward != 0));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NavigationState callback error: {ex}");
            }
        }

        private void OnNavigationStateChanged(bool canGoBack, bool canGoForward)
        {
            NavigationStateChanged?.Invoke(this, EventArgs.Empty);

            // If you have properties for CanGoBack/CanGoForward, set and raise notifications here.
            // e.g. SetValue(CanGoBackProperty, canGoBack);
        }

        private void UnregisterNativeNavigationCallback()
        {
            try
            {
                if (_nativeCallbackRegistered)
                {
                    // unregister by passing null callback (native implementation should handle null as unregister)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        Platforms.Windows.NativeWebViewShim.RegisterNavigationStateCallback(null, IntPtr.Zero);
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        Platforms.Linux.NativeWebViewShim.RegisterNavigationStateCallback(null, IntPtr.Zero);
                    _nativeCallbackRegistered = false;
                }

                if (_nativeCallbackUserData.IsAllocated)
                {
                    _nativeCallbackUserData.Free();
                }

                _winNavCallback = null;
                _linuxNavCallback = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UnregisterNativeNavigationCallback failed: {ex}");
            }
        }

        // Call RegisterNativeNavigationCallback immediately after successful CreateWebView
        // and UnregisterNativeNavigationCallback in DestroyNativeControlCore.

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
                Navigate(newUri.ToString());
            }
        }
    }
}