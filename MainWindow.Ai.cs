using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NovaIsland.Services;

namespace NovaIsland;

public partial class MainWindow
{
    private sealed record AiSnapshot(bool Available, bool Running, string Provider, string Model, string Task,
        string Status, string Detail, string[] Processes, DateTimeOffset Updated, AgentActivity[] Activities);
    private AiSnapshot aiSnapshot = new(true, false, "AI activity", "", "", "No active apps", "", [], DateTimeOffset.Now, []);
    private readonly AgentActivityReader activityReader = new();
    private readonly DispatcherTimer aiTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool refreshingAi;
    private static readonly (string Process, string Label)[] KnownAiApps =
    [ ("codex", "Codex"), ("ChatGPT", "ChatGPT"), ("claude", "Claude"), ("cursor", "Cursor"),
      ("windsurf", "Windsurf"), ("opencode", "OpenCode") ];

    private void InitializeAiMonitor()
    {
        aiTimer.Tick += async (_, _) => await RefreshAiAsync();
        Closed += (_, _) => aiTimer.Stop();
        aiTimer.Start();
        _ = RefreshAiAsync();
    }

    private async Task RefreshAiAsync()
    {
        if (refreshingAi || isClosed) return;
        refreshingAi = true;
        try
        {
            var snapshot = await Task.Run(ReadAiSnapshot);
            if (!isClosed) aiSnapshot = snapshot;
        }
        catch (Exception ex) { Debug.WriteLine("AI status: " + ex.Message); }
        finally { refreshingAi = false; PublishWebState(); }
    }

    private AiSnapshot ReadAiSnapshot()
    {
        var running = new List<string>();
        foreach (var app in KnownAiApps)
        {
            try
            {
                var processes = Process.GetProcessesByName(app.Process);
                if (processes.Length > 0) running.Add(app.Label);
                foreach (var process in processes) process.Dispose();
            }
            catch (Exception ex) { Debug.WriteLine("AI discovery: " + ex.Message); }
        }
        var activities = activityReader.Read(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).ToList();
        foreach (string app in running)
            if (!activities.Any(a => a.App == app)) activities.Add(new AgentActivity
            { App = app, Status = "unknown", Source = "App detected", Updated = DateTimeOffset.Now });
        var current = activities.FirstOrDefault(a => a.Status == "waiting")
            ?? activities.Where(a => a.Status == "working").OrderByDescending(a => a.Updated).FirstOrDefault()
            ?? activities.OrderByDescending(a => a.Updated).FirstOrDefault();
        bool active = running.Count > 0 || activities.Any(a => a.Status is "working" or "waiting");
        return new(true, active, current?.App ?? "AI activity", current?.Model ?? "", current?.Task ?? "",
            current?.Status ?? "idle", current?.Source ?? "", running.ToArray(), DateTimeOffset.Now,
            activities.OrderByDescending(a => a == current).Take(8).ToArray());
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    private void OpenAiAgent()
    {
        // Bring the detected app forward; approvals remain in the originating app.
        foreach (var app in KnownAiApps.OrderByDescending(a => a.Label == aiSnapshot.Provider))
        {
            foreach (var process in Process.GetProcessesByName(app.Process))
            {
                using (process)
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindowAsync(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);
                    return;
                }
            }
        }
        throw new InvalidOperationException("Open your AI app to view its task or respond to a request.");
    }
}
