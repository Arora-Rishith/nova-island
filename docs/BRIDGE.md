# WebView2 bridge v1

Only the local document `https://nova.local/index.html` can send accepted messages. External navigation, frames, downloads, host objects, and browser permission requests are blocked. The frontend loads local assets under a restrictive Content Security Policy.

Request:

```json
{"id":"1","action":"radio.setWifi","payload":{"enabled":true}}
```

Response:

```json
{"type":"response","id":"1","ok":true,"data":null}
```

Failures use `ok:false` and an `error` string. IDs are nonempty strings of at most 64 characters. Messages are limited to 16 KB and eight JSON nesting levels. Payloads are objects; numeric ranges, boolean types, string lengths and action names are checked in C#. No arbitrary command execution is exposed.

| Action | Payload |
| --- | --- |
| `app.ready`, `app.getState`, `app.exit` | `{}` |
| `clipboard.dismiss` | `{}` |
| `voice.start`, `voice.stop` | `{}` |
| `shortcut.open` | `{"id":"calculator"}`; `files`, `calculator`, `notes`, or `settings` |
| `ai.refresh`, `ai.open` | `{}`; refresh status or launch the configured OpenCode workspace |
| `window.expand`, `window.collapse` | `{}` |
| `window.navigate` | `{"page":0}`; integer 0–6 |
| `media.playPause`, `media.previous`, `media.next` | `{}` |
| `media.seek` | `{"seconds":30}`; within the current timeline |
| `quickSettings.setVolume` | `{"value":50}`; 0–100 |
| `quickSettings.setBrightness` | `{"value":50}`; integer 0–100 |
| `radio.setWifi`, `radio.setBluetooth`, `hotspot.set` | `{"enabled":true}` |
| `wifi.scan` | `{}` |
| `wifi.connect` | `{"id":"from scan","password":""}`; blank uses a saved profile |
| `bluetooth.scan`, `bluetooth.stop` | `{}` |
| `bluetooth.pair` | `{"id":"from discovery"}` |
| `system.refresh`, `devices.refresh` | `{}` |
| `notifications.enable`, `notifications.refresh`, `notifications.clear` | `{}` |
| `notifications.dismiss` | `{"id":"from snapshot"}` |
| `settings.open` | `{"page":"wifi"}` |

Allowed Settings destinations: `wifi`, `bluetooth`, `hotspot`, `airplane`, `nightlight`, `sound`, `display`, `location`, `notifications`.

Events use `{ "type": "...", "data": ... }`:

- `state`: window, media, weather, quick settings, network/device lists, system metrics, notifications, clipboard preview, voice status, app shortcuts, album accent, AI agent status and local time; published every 500 ms and after actions. Clipboard state is empty at startup and becomes available only after a later clipboard update. Hardware sampling runs separately.
- `audio`: five normalized levels and a peak, approximately 30 Hz; decays to silence independently of the current page.
- `artwork`: a bounded PNG data URI or null when the artwork changes.

Lyrics are looked up by the C# backend from LRCLIB when the media key changes. `media.lyric`, `media.lyricsStatus`, and `media.lyrics` contain the current line, source/status label, and safe text lines. Voice recognition requires a microphone-capable packaged installation and Windows speech language support; failures are returned as normal bridge errors. `ai` reports OpenCode-compatible provider/model/task context from `NOVA_AI_PROVIDER`, `NOVA_AI_MODEL`, and `NOVA_AI_TASK`, plus known local agent processes. `NOVA_AI_WORKSPACE` optionally selects the folder passed to the OpenCode launcher.

`UI/js/bridge.js` correlates responses with promises and times out uncompleted requests after 45 seconds. Display content from Windows is inserted as text, never HTML. Wi-Fi credentials are sent only to the selected Windows WLAN operation, are not returned in snapshots, and are not written to temporary profile files.
