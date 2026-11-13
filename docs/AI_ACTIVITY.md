# Local AI activity

NOVA automatically detects Codex, Claude, ChatGPT, Cursor, Windsurf and OpenCode processes. It reads recent user-owned Codex and Claude Code session logs for reported model, task and tool activity. Codex permission policy, filesystem sandbox and network policy are shown when present. Internal review/subagent sessions are excluded. Running processes alone are labelled **App detected**; NOVA does not infer their tasks or permissions.

All display data stays on this PC. Tool arguments, tool output, private reasoning and credentials are not displayed. The task label is a shortened user request. The **Open app** button brings a detected application window forward; it does not approve requests. Activity without updates for five minutes is labelled stale rather than assumed to be complete. Session log formats can change, and permissions absent from the source are omitted.

## Claude permission notifications

For immediate Claude permission prompts, register the supplied `Integrations/claude-activity.ps1` observer in Claude's own hooks settings. NOVA does not modify your agent configuration automatically. Use the following command for UserPromptSubmit, PreToolUse, PermissionRequest, PostToolUse, PostToolUseFailure, Notification, Stop and SessionEnd:

```text
powershell.exe -NoProfile -File "D:\NovaIsland\Integrations\claude-activity.ps1"
```

Example entry to merge into the existing `hooks` object (preserve existing hooks):

```json
{
  "PermissionRequest": [{
    "matcher": "",
    "hooks": [{"type": "command", "command": "powershell.exe -NoProfile -File \"D:\\NovaIsland\\Integrations\\claude-activity.ps1\""}]
  }]
}
```

The observer reads event metadata from stdin and atomically writes a local activity file. It emits no approval decisions. See [Claude's official hook reference](https://code.claude.com/docs/en/hooks).

## Other app integrations

An app integration can atomically write UTF-8 JSON files (up to 32 KB) under `%LOCALAPPDATA%\NovaIsland\Activity`. It should include an ISO timestamp, update state after a permission decision, and mark completed/failed sessions explicitly:

```json
{"app":"OpenCode","session":"example-session","task":"Run the test suite","model":"reported model","tool":"shell","permission":"Ask before commands","status":"waiting","workspace":"D:\\Project","updated":"2026-11-14T00:00:00Z"}
```

States: `working`, `waiting`, `completed`, `failed`, `idle`, `unknown`. These files are display-only. NOVA never runs their content. OpenCode provides [session and permission events](https://opencode.ai/docs/api); an adapter must publish those events before NOVA can show its task-level activity.

## Live activities

The compact attention badge prioritizes an AI permission request, then a completed focus timer, an active focus timer, and recent AI completion. Click the compact pill to open the relevant page. No forced expansion or sounds interrupt your work. The focus timer uses elapsed time, supports pause/resume, and stays accurate across missed UI ticks; it resets when NOVA exits.

The timer and compact attention indicator follow the ongoing-activity pattern described in [Apple's Dynamic Island guide](https://support.apple.com/en-mide/guide/iphone/iph28f50d10d/ios).
