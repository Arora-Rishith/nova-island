using NovaIsland.Services;

static byte[] Pixels(params (byte R, byte G, byte B, byte A, int Count)[] colours) =>
    colours.SelectMany(c => Enumerable.Range(0, c.Count).SelectMany(_ => new[] { c.B, c.G, c.R, c.A })).ToArray();
static void Check(bool passed, string label)
{
    if (!passed) throw new Exception(label);
    Console.WriteLine("PASS: " + label);
}
var red = ArtworkPalette.Extract(Pixels((220, 35, 55, 255, 150), (20, 50, 230, 255, 60)));
Check(red.R > red.B * 2, "Dominant red and blue artwork stays red rather than muddy purple");
var teal = ArtworkPalette.Extract(Pixels((240, 240, 240, 255, 450), (15, 180, 155, 255, 100)));
Check(teal.G > teal.R * 3 && teal.B > teal.R * 3, "Neutral background does not drown out teal artwork");
var transparent = ArtworkPalette.Extract(Pixels((255, 0, 0, 0, 500), (20, 55, 220, 255, 20)));
Check(transparent.B > transparent.R * 3, "Transparent pixels do not bias the palette");
var dark = ArtworkPalette.Extract(Pixels((65, 15, 25, 255, 100)));
Check(dark.R >= 160 && dark.R > dark.G * 3, "Dark artwork is lifted without changing its hue");
var neutral = ArtworkPalette.Extract(Pixels((15, 15, 15, 255, 100)));
Check(neutral.R == neutral.G && neutral.B >= neutral.R, "Monochrome artwork uses a neutral halo");
Check(ArtworkPalette.Extract([]) == neutral, "Missing pixel data has a stable fallback");

Check(IslandBounds.Contains(200, 100, 400, 280, 32), "Native pointer check accepts the island body");
Check(!IslandBounds.Contains(401, 100, 400, 280, 32) && !IslandBounds.Contains(0, 0, 400, 280, 32), "Pointer in glow or rounded corner is outside");
Check(!IslandBounds.Contains(200, 281, 400, 280, 32), "Leaving below the island triggers collapse");
var lookup = LyricsParser.LookupUri("Artist & Guest", "Song / Title");
Check(lookup.Contains("artist_name=Artist%20%26%20Guest") && lookup.Contains("track_name=Song%20%2F%20Title"), "Lyrics lookup preserves artist/title order and escapes names");
var lyrics = LyricsParser.Parse("[offset:100]\n[00:01.20][00:05.000]Repeated line\n[00:03]Middle\n[00:99]Invalid");
Check(lyrics.Count == 3 && Math.Abs(lyrics[0].Seconds - 1.3) < .001 && lyrics[2].Text == "Repeated line", "LRC supports fractions, offset, multiple timestamps and sorted lines");
Check(LyricsParser.Parse("Plain lyrics").Count == 0, "Plain lyrics never receive invented timestamps");
Check(LyricsParser.NormalizeTitle("Song (Official Music Video)") == "Song" && LyricsParser.NormalizeTitle("Song (Live)") == "Song (Live)", "Lyrics fallback cleans video decorations without changing editions");

var activity = new AgentActivity { App = "Codex" };
AgentActivityReader.ApplyLine(activity, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}");
AgentActivityReader.ApplyLine(activity, "{\"type\":\"turn_context\",\"payload\":{\"model\":\"test-model\",\"approval_policy\":\"on-request\",\"sandbox_policy\":{\"type\":\"workspace-write\",\"network_access\":false}}}");
Check(activity.Model == "test-model" && activity.Permission == "Ask when needed" && activity.Network == "Network restricted", "AI policies come from actual session context");
AgentActivityReader.ApplyLine(activity, "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"Fix the player\"}]}}");
AgentActivityReader.ApplyLine(activity, System.Text.Json.JsonSerializer.Serialize(new { type = "response_item", payload = new { type = "function_call", name = "exec_command", call_id = "permission-1", arguments = "{\"sandbox_permissions\":\"require_escalated\"}" } }));
Check(activity.Status == "waiting" && activity.Task == "Fix the player", "Approval request marks the real task as needing attention");
AgentActivityReader.ApplyLine(activity, "{\"type\":\"response_item\",\"payload\":{\"type\":\"reasoning\",\"text\":\"PRIVATE REASONING\"}}");
Check(activity.Task == "Fix the player", "Private reasoning never becomes a task label");
AgentActivityReader.ApplyLine(activity, "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"permission-1\",\"output\":\"SECRET\"}}");
Check(activity.Status == "working" && activity.Task == "Fix the player", "Tool completion clears attention without displaying tool output");
AgentActivityReader.ApplyLine(activity, "{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}");
Check(activity.Status == "completed", "Explicit completion ends the AI activity");
AgentActivityReader.ApplyLine(activity, "{truncated");
AgentActivityReader.ApplyLine(activity, "{\"type\":\"turn_context\",\"payload\":{}}");
Check(activity.Permission == "Not reported", "Missing policies are never invented");
using var userSource = System.Text.Json.JsonDocument.Parse("\"vscode\"");
using var reviewSource = System.Text.Json.JsonDocument.Parse("{\"subagent\":{\"other\":\"guardian\"}}");
Check(AgentActivityReader.IsUserSource(userSource.RootElement) && !AgentActivityReader.IsUserSource(reviewSource.RootElement), "Internal review sessions are excluded from user activity");

var clock = new TestClock();
var focus = new FocusSession(clock);
focus.Start(1); clock.Advance(12); focus.Toggle(); clock.Advance(30);
Check(focus.Snapshot() == ("paused", 48, 60), "Focus pauses with accurate remaining time");
focus.Toggle(); clock.Advance(49);
Check(focus.Snapshot() == ("complete", 0, 60), "Focus completes after elapsed time, including missed UI ticks");
focus.Reset();
Check(focus.Snapshot().Phase == "idle", "Reset clears completed focus activity");
bool rejected = false;
try { focus.Start(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "Invalid focus durations are rejected");

sealed class TestClock : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public void Advance(int seconds) => ticks += seconds * TimeSpan.TicksPerSecond;
}
