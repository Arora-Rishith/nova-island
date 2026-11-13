using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NovaIsland;

public partial class MainWindow
{
    private const string UiOrigin = "https://nova.local";
    private WebView2CompositionControl? webView;
    private bool webReady;
    private bool isClosed;
    private bool? pendingExpansion;
    // Keep the browser's width stable. Only resize the host before opening and
    // after closing; CSS owns the motion, so WPF never relayouts it every frame.
    private const double WebGlowPadding = 32;
    private readonly DispatcherTimer webCollapseTimer = new() { Interval = TimeSpan.FromMilliseconds(520) };
    private readonly DispatcherTimer webPointerTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    private HwndSource? webHostSource;
    private bool webTestHoldsPointer;
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    private void CheckWebPointer()
    {
        if (!webReady || !isExpanded || isClosed || webTestHoldsPointer || !GetCursorPos(out var cursor)) return;
        var point = PointFromScreen(new Point(cursor.X, cursor.Y));
        if (Services.IslandBounds.Contains(point.X - WebGlowPadding, point.Y - WebGlowPadding,
            ExpandedWidth, ExpandedHeight, 32)) return;
        StopBluetoothWatcher();
        AnimateWebIsland(false);
    }

    private void ConfigureWebHost()
    {
        Width = ExpandedWidth + WebGlowPadding * 2;
        Height = (isExpanded ? ExpandedHeight : CompactHeight) + WebGlowPadding * 2;
        CenterIsland();
        if (webHostSource == null)
        {
            webHostSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            webHostSource?.AddHook(WebHostHitTest);
            webPointerTimer.Tick += (_, _) => CheckWebPointer();
            Closed += (_, _) =>
            {
                webCollapseTimer.Stop();
                webPointerTimer.Stop();
                webHostSource?.RemoveHook(WebHostHitTest);
            };
        }
        if (isExpanded) webPointerTimer.Start();
    }

    private IntPtr WebHostHitTest(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!webReady || message != 0x0084) return IntPtr.Zero; // WM_NCHITTEST
        long position = lParam.ToInt64();
        var point = PointFromScreen(new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff)));
        double width = isExpanded ? ExpandedWidth : CompactWidth;
        double height = isExpanded ? ExpandedHeight : CompactHeight;
        var shape = new RectangleGeometry(new Rect((Width - width) / 2, WebGlowPadding, width, height), 27, 27);
        if (shape.FillContains(point)) return IntPtr.Zero;
        handled = true;
        return new IntPtr(-1); // HTTRANSPARENT: desktop remains usable around the island.
    }

    private void AnimateWebIsland(bool expand)
    {
        if (isClosed || isExpanded == expand) return;
        expandTimer.Stop();
        collapseTimer.Stop();
        webCollapseTimer.Stop();
        isExpanded = expand;
        if (expand)
        {
            Height = ExpandedHeight + WebGlowPadding * 2;
            webPointerTimer.Start();
        }
        else
        {
            webPointerTimer.Stop();
            webCollapseTimer.Start();
        }
        PublishWebState();
    }
    private readonly DispatcherTimer webStateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly JsonSerializerOptions webJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async Task InitializeWebViewAsync()
    {
        if (Environment.GetCommandLineArgs().Contains("--legacy-ui")) return;
        try
        {
            string uiFolder = Path.Combine(AppContext.BaseDirectory, "UI");
            if (!File.Exists(Path.Combine(uiFolder, "index.html")))
                throw new FileNotFoundException("The UI folder is missing. Rebuild or republish NOVA.");

            webView = new WebView2CompositionControl { Visibility = Visibility.Hidden, IsHitTestVisible = false };
            WindowRoot.Children.Add(webView);
            webCollapseTimer.Tick += (_, _) =>
            {
                webCollapseTimer.Stop();
                if (!isClosed && webReady && !isExpanded)
                    Height = CompactHeight + WebGlowPadding * 2;
            };
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaIsland", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            if (isClosed) return;
            await webView.EnsureCoreWebView2Async(environment);
            if (isClosed) return;
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            var core = webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = Environment.GetCommandLineArgs().Contains("--devtools");
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.SetVirtualHostNameToFolderMapping("nova.local", uiFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => e.Cancel = !IsUiDocument(e.Uri);
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.ProcessFailed += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!isClosed) UseLegacyUi("WebView2 stopped. The native interface has been restored.");
            }));
            core.WebMessageReceived += WebMessageReceived;
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    UseLegacyUi("Web UI could not load: " + e.WebErrorStatus);
            };
            webStateTimer.Tick += (_, _) => PublishWebState();
            core.Navigate(UiOrigin + "/index.html?v=" + Guid.NewGuid().ToString("N"));
        }
        catch (Exception ex)
        {
            UseLegacyUi("WebView2 initialization: " + ex.Message);
        }
    }

    private static bool IsUiDocument(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "nova.local" && uri.IsDefaultPort &&
        uri.AbsolutePath == "/index.html" &&
        (string.IsNullOrEmpty(uri.Query) ||
         (uri.Query.StartsWith("?v=", StringComparison.Ordinal) && uri.Query.Length < 48 &&
          uri.Query.Skip(3).All(character => char.IsLetterOrDigit(character))));

    private void UseLegacyUi(string reason)
    {
        webReady = false;
        webPointerTimer.Stop();
        webCollapseTimer.Stop();
        webStateTimer.Stop();
        Width = isExpanded ? ExpandedWidth : CompactWidth;
        Height = isExpanded ? ExpandedHeight : CompactHeight;
        CenterIsland();
        IslandShell.Visibility = Visibility.Visible;
        currentPageIndex = Math.Clamp(currentPageIndex, 0, pages.Length - 1);
        for (int index = 0; index < pages.Length; index++)
            pages[index].Visibility = index == currentPageIndex ? Visibility.Visible : Visibility.Collapsed;
        CompactView.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedView.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        CompactView.Opacity = ExpandedView.Opacity = 1;
        PageTitleText.Text = pageTitles[currentPageIndex];
        UpdateNavigationDots();
        if (webView != null)
        {
            WindowRoot.Children.Remove(webView);
            webView.Dispose();
            webView = null;
        }
        System.Diagnostics.Debug.WriteLine(reason);
            Title = "Dynamic Island — " + reason;
    }

    private void SendWebMessage(object message)
    {
        if (!webReady || isClosed || webView?.CoreWebView2 == null) return;
        try { webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, webJson)); }
        catch (InvalidOperationException) { /* WebView shutdown can race an in-flight refresh. */ }
    }

    private async void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (isClosed || !IsUiDocument(e.Source)) return;
        string? id = null;
        try
        {
            string json = e.WebMessageAsJson;
            if (json.Length > 16_384) throw new ArgumentException("Message exceeds 16 KB.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var request = document.RootElement;
            if (request.ValueKind != JsonValueKind.Object ||
                !request.TryGetProperty("id", out var requestId) || requestId.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id = requestId.GetString()) || id.Length > 64 ||
                !request.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Expected { id, action, payload }.");
            if (request.TryGetProperty("payload", out var payload) && payload.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Payload must be an object.");
            object? result = await HandleWebActionAsync(action.GetString()!, payload);
            SendWebMessage(new { type = "response", id, ok = true, data = result });
            PublishWebState();
        }
        catch (Exception ex)
        {
            SendWebMessage(new { type = "response", id, ok = false, error = ex.Message });
        }
    }

}
