using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NovaIsland;

public partial class MainWindow
{
    private const int WmClipboardUpdate = 0x031D;
    private HwndSource? clipboardSource;
    private IntPtr clipboardHandle;
    private string? clipboardPreview;
    private int clipboardLength;
    private string clipboardKind = "text";
    private long clipboardRevision;
    private DateTimeOffset clipboardUpdated;

    private void InitializeClipboardMonitor()
    {
        clipboardHandle = new WindowInteropHelper(this).Handle;
        if (clipboardHandle == IntPtr.Zero || !AddClipboardFormatListener(clipboardHandle)) return;
        clipboardSource = HwndSource.FromHwnd(clipboardHandle);
        clipboardSource?.AddHook(ClipboardWindowProc);
        // Establish a baseline without showing the content that was already
        // on the clipboard before NOVA started. Future clipboard messages
        // alone create the transient preview.
        try
        {
            var baseline = ReadClipboardContent();
            clipboardKind = baseline.Kind;
        }
        catch (ExternalException) { }
        catch (InvalidOperationException) { }
    }

    private IntPtr ClipboardWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmClipboardUpdate)
        {
            RefreshClipboardPreview();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void RefreshClipboardPreview()
    {
        try
        {
            var content = ReadClipboardContent();
            if (content.Preview == null)
            {
                ClearClipboardPreview();
                return;
            }
            clipboardLength = content.Length;
            clipboardKind = content.Kind;
            clipboardPreview = content.Preview.Length > 180
                ? content.Preview[..180].TrimEnd() + "…"
                : content.Preview;
            clipboardUpdated = DateTimeOffset.Now;
            clipboardRevision++;
            PublishWebState();
        }
        catch (ExternalException) { }
        catch (InvalidOperationException) { }
    }

    private static (string? Preview, int Length, string Kind) ReadClipboardContent()
    {
        if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            string value = Clipboard.GetText(TextDataFormat.UnicodeText)
                .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            return string.IsNullOrWhiteSpace(value) ? (null, 0, "text") : (value, value.Length, "text");
        }
        if (Clipboard.ContainsImage()) return ("Image copied", 0, "image");
        if (Clipboard.ContainsFileDropList())
        {
            int count = Clipboard.GetFileDropList().Count;
            return ($"{count} file{(count == 1 ? "" : "s")} copied", count, "files");
        }
        return (null, 0, "other");
    }

    private void ClearClipboardPreview()
    {
        if (clipboardPreview == null && clipboardLength == 0) return;
        clipboardPreview = null;
        clipboardLength = 0;
        clipboardKind = "text";
        clipboardUpdated = DateTimeOffset.Now;
        clipboardRevision++;
        PublishWebState();
    }

    private object GetClipboardState() => new
    {
        available = !string.IsNullOrWhiteSpace(clipboardPreview),
        preview = clipboardPreview,
        length = clipboardLength,
        kind = clipboardKind,
        revision = clipboardRevision,
        updated = clipboardUpdated
    };

    private void StopClipboardMonitor()
    {
        clipboardSource?.RemoveHook(ClipboardWindowProc);
        clipboardSource = null;
        if (clipboardHandle != IntPtr.Zero) RemoveClipboardFormatListener(clipboardHandle);
        clipboardHandle = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
