using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaIsland.Services;

internal sealed class AgentActivity
{
    public string App { get; set; } = "";
    public string Model { get; set; } = "";
    public string Task { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public string Tool { get; set; } = "";
    public string Permission { get; set; } = "Not reported";
    public string Sandbox { get; set; } = "Not reported";
    public string Network { get; set; } = "Not reported";
    public string Workspace { get; set; } = "";
    public string Session { get; set; } = "";
    public string Source { get; set; } = "Local session";
    public DateTimeOffset Updated { get; set; }
    public bool NeedsAttention => Status is "waiting" or "completed" or "failed";
    internal HashSet<string> PendingPermissions { get; } = new();
}

internal sealed class AgentActivityReader
{
    private sealed class Cursor
    {
        public long Offset;
        public AgentActivity Activity = new();
        public DateTime LastWrite;
    }
    private readonly Dictionary<string, Cursor> cursors = new(StringComparer.OrdinalIgnoreCase);
    private string[] paths = [];
    private DateTime nextDiscovery;

    public AgentActivity[] Read(string profile)
    {
        if (DateTime.UtcNow >= nextDiscovery)
        {
            nextDiscovery = DateTime.UtcNow.AddSeconds(10);
            var candidates = new List<FileInfo>();
            foreach (string root in new[] { Path.Combine(profile, ".codex", "sessions"), Path.Combine(profile, ".claude", "projects") })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    candidates.AddRange(new DirectoryInfo(root).EnumerateFiles("*.jsonl", new EnumerationOptions
                    { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                        .Take(10000).Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-2)));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            paths = candidates.OrderByDescending(f => f.LastWriteTimeUtc).Take(40)
                .Where(f => IsUserSession(f.FullName)).Take(8).Select(f => f.FullName).ToArray();
            foreach (string old in cursors.Keys.Except(paths).ToArray()) cursors.Remove(old);
        }
        var result = new List<AgentActivity>();
        foreach (string path in paths)
        {
            try
            {
                if (!cursors.TryGetValue(path, out var cursor))
                {
                    cursor = new Cursor { Activity = new AgentActivity
                    { App = path.Contains(".codex", StringComparison.OrdinalIgnoreCase) ? "Codex" : "Claude", Session = Path.GetFileNameWithoutExtension(path) } };
                    cursors[path] = cursor;
                }
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc != cursor.LastWrite || info.Length != cursor.Offset)
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (cursor.Offset > stream.Length) { cursor.Offset = 0; cursor.Activity = new() { App = cursor.Activity.App }; }
                    // Bounded reads: avoid loading large tool outputs or entire chat histories.
                    long start = Math.Max(cursor.Offset, stream.Length - 1_048_576);
                    stream.Position = start;
                    byte[] buffer = new byte[(int)(stream.Length - start)];
                    int length = stream.Read(buffer, 0, buffer.Length);
                    int end = length > 0 ? Array.LastIndexOf(buffer, (byte)'\n', length - 1, length) : -1;
                    if (end >= 0)
                    {
                        string chunk = Encoding.UTF8.GetString(buffer, 0, end + 1);
                        if (start > cursor.Offset) chunk = chunk[(chunk.IndexOf('\n') + 1)..];
                        foreach (string line in chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            ApplyLine(cursor.Activity, line);
                        cursor.Offset = start + end + 1;
                    }
                    cursor.LastWrite = info.LastWriteTimeUtc;
                }
                if (cursor.Activity.Updated != default) result.Add(CloneForDisplay(cursor.Activity));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // Optional local activity producers (Claude hooks, editor integrations).
        string feedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaIsland", "Activity");
        if (Directory.Exists(feedRoot))
        {
            foreach (string path in Directory.EnumerateFiles(feedRoot, "*.json").Take(20))
            {
                try
                {
                    if (new FileInfo(path).Length > 32768) continue;
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var feed = document.RootElement;
                    if (!DateTimeOffset.TryParse(S(feed, "updated"), out var updated) || updated > DateTimeOffset.UtcNow.AddMinutes(1) || updated < DateTimeOffset.UtcNow.AddHours(-12)) continue;
                    var activity = new AgentActivity { App = Clean(S(feed, "app"), 40), Task = Clean(S(feed, "task")),
                        Model = Clean(S(feed, "model"), 80), Tool = Clean(S(feed, "tool"), 60), Session = Clean(S(feed, "session"), 150),
                        Permission = Clean(S(feed, "permission"), 90), Workspace = Clean(S(feed, "workspace"), 180),
                        Status = S(feed, "status") is "working" or "waiting" or "completed" or "failed" or "idle" ? S(feed, "status") : "unknown",
                        Source = "Local activity feed", Updated = updated };
                    if (activity.App.Length > 0) result.Add(CloneForDisplay(activity));
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            }
        }
        return result.OrderByDescending(a => a.Status == "waiting").ThenByDescending(a => a.Updated).Take(8).ToArray();
    }

    private static bool IsUserSession(string path)
    {
        if (!path.Contains(".codex", StringComparison.OrdinalIgnoreCase)) return !path.Contains("subagents", StringComparison.OrdinalIgnoreCase);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string? line = reader.ReadLine();
            if (line == null || line.Length > 1_048_576) return false;
            using var document = JsonDocument.Parse(line);
            return IsUserSource(O(O(document.RootElement, "payload"), "source"));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    internal static bool IsUserSource(JsonElement source) => source.ValueKind == JsonValueKind.String &&
        source.GetString() is "vscode" or "cli" or "exec" or "appServer" or "desktop";

    private static AgentActivity CloneForDisplay(AgentActivity a) => new()
    {
        App = a.App, Model = a.Model, Task = a.Task, Tool = a.Tool, Permission = a.Permission,
        Sandbox = a.Sandbox, Network = a.Network, Workspace = a.Workspace, Session = a.Session,
        Source = a.Source, Updated = a.Updated,
        Status = DateTimeOffset.UtcNow - a.Updated > TimeSpan.FromMinutes(5) && a.Status is "working" or "waiting" ? "stale" : a.Status
    };

    // Only display explicit user/task, tool, model and policy fields. Reasoning,
    // tool arguments/results, credentials and assistant private content are never surfaced.
    internal static void ApplyLine(AgentActivity activity, string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (DateTimeOffset.TryParse(S(root, "timestamp"), out var timestamp)) activity.Updated = timestamp;
            string type = S(root, "type");
            var p = O(root, "payload");
            if (type == "turn_context")
            {
                activity.Model = Clean(S(p, "model"), 80);
                activity.Workspace = Clean(S(p, "cwd"), 180);
                activity.Permission = S(p, "approval_policy") switch
                { "never" => "No approval prompts", "on-request" => "Ask when needed", "untrusted" => "Ask for untrusted actions", "on-failure" => "Ask after failure", _ => "Not reported" };
                var sandbox = O(p, "sandbox_policy");
                activity.Sandbox = S(sandbox, "type") switch
                { "workspace-write" => "Workspace write", "read-only" => "Read only", "danger-full-access" => "Full access", _ => "Not reported" };
                var network = O(sandbox, "network_access");
                activity.Network = network.ValueKind is JsonValueKind.True or JsonValueKind.False ? network.ValueKind == JsonValueKind.True ? "Network allowed" : "Network restricted" : "Not reported";
            }
            if (type == "event_msg")
            {
                switch (S(p, "type"))
                {
                    case "task_started": activity.Status = "working"; activity.PendingPermissions.Clear(); activity.Tool = ""; break;
                    case "task_complete": activity.Status = "completed"; activity.PendingPermissions.Clear(); activity.Tool = ""; break;
                    case "turn_aborted": activity.Status = "idle"; activity.PendingPermissions.Clear(); break;
                    case "user_message": SetTask(activity, S(p, "message")); break;
                    case "approval_requested": case "permission_requested": activity.Status = "waiting"; break;
                    case "approval_resolved": case "permission_resolved": activity.Status = "working"; break;
                }
            }
            if (type == "response_item")
            {
                string itemType = S(p, "type");
                if (itemType == "message" && S(p, "role") == "user") SetTask(activity, ContentText(O(p, "content")));
                if (itemType is "function_call" or "custom_tool_call")
                {
                    activity.Tool = Clean(S(p, "name").Replace("functions.", ""), 60);
                    if (activity.PendingPermissions.Count == 0) activity.Status = "working";
                    string args = S(p, "arguments") + S(p, "input");
                    if (Regex.IsMatch(args, "[\\\"']?sandbox_permissions[\\\"']?\\s*:\\s*[\\\"']require_escalated[\\\"']"))
                    {
                        activity.PendingPermissions.Add(S(p, "call_id"));
                        activity.Status = "waiting";
                    }
                }
                if (itemType is "function_call_output" or "custom_tool_call_output")
                {
                    activity.PendingPermissions.Remove(S(p, "call_id"));
                    if (activity.PendingPermissions.Count == 0) activity.Status = "working";
                }
            }
            if (activity.App == "Claude" && type is "user" or "assistant")
            {
                var message = O(root, "message");
                string model = S(message, "model");
                if (model.Length > 0) activity.Model = Clean(model, 80);
                if (type == "user" && !root.TryGetProperty("isMeta", out _)) SetTask(activity, ContentText(O(message, "content")));
                activity.Workspace = Clean(S(root, "cwd"), 180);
                string mode = S(root, "permissionMode");
                if (mode.Length > 0) activity.Permission = Clean(mode, 80);
                var content = O(message, "content");
                if (content.ValueKind == JsonValueKind.Array)
                    foreach (var item in content.EnumerateArray())
                        if (S(item, "type") == "tool_use") { activity.Tool = Clean(S(item, "name"), 60); activity.Status = "working"; }
                if (S(message, "stop_reason") == "end_turn") { activity.Status = "completed"; activity.Tool = ""; }
            }
        }
        catch (JsonException) { /* A writer may be between records. */ }
    }

    private static void SetTask(AgentActivity activity, string value)
    {
        if (value.Contains("## My request:")) value = value[(value.LastIndexOf("## My request:", StringComparison.Ordinal) + 14)..];
        if (string.IsNullOrWhiteSpace(value) || value.TrimStart().StartsWith('<')) return;
        activity.Task = Clean(value);
        activity.Status = "working";
        activity.PendingPermissions.Clear();
    }
    private static string ContentText(JsonElement content) => content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" :
        content.ValueKind == JsonValueKind.Array ? string.Join(" ", content.EnumerateArray().Where(i => S(i, "type") is "text" or "input_text").Select(i => S(i, "text"))) : "";
    internal static string Clean(string value, int limit = 180)
    {
        string text = Regex.Replace(value, @"\s+", " ").Trim();
        return text.Length > limit ? text[..limit] + "…" : text;
    }
    internal static JsonElement O(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) ? value : default;
    internal static string S(JsonElement element, string key) => O(element, key).ValueKind == JsonValueKind.String ? O(element, key).GetString() ?? "" : "";
}
