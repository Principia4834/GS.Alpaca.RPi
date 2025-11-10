using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Avalonia.Controls.WebView
{
    /// <summary>
    /// A cross-platform WebView control for Avalonia, currently supporting Windows via WebView2.
    /// This control extends NativeControlHost to embed a native web browser control.
    /// </summary>
    public class AvaloniaWebView : NativeControlHost
    {
        #region Styled Properties
        
        /// <summary>
        /// Defines the Source property - the URL to navigate to
        /// </summary>
        public static readonly StyledProperty<Uri?> SourceProperty =
            AvaloniaProperty.Register<AvaloniaWebView, Uri?>(nameof(Source));

        /// <summary>
        /// Gets or sets the URL that the WebView should navigate to
        /// </summary>
        public Uri? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        #endregion

        #region Private Fields

        // The native WebView2 control (Windows only for now)
        private WebView2? _webView;
        
        // Flag to track if the WebView2 environment has been initialized
        private bool _isInitialized;
        
        // Task to track async initialization
        private TaskCompletionSource<bool>? _initializationTask;

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the AvaloniaWebView control
        /// </summary>
        public AvaloniaWebView()
        {
            // Subscribe to property changes
            SourceProperty.Changed.AddClassHandler<AvaloniaWebView>((sender, e) => 
                sender.OnSourceChanged(e));
        }

        #endregion

        #region NativeControlHost Override

        /// <summary>
        /// Creates the native control. This is called by Avalonia's NativeControlHost
        /// when the control is attached to the visual tree.
        /// </summary>
        /// <param name="parent">The parent platform handle (HWND on Windows)</param>
        /// <returns>A platform handle to the created native control</returns>
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            // Ensure we're on Windows
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException(
                    "This version of AvaloniaWebView only supports Windows. " +
                    "macOS and Linux support will be added in future versions.");
            }

            // Create the WebView2 control
            _webView = new WebView2
            {
                // Set initial size - this will be updated by Avalonia's layout system
                Width = (int)Bounds.Width,
                Height = (int)Bounds.Height
            };

            // Initialize WebView2 asynchronously
            // WebView2 requires async initialization to set up the Edge runtime
            _initializationTask = new TaskCompletionSource<bool>();
            InitializeWebView2Async();

            // Return the HWND handle of the WebView2 control
            // This tells Avalonia where to position and size the native control
            return new PlatformHandle(_webView.Handle, "HWND");
        }

        /// <summary>
        /// Called when the control is being destroyed
        /// Clean up the native WebView2 control
        /// </summary>
        /// <param name="control">The platform handle to destroy</param>
        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (_webView != null)
            {
                // Dispose of the WebView2 control properly
                _webView.Dispose();
                _webView = null;
            }

            _isInitialized = false;
            base.DestroyNativeControlCore(control);
        }

        #endregion

        #region WebView2 Initialization

        /// <summary>
        /// Initializes the WebView2 control asynchronously.
        /// WebView2 requires the Edge WebView2 runtime to be installed.
        /// </summary>
        private async void InitializeWebView2Async()
        {
            try
            {
                if (_webView == null)
                    return;

                // Create a CoreWebView2Environment with default settings
                // This will use the installed Edge WebView2 runtime
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,  // Use default Edge installation
                    userDataFolder: null,            // Use default user data folder
                    options: null                    // Use default options
                );

                // Initialize the WebView2 control with the environment
                await _webView.EnsureCoreWebView2Async(environment);

                // Mark as initialized
                _isInitialized = true;
                _initializationTask?.SetResult(true);

                // Subscribe to WebView2 events
                SubscribeToWebViewEvents();

                // Navigate to the initial source if one was set
                if (Source != null)
                {
                    Navigate(Source);
                }
            }
            catch (Exception ex)
            {
                // Log or handle initialization errors
                System.Diagnostics.Debug.WriteLine($"WebView2 initialization failed: {ex.Message}");
                _initializationTask?.SetException(ex);
            }
        }

        /// <summary>
        /// Subscribes to WebView2 events for handling navigation, errors, etc.
        /// </summary>
        private void SubscribeToWebViewEvents()
        {
            if (_webView?.CoreWebView2 == null)
                return;

            // Handle navigation starting
            _webView.CoreWebView2.NavigationStarting += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Navigating to: {e.Uri}");
                // You can cancel navigation here if needed: e.Cancel = true;
            };

            // Handle navigation completed
            _webView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                if (e.IsSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("Navigation completed successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Navigation failed: {e.WebErrorStatus}");
                }
            };

            // Handle DOM content loaded
            _webView.CoreWebView2.DOMContentLoaded += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("DOM content loaded");
            };
        }

        #endregion

        #region Navigation Methods

        /// <summary>
        /// Navigates to the specified URL
        /// </summary>
        /// <param name="url">The URL to navigate to</param>
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

        /// <summary>
        /// Navigates to the specified URI
        /// </summary>
        /// <param name="uri">The URI to navigate to</param>
        public async void Navigate(Uri uri)
        {
            if (uri == null)
                return;

            // Wait for initialization if not complete
            if (!_isInitialized && _initializationTask != null)
            {
                await _initializationTask.Task;
            }

            // Navigate using the WebView2 control
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate(uri.ToString());
            }
        }

        /// <summary>
        /// Navigates to the specified HTML string
        /// </summary>
        /// <param name="html">The HTML content to display</param>
        public async void NavigateToString(string html)
        {
            if (string.IsNullOrEmpty(html))
                return;

            // Wait for initialization if not complete
            if (!_isInitialized && _initializationTask != null)
            {
                await _initializationTask.Task;
            }

            // Load HTML content
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.NavigateToString(html);
            }
        }

        /// <summary>
        /// Executes JavaScript in the WebView
        /// </summary>
        /// <param name="script">The JavaScript code to execute</param>
        /// <returns>The result of the script execution</returns>
        public async Task<string> ExecuteScriptAsync(string script)
        {
            // Wait for initialization if not complete
            if (!_isInitialized && _initializationTask != null)
            {
                await _initializationTask.Task;
            }

            if (_webView?.CoreWebView2 != null)
            {
                return await _webView.CoreWebView2.ExecuteScriptAsync(script);
            }

            return string.Empty;
        }

        /// <summary>
        /// Reloads the current page
        /// </summary>
        public void Reload()
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Reload();
            }
        }

        /// <summary>
        /// Navigates back in the browser history
        /// </summary>
        public void GoBack()
        {
            if (_webView?.CoreWebView2?.CanGoBack == true)
            {
                _webView.CoreWebView2.GoBack();
            }
        }

        /// <summary>
        /// Navigates forward in the browser history
        /// </summary>
        public void GoForward()
        {
            if (_webView?.CoreWebView2?.CanGoForward == true)
            {
                _webView.CoreWebView2.GoForward();
            }
        }

        /// <summary>
        /// Gets whether the WebView can navigate back
        /// </summary>
        public bool CanGoBack => _webView?.CoreWebView2?.CanGoBack ?? false;

        /// <summary>
        /// Gets whether the WebView can navigate forward
        /// </summary>
        public bool CanGoForward => _webView?.CoreWebView2?.CanGoForward ?? false;

        #endregion

        #region Property Change Handlers

        /// <summary>
        /// Handles changes to the Source property
        /// </summary>
        private void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is Uri newUri)
            {
                Navigate(newUri);
            }
        }

        #endregion
    }
}