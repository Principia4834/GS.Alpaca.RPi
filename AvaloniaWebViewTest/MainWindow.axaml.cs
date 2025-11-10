using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Controls.WebView;

namespace AvaloniaWebViewTest
{
    /// <summary>
    /// Main window demonstrating the AvaloniaWebView control
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Set up a timer to update navigation buttons
            // This demonstrates checking the WebView's navigation state
            var timer = new System.Timers.Timer(500);
            timer.Elapsed += (s, e) => UpdateNavigationButtons();
            timer.Start();
        }

        /// <summary>
        /// Updates the enabled state of navigation buttons based on WebView state
        /// </summary>
        private void UpdateNavigationButtons()
        {
            // Access UI elements on the UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (WebView != null)
                {
                    BackButton.IsEnabled = WebView.CanGoBack;
                    ForwardButton.IsEnabled = WebView.CanGoForward;
                }
            });
        }

        /// <summary>
        /// Handles the Navigate button click - navigates to the URL in the textbox
        /// </summary>
        private void NavigateButton_Click(object? sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text;
            
            if (string.IsNullOrWhiteSpace(url))
                return;

            // Add http:// if no protocol is specified
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            try
            {
                WebView.Navigate(url);
            }
            catch (Exception ex)
            {
                // In a real app, you'd show this in a dialog
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the Back button click
        /// </summary>
        private void BackButton_Click(object? sender, RoutedEventArgs e)
        {
            WebView.GoBack();
        }

        /// <summary>
        /// Handles the Forward button click
        /// </summary>
        private void ForwardButton_Click(object? sender, RoutedEventArgs e)
        {
            WebView.GoForward();
        }

        /// <summary>
        /// Handles the Reload button click
        /// </summary>
        private void ReloadButton_Click(object? sender, RoutedEventArgs e)
        {
            WebView.Reload();
        }

        /// <summary>
        /// Demonstrates executing JavaScript in the WebView
        /// This example shows an alert dialog
        /// </summary>
        private async void ExecuteScriptButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Execute a simple JavaScript alert
                await WebView.ExecuteScriptAsync("alert('Hello from Avalonia WebView!');");
                
                // Example: Get page title
                var titleScript = "document.title";
                var title = await WebView.ExecuteScriptAsync(titleScript);
                System.Diagnostics.Debug.WriteLine($"Page title: {title}");
                
                // Example: Change background color
                // await WebView.ExecuteScriptAsync("document.body.style.backgroundColor = '#f0f0f0';");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Script execution error: {ex.Message}");
            }
        }
    }
}