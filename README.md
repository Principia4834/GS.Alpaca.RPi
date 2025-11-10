# Avalonia WebView Component - Windows Implementation

A native WebView control for Avalonia UI applications using Microsoft Edge WebView2 on Windows.

## Prerequisites

- .NET 8.0 SDK or later
- Windows 10/11
- Microsoft Edge WebView2 Runtime (usually pre-installed on Windows 10/11)
  - If not installed, download from: https://developer.microsoft.com/microsoft-edge/webview2/

## Project Structure

```
Solution/
├── Avalonia.Controls.WebView/          # The WebView component library
│   ├── AvaloniaWebView.cs              # Main WebView control implementation
│   └── Avalonia.Controls.WebView.csproj
└── AvaloniaWebViewTest/                # Test application
    ├── Program.cs
    ├── App.axaml
    ├── App.axaml.cs
    ├── MainWindow.axaml                # Demo window with WebView
    ├── MainWindow.axaml.cs
    └── AvaloniaWebViewTest.csproj
```

## Building the Solution

1. Open a terminal in the solution directory
2. Restore dependencies:
   ```bash
   dotnet restore
   ```
3. Build the solution:
   ```bash
   dotnet build
   ```

## Running the Test Application

```bash
cd AvaloniaWebViewTest
dotnet run
```

Or open the solution in Visual Studio 2022 or JetBrains Rider and run the `AvaloniaWebViewTest` project.

## Using the WebView Component

### In XAML

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:webview="clr-namespace:Avalonia.Controls.WebView;assembly=Avalonia.Controls.WebView">
    
    <webview:AvaloniaWebView Source="https://www.github.com" />
    
</Window>
```

### In Code-Behind

```csharp
using Avalonia.Controls.WebView;

// Create the WebView
var webView = new AvaloniaWebView();

// Navigate to a URL
webView.Navigate("https://www.github.com");

// Navigate to HTML string
webView.NavigateToString("<html><body><h1>Hello World!</h1></body></html>");

// Execute JavaScript
var result = await webView.ExecuteScriptAsync("document.title");

// Navigation methods
webView.GoBack();
webView.GoForward();
webView.Reload();

// Check navigation state
bool canGoBack = webView.CanGoBack;
bool canGoForward = webView.CanGoForward;
```

## Features

- ✅ Navigate to URLs
- ✅ Navigate to HTML strings
- ✅ Execute JavaScript
- ✅ Back/Forward navigation
- ✅ Reload page
- ✅ Navigation state tracking
- ✅ Avalonia property binding support
- ✅ Full WebView2 event access

## Architecture Notes

### NativeControlHost Pattern

The `AvaloniaWebView` extends `NativeControlHost`, which is Avalonia's pattern for embedding native platform controls:

1. **CreateNativeControlCore()**: Creates the native WebView2 control and returns its HWND handle
2. **DestroyNativeControlCore()**: Cleans up the native control when destroyed
3. **Avalonia handles positioning**: The framework automatically positions and sizes the native control

### WebView2 Initialization

WebView2 requires asynchronous initialization:
1. Creates a `CoreWebView2Environment` (connects to Edge runtime)
2. Calls `EnsureCoreWebView2Async()` to initialize the control
3. Once initialized, navigation and script execution are available

### Future Platform Support

The component is structured to support macOS (WKWebView) and Linux (WebKitGTK) in the future:
- Platform detection in `CreateNativeControlCore()`
- Separate platform-specific handler classes
- Common interface for navigation and scripting

## Known Limitations

- **Windows Only**: Current implementation only supports Windows
- **WebView2 Runtime Required**: The Edge WebView2 runtime must be installed
- **Async Initialization**: Navigation may fail if called before WebView2 is initialized

## Troubleshooting

### "WebView2 Runtime not found"
Install the WebView2 runtime from: https://developer.microsoft.com/microsoft-edge/webview2/

### "Navigation not working"
Ensure the URL includes the protocol (http:// or https://)

### "Control not visible"
Check that the WebView has non-zero width and height in the layout

## Next Steps

- Add macOS support using WKWebView
- Add Linux support using WebKitGTK
- Add more WebView2 features (DevTools, custom request handlers, etc.)
- Add Avalonia-specific events (NavigationStarted, NavigationCompleted, etc.)

## License

MIT License - See LICENSE file for details
