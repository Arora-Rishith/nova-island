using System.Diagnostics;

namespace NovaIsland;

public partial class MainWindow
{
    private static readonly object[] AppShortcuts =
    {
        new { id = "files", label = "Files", icon = "⌂" },
        new { id = "calculator", label = "Calculator", icon = "＋" },
        new { id = "notes", label = "Notes", icon = "✎" },
        new { id = "settings", label = "Settings", icon = "⌘" }
    };

    private object GetAppShortcuts() => AppShortcuts;

    private static void OpenAppShortcut(string shortcut)
    {
        ProcessStartInfo startInfo = shortcut switch
        {
            "files" => new ProcessStartInfo("explorer.exe") { UseShellExecute = false },
            "calculator" => new ProcessStartInfo("calc.exe") { UseShellExecute = false },
            "notes" => new ProcessStartInfo("notepad.exe") { UseShellExecute = false },
            "settings" => new ProcessStartInfo("ms-settings:") { UseShellExecute = true },
            _ => throw new ArgumentException("Unknown app shortcut.")
        };
        Process.Start(startInfo);
    }
}
