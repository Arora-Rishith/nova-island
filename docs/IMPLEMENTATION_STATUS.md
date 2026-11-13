# Implementation status — 14 November 2026

## Interaction and live-activity follow-up

- Fixed missed WebView pointer exits with a native cursor watchdog (16 ms while expanded), including rounded corners and transparent padding. Removed the frontend collapse delay; contraction begins immediately and takes 280 ms. Duplicate collapse requests no longer cancel the host-shrink timer.
- Switched to vertical page motion and a vertical navigation rail, with Up/Down keys and scrolling through nested content before changing pages. Preserved Control Centre's internal layout while removing redundant headings elsewhere.
- Fixed the reversed artist/title arguments in lyrics lookup. Added safe title normalization, retry UI, always-accessible lyrics, current-line highlighting, multiple LRC timestamps and offsets. Plain lyrics no longer receive fabricated timing.
- Added automatic AI process discovery and bounded local Codex/Claude activity readers. The live UI displays actual task, model, tool, approval, filesystem and network fields where reported. Internal review/subagent logs are excluded. Other apps require an activity feed for task-level information; process detection alone is explicitly labelled.
- Added a monotonic focus timer with pause/resume and a compact Attention badge. No external AI inference or API credits are used by these features.
- Verified native Windows pointer exit with the UI tool; tested eight page layouts, synthetic interactions, focus controls, lyrics and reduced motion through WebView. Isolated checks cover palette extraction, cursor geometry, lyric parsing, AI policy/permission transitions, internal-session exclusion and timer accuracy.
- `--devtools` instances appear in the taskbar for native inspection. Normal launches retain the desktop-island behavior. See `docs/AI_ACTIVITY.md` for integration details and limitations.

## Reference redesign and fixes

- Preserved the Control Centre markup and page styling. Added `UI/styles/desktop.css` for the surrounding shell and the other six pages.
- Added a visible outer album halo with transparent host padding. A hue histogram replaces whole-image averaging so opposing artwork colours do not become muddy grey. Neutral art uses a neutral glow; no media uses soft lavender.
- Replaced per-frame native window resizing with interruptible browser transitions and fixed-size content layers. Host width stays at 464 DIP; height grows before expansion and shrinks after contraction. Native hit testing excludes the padding and inactive area.
- Audio animates bar transforms and halo opacity instead of bar height and border paint. Hidden native audio visuals and redundant text, lyric, and agent process rendering are skipped.
- Restored visible lyrics controls, preserved lyric scroll position during state updates, contained long titles, made agent content scrollable, repaired clipboard dismissal, and restored the correct native fallback page when WebView fails.
- Real WebView integration checks cover all seven pages, long track content, calendar navigation, bridge validation, safe rendering, live memory readings, rapid animation reversals, pointer entry/exit, inert collapsed controls, and reduced-motion preferences. Six independent palette checks cover mixed colours, neutrals, transparency, dark art, and missing pixels.
- Screenshots are in `artifacts/screenshots/`. Frame timing is diagnostic and hardware-dependent; test runs verify a stable host width and correct final animation state rather than promising a universal frame rate.

The installed-package and physical-device checks listed below remain separate from this desktop UI verification. Existing MSIX artifacts have not been repackaged for this redesign.

The original handoff describes the pre-migration project. This file records the work completed against the actual `D:\NovaIsland` source.

## Completed and checked

- Inspected the full handoff and actual source; baseline build passed with zero warnings/errors.
- Created a full pre-migration ZIP backup outside the workspace before editing.
- Added WebView2 1.0.4314.35, a transparent WPF composition host, local assets, restricted navigation and a validated bidirectional JSON bridge.
- Confirmed the initial HTML host and real JS/C# expand/collapse requests before adding the pages.
- Implemented seven web pages, black reference-inspired dashboard styling, integrated connectivity arrows, keyboard/wheel navigation, hover expansion and whole-island audio response.
- Reworked the WebView2 presentation into a compact Apple-style acrylic shell with spring motion, album-derived tinting, dynamic lighting, and redesigned pill/icon controls.
- Added app shortcuts, native clipboard previews, optional Windows voice commands, LRCLIB lyrics lookup, synced lyric progression, and an OpenCode AI Agent page with process/context status.
- Removed the song card from Control Centre while preserving its connectivity, display, sound, hotspot, airplane and shortcut controls.
- Rebuilt the music page around the reference pattern with a black/red player card, artwork row, lyric line, seek rail, transport controls and output badge; the shell is opaque black and the calendar uses a contained six-row grid.
- Clipboard previews now establish a silent startup baseline and only appear for later clipboard update messages; text, image and file copies have distinct labels.
- Hover behavior is now the only WebView expansion control: fast expansion on entry, fast collapse on exit, directional page transitions, and no manual collapse/quit buttons.
- Compact mode now combines media, local time and a weather snapshot; weather is fetched through Windows location plus Open-Meteo, with coordinate environment variables available for controlled setups.
- Media colors are published from the album extraction and applied to the music card, bars, badge and lighting instead of using a fixed red accent.
- Preserved Windows media sessions, artwork, transport controls, seeking, NAudio capture, volume and brightness integration.
- Replaced localized Wi-Fi text parsing and temporary password files with native WLAN APIs; connection labels now use Windows connection profiles.
- Corrected Bluetooth tile/picker status mix-ups; added classic/BLE connection reporting, discovery updates and bounded watcher lifetime.
- Added real CPU, memory, GPU 3D-engine, battery/power collection, monthly calendar navigation, and device discovery.
- Added permission-aware Windows notification history plus local NOVA notices; dismissal and clearing operate on known displayed IDs.
- Added hotspot capability/state/error reporting, serialized changes and Settings fallback.
- Created a self-contained x64 MSIX using official SDK tools and a manifest with the required capabilities. MakeAppx validation/package creation passed.
- Final development build passed with zero warnings/errors.
- `node tests/webview-smoke.mjs` passed against the running native app: real bridge, feature-state snapshots, invalid-request rejection, seven pages, calendar, safe text rendering, layout, actual memory readings, rapid expansion/collapse, and no uncaught frontend errors.
- Reviewed the rendered system-page screenshot at `artifacts/screenshots/system.png`.

## Installed-package blocker

The user approved local registration. Windows rejected registration with `0x80073CFF` before and after the reported Developer Mode change. The queried development-mode registry setting was absent; the machine's developer/sideloading configuration must be resolved before installation testing can proceed. No successful registration is claimed.

Built package: `artifacts/NovaIsland_1.0.0.0_x64.msix` (unsigned; rebuilt after the dynamic album color, weather pill, hover and concise page labels work).

Validated staging manifest: `artifacts/msix-stage-195a0fb6/AppxManifest.xml`.

See `Packaging/README.md` for developer registration and signing.

## Checks requiring user hardware or Windows consent

- Successful hotspot start/stop from the installed package.
- Windows notification consent, actual toast history and dismissal in the installed package.
- Joining a Wi-Fi network with credentials and pairing a physical Bluetooth device.
- Physical volume/brightness changes and media playback/seek with an active media source.
- GPU counter availability on each target driver; unavailable metrics remain explicitly unknown rather than fabricated. CPU, RAM and battery were observed in the running app.

Automated verification intentionally exercises read-only state and UI actions, rather than disconnecting the user's network or changing hardware settings.

## Future scope from the handoff

- Lyrics use LRCLIB through the C# backend; offline or unavailable tracks show a clear fallback state.
- Calendar account integration and reminders are not part of the local month view.
- Direct AI task execution is not exposed; the AI page launches OpenCode when its CLI is installed and reports known agent processes plus optional `NOVA_AI_*` context.
- Night Light and Airplane Mode remain clearly labelled Windows Settings launchers.
- The native XAML fallback and existing control-backed media/audio adapters remain for staged migration. Further extraction of these adapters is optional cleanup after physical-device parity testing.
