# NOVA Island — Complete Project Handoff Report

> **Implementation update (2 November 2026):** This report is the historical handoff. See `README.md` and `docs/IMPLEMENTATION_STATUS.md` for the implemented WebView2 interface, verified checks, package output and remaining Windows installation/hardware checks. The current workspace is `D:\NovaIsland`.

**Purpose:** Transfer project context to a new ChatGPT account/assistant and continue development without repeating discovery work or breaking existing functionality.

**Report date:** 1 November 2026  
**Project name:** NOVA Island  
**Local project path:** `C:\Projects\NovaIsland`  
**Current platform:** Windows 11, build reported as `26200.9711`  
**Current application technology:** C# / WPF / .NET 10  
**Development environment:** VS Code and PowerShell; Visual Studio may be needed for MSIX packaging  
**User's preferred working style:** Step-by-step instructions, exact filenames, exact search strings, and precise instructions about what to add or replace. Avoid vague directions such as “put it somewhere in the class.”

---

## 1. Executive summary

NOVA Island is a custom Windows desktop overlay inspired by Apple's Dynamic Island and Control Centre. It is intended to be an AI-capable, highly customizable desktop companion with a compact floating pill, expanded multi-page interface, media controls, audio-reactive visuals, system monitoring, quick settings, notifications, and connected-device information.

The existing application is a WPF application written in C# targeting .NET 10. The basic window, floating island, page navigation, media integration, audio capture/visualization, and several quick-setting controls already exist. The project has compiled successfully during recent development. Wi-Fi and Bluetooth radio toggle UI was added and the user reported that the radio toggles themselves work. Their displayed connection status is not yet reliably integrated with actual connected network/device information.

The code-behind had become large and difficult to maintain. A new file, `MainWindow.QuickSettings.cs`, was created and some existing settings shortcut handlers were moved into it. The user now wants feature code separated into dedicated files.

**Major architectural decision (latest):** The user agreed to change the UI implementation from WPF XAML styling to a web-based UI using **HTML + CSS + JavaScript hosted inside WebView2**, while retaining the existing **C# backend** for Windows APIs, media, audio, hardware/system information, and other OS integrations. This is a gradual migration, not a request to discard the working project or rewrite the whole backend immediately.

**Important:** The new account/assistant should inspect the actual project files before making edits. This report summarizes the conversation and known state, but it does not contain the complete source code and cannot prove that every suggested code snippet was inserted exactly as described.

---

## 2. Goals and product vision

NOVA should become a polished, Apple-inspired Windows desktop overlay with the following principles:

- Compact black Dynamic-Island-style pill that sits near the top of the screen.
- Expands on hover or interaction into a larger multi-page panel.
- Smooth, restrained transitions; no harsh default Windows hover/selection tint.
- Consistent rounded cards, buttons, toggles, icons, typography, and spacing.
- Clean white/neutral visual accent rather than an unwanted purple tint. Music may influence the island's visual reaction, but should not make the design feel inconsistent.
- The **whole island shell** should react to music/audio, not only the music page.
- Quick Settings should eventually have meaningful status and actions instead of just launching Windows Settings pages.
- Notifications should have a proper notification-centre experience.
- All features should be modular so the main window file does not become an unmaintainable monolith.
- Future AI-assisted desktop actions may be considered, but do not assume an AI agent integration is currently implemented.

The user particularly values a high-quality Apple-like finish and needs exact implementation guidance.

---

## 3. Existing development environment

### 3.1 Project creation and target

The project was originally created with:

```powershell
mkdir C:\Projects -Force
cd C:\Projects
dotnet new wpf -n NovaIsland -f net10.0
cd NovaIsland
code .
```

The project file was changed to target Windows explicitly:

```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<UseWPF>true</UseWPF>
```

.NET SDK 10 and Git were reported installed. The exact current `.csproj` must be inspected before changing it.

### 3.2 Hardware / runtime context

- Windows 11, build `26200.9711` (reported by user).
- User's laptop: NVIDIA RTX 4050, 16 GB RAM (may be relevant if any future local AI or GPU visualization features are introduced; not required for the current UI migration).
- Main project path: `C:\Projects\NovaIsland`.
- User has primarily used VS Code and PowerShell.
- Visual Studio Community is **not confirmed installed**. It may be needed for the Visual Studio Windows Application Packaging Project workflow, depending on the chosen packaging approach.

### 3.3 Build status

The user explicitly reported **“build succeded”** after adding the Wi-Fi/Bluetooth XAML blocks and related radio toggle changes.

The user later reported that NOVA builds but clicking Mobile Hotspot does not activate it. Do not interpret successful compilation as proof that the Hotspot feature works.

No current build output, full source snapshot, or repository commit was provided with this handoff.

---

## 4. Existing application structure and features

The existing main window is a WPF window with these known controls and pages. Use these names when inspecting existing files; do not invent replacements without checking the actual XAML.

### 4.1 Main window structure

Known core XAML elements:

- `IslandShell`
- `CompactView`
- `ExpandedView`
- Six page grids:
  - `PageMusic`
  - `PageSystem`
  - `PageCalendar`
  - `PageQuickSettings`
  - `PageNotifications`
  - `PageDevices`
- Navigation dots:
  - `PageDot0` through `PageDot5`
- Compact audio bars:
  - `CompactWaveBar1` through `CompactWaveBar5`
- Global audio bars:
  - `GlobalWaveBar1` through `GlobalWaveBar5`

The main window has event handlers for `Loaded`, `Closed`, `MouseEnter`, `MouseLeave`, `PreviewKeyDown`, and `PreviewMouseWheel`.

### 4.2 Known existing styles in `MainWindow.xaml`

- `TransportButtonStyle`
- `PlayButtonStyle`
- `PageDotButtonStyle`
- `QuickTileButtonStyle`
- `AppleVerticalSliderStyle`
- `AppleSeekSliderStyle`
- `PickerListBoxItemStyle`

A new `QuickTileArrowButtonStyle` was proposed and the user was instructed to add it to remove the standard Windows hover/focus look from the Wi-Fi and Bluetooth arrow buttons. It is not confirmed that this style was added successfully. Check the file.

### 4.3 Music and media integration

Known controls:

- `CompactAlbumArt`
- `CompactArtPlaceholder`
- `CompactTitle`
- `CompactArtist`
- `ExpandedAlbumArt`
- `ExpandedArtPlaceholder`
- `ExpandedTitle`
- `ExpandedArtist`
- `ExpandedAlbum`
- `PlayPauseButton`
- `SeekSlider`
- `PositionText`
- `DurationText`
- `CurrentLyricText`

Known implementation uses Windows media session APIs:

```csharp
using Windows.Media.Control;
using Windows.Storage.Streams;
```

Known methods include:

- `InitializeMediaAsync`
- `RefreshMediaAsync`
- `LoadAlbumArtAsync`
- `UpdateAlbumAccent`
- `ResetAlbumAccent`
- `ApplyAlbumAccentToUi`
- `UpdateTimeline`
- `PlayPause`
- `Previous`
- `Next`
- `ShowNoMedia`

A media timer runs about every 500 ms. The exact current method signatures should be inspected from the source.

### 4.4 Audio capture and island-wide reaction

The project uses NAudio loopback capture. Known members include:

- `WasapiRecorder? audioRecorder`
- `audioLock`
- `targetAudioBars`
- `displayedAudioBars`
- `lastAudioPacketUtc`
- `compactWaveBars`
- `globalWaveBars`
- `islandBackground`
- `islandBorderBrush`
- `islandGlow`
- `albumAccentColor`
- `albumAccentBrush`

Known implementation uses `WasapiRecorderBuilder`, loopback capture, 48 kHz float stereo format, and methods such as `ProcessAudioBuffer` and `UpdateAudioVisuals`.

The user explicitly wants **the entire island to react to music**, not just the currently visible music page. Existing implementation was described as updating the shell background, border and glow using audio level. Preserve this intent during the UI migration.

The user's requested visual direction was to remove the purple tint and use white/neutral accents, while retaining island-wide audio response. In the code snapshot previously pasted, `albumAccentColor` initialized to white but album artwork extraction could later change it. Inspect current code and adjust so the default visual language remains neutral and controlled.

### 4.5 System monitor

Known controls:

- `CpuUsageText`
- `RamUsageText`
- `GpuUsageText`
- `BatteryPercentText`
- `BatteryStatusText`

The system monitor page already exists, but its accuracy, refresh cadence, performance and GPU collection method have not been audited. Do not claim that every metric is fully implemented or reliable without checking.

### 4.6 Date and calendar

Known controls:

- `ClockText`
- `DateText`

A clock timer updates these, approximately every second. The calendar page exists, but the completeness of its calendar functionality is unknown.

---

## 5. Quick Settings — current known state

The Quick Settings page is `PageQuickSettings`. It includes:

- Wi-Fi tile and a network picker panel.
- Bluetooth tile and a device picker panel.
- Volume slider.
- Brightness slider.
- Hotspot shortcut/button.
- Airplane Mode shortcut/button.
- Night Light shortcut/button.
- Sound settings shortcut.
- Display settings shortcut.

Known controls:

- `QuickSettingsHome`
- `WifiPickerPanel`
- `BluetoothPickerPanel`
- `WifiNetworksList`
- `BluetoothListBox`
- `BluetoothStatusText`
- `BluetoothFooterText`
- `PairBluetoothButton`
- `WifiStatusText`
- `WifiPasswordBox`
- `WifiConnectButton`
- `WifiCurrentSsidText`
- `CurrentWifiText`
- `VolumeSlider`
- `BrightnessSlider`
- `VolumeValueText`
- `BrightnessValueText`

### 5.1 Wi-Fi tile XAML (confirmed by user)

The user pasted this complete block and it should be treated as the current intended structure unless the local project differs:

```xml
<Grid Grid.Row="0" Grid.Column="0" Margin="0,0,4,4">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="27"/>
    </Grid.ColumnDefinitions>

    <Button x:Name="WifiRadioButton"
            Grid.Column="0"
            Style="{StaticResource QuickTileButtonStyle}"
            Click="WifiRadioToggle_Click">
        <StackPanel Orientation="Horizontal">
            <Border x:Name="WifiIconCircle"
                    Width="30"
                    Height="30"
                    CornerRadius="15"
                    Background="#FF303038">
                <TextBlock x:Name="WifiRadioIcon"
                           Text="&#xE701;"
                           FontFamily="Segoe MDL2 Assets"
                           FontSize="16"
                           Foreground="White"
                           HorizontalAlignment="Center"
                           VerticalAlignment="Center"/>
            </Border>
            <StackPanel VerticalAlignment="Center" Margin="7,0,0,0">
                <TextBlock x:Name="WifiRadioLabel"
                           Text="Wi-Fi"
                           Foreground="White"
                           FontSize="11"
                           FontWeight="SemiBold"/>
                <TextBlock x:Name="CurrentWifiText"
                           Text="Checking..."
                           Foreground="#FFB8B8BE"
                           FontSize="9"
                           MaxWidth="85"
                           TextTrimming="CharacterEllipsis"/>
            </StackPanel>
        </StackPanel>
    </Button>

    <Button Grid.Column="1"
            Content="›"
            FontSize="21"
            Background="Transparent"
            Foreground="White"
            BorderThickness="0"
            Click="WifiTile_Click"
            ToolTip="Available networks"/>
</Grid>
```

The arrow is a separate sibling button in the same Grid. The user said it looks visually detached and receives a default Windows hover selection tint. A no-hover transparent arrow style was suggested, but the exact current XAML must be inspected. The main tile may also have hover tint from `QuickTileButtonStyle`; this style must be reviewed before reworking it.

### 5.2 Bluetooth tile XAML (confirmed by user)

```xml
<Grid Grid.Row="0" Grid.Column="1" Margin="4,0,0,4">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="27"/>
    </Grid.ColumnDefinitions>

    <Button x:Name="BluetoothRadioButton"
            Grid.Column="0"
            Style="{StaticResource QuickTileButtonStyle}"
            Click="BluetoothRadioToggle_Click">
        <StackPanel Orientation="Horizontal">
            <Border x:Name="BluetoothIconCircle"
                    Width="30"
                    Height="30"
                    CornerRadius="15"
                    Background="#FF303038">
                <TextBlock x:Name="BluetoothRadioIcon"
                           Text="&#xE702;"
                           FontFamily="Segoe MDL2 Assets"
                           FontSize="16"
                           Foreground="White"
                           HorizontalAlignment="Center"
                           VerticalAlignment="Center"/>
            </Border>
            <StackPanel VerticalAlignment="Center" Margin="7,0,0,0">
                <TextBlock x:Name="BluetoothRadioLabel"
                           Text="Bluetooth"
                           Foreground="White"
                           FontSize="11"
                           FontWeight="SemiBold"/>
                <TextBlock x:Name="BluetoothRadioStatusText"
                           Text="Checking..."
                           Foreground="#FFB8B8BE"
                           FontSize="9"/>
            </StackPanel>
        </StackPanel>
    </Button>

    <Button Grid.Column="1"
            Content="›"
            FontSize="21"
            Background="Transparent"
            Foreground="White"
            BorderThickness="0"
            Click="BluetoothTile_Click"
            ToolTip="Available devices"/>
</Grid>
```

**Important distinction:**

- `BluetoothRadioStatusText` is the Bluetooth tile's status text.
- `BluetoothStatusText` is used in the Bluetooth device-picker panel.
- `CurrentWifiText` is the Wi-Fi tile's status text.
- `WifiStatusText` is used in the Wi-Fi picker panel.

Do not globally replace these names. They refer to different controls.

### 5.3 Wi-Fi and Bluetooth implementation

Known namespaces include:

```csharp
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
```

Known fields added or proposed:

```csharp
private Radio? wifiRadio;
private Radio? bluetoothRadio;
private bool radioAccessGranted = false;
private bool radioToggleInProgress = false;
```

The radio fields were added as part of a previous step, but the current state must be verified in `MainWindow.xaml.cs` and `MainWindow.QuickSettings.cs`.

Radio methods previously introduced:

- `InitializeRadiosAsync`
- `Radio_StateChanged`
- `UpdateRadioTiles`
- `WifiRadioToggle_Click`
- `BluetoothRadioToggle_Click`
- `ToggleRadioAsync`
- `SetRadioStatus`

The exact implementations must be read from the current files. Do not overwrite them from this summary.

Wi-Fi scanning/connection uses `netsh.exe` and existing methods such as:

- `WifiTile_Click`
- `RefreshWifiNetworksAsync`
- `ParseWifiNetworks`
- `WifiNetworksList_SelectionChanged`
- `ConnectWifi_Click`
- `RefreshCurrentWifiNameAsync`
- `RunCommandAsync`

Bluetooth discovery/pairing uses `DeviceInformation.FindAllAsync`, `DeviceWatcher`, and methods such as:

- `BluetoothTile_Click`
- `StartBluetoothDiscoveryAsync`
- `BluetoothListBox_SelectionChanged`
- `PairBluetooth_Click`
- `StopBluetoothWatcher`

The user reported that Wi-Fi and Bluetooth radio toggles worked, but the displayed current Wi-Fi and Bluetooth connection status was not correctly/consistently connected to real state. A periodic `connectionStatusTimer` was proposed to refresh status every three seconds. Methods proposed:

- `RefreshConnectionStatusAsync`
- `RefreshBluetoothConnectionStatusAsync`

Those methods read `wifiRadio.State`, reuse `RefreshCurrentWifiNameAsync`, and query connected Bluetooth devices using:

```csharp
BluetoothDevice.GetDeviceSelectorFromConnectionStatus(
    BluetoothConnectionStatus.Connected)
```

Check if these methods and timer were actually added. The conversation indicates the user followed the instructions but no latest build result after all those additions was explicitly provided. Verify by compiling.

### 5.4 Volume and brightness

Known implementation:

- NAudio/CoreAudio APIs for endpoint volume.
- `MMDeviceEnumerator` and `MMDevice`.
- `VolumeSlider_ValueChanged`.
- WMI brightness using `WmiMonitorBrightness` / `WmiMonitorBrightnessMethods`.
- `BrightnessSlider_ValueChanged` and `BrightnessDebounceTimer_Tick`.
- A brightness debounce timer around 180 ms.

The user previously reported the brightness hardware control worked, and its slider animation was later improved. Do not regress the slider or its debounce behaviour. Ensure status/UI updates stay responsive during future frontend migration.

### 5.5 Night Light

Current known implementation is a Settings shortcut, not a direct toggle. The handler has been moved from `MainWindow.xaml.cs` into `MainWindow.QuickSettings.cs` (the user reported finishing the method moves). Its intended handler was:

```csharp
private void OpenNightLightSettings_Click(object sender, RoutedEventArgs e)
{
    OpenSettingsPage("ms-settings:nightlight");
}
```

Windows does not provide a straightforward supported public third-party API to directly toggle Night Light. Do not use undocumented registry hacks or promise direct toggle control without research and a reliable supported mechanism.

### 5.6 Mobile Hotspot

A direct Hotspot implementation was added/proposed in `MainWindow.QuickSettings.cs` using:

```csharp
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;
```

The implementation uses:

- `NetworkInformation.GetInternetConnectionProfile()`
- `NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile)`
- `NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile)`
- `StartTetheringAsync()` / `StopTetheringAsync()`
- `TetheringOperationalState`
- `TetheringOperationStatus`

A button handler named `ToggleHotspot_Click` was proposed, with fallback to `ms-settings:network-mobilehotspot` if the API fails.

**Observed limitation:** The project is currently run with `dotnet run` as an unpackaged WPF application. The user says the app builds, but clicking Hotspot does not actually turn it on. The likely blocker is the required package identity/capability. This has not yet been proven by capturing the exception or capability result.

The `wiFiControl` declaration must be added to a package manifest in an MSIX packaging project:

```xml
<DeviceCapability Name="wiFiControl" />
```

A previous discussion also identified these declarations for radio / Bluetooth operations:

```xml
<DeviceCapability Name="radios" />
<DeviceCapability Name="bluetooth" />
```

Do not add these to `MainWindow.xaml`, `App.xaml`, or ordinary `app.manifest`. Add them to the correct `Package.appxmanifest` in an MSIX package project and validate the correct namespace/manifest schema and capability requirements. Do not tell the user that simply adding a line to the WPF project enables the capability under `dotnet run`.

The user has not confirmed having Visual Studio Community installed. Earlier instructions proposed creating a Windows Application Packaging Project in Visual Studio, adding a project reference to the WPF app, and running the package project. This packaging flow has not been completed.

### 5.7 Airplane Mode

Currently expected to be a Settings shortcut (`ms-settings:network-airplanemode`) unless source code indicates otherwise. Direct control has not yet been completed or audited. Radio APIs and permissions may be relevant, but do not assume the same API semantics as Hotspot.

---

## 6. Code separation already started

The user requested a new file because the main code-behind had become cluttered.

A new file was created:

`MainWindow.QuickSettings.cs`

The initial skeleton was:

```csharp
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Windows.Devices.Radios;

namespace NovaIsland
{
    public partial class MainWindow
    {
        // Quick settings methods
    }
}
```

The user later confirmed they moved the following settings shortcut methods from `MainWindow.xaml.cs` into `MainWindow.QuickSettings.cs`:

- `OpenNightLightSettings_Click`
- `OpenHotspotSettings_Click`
- `OpenAirplaneSettings_Click`
- `OpenWifiSettings_Click`
- `OpenBluetoothSettings_Click`
- `OpenSoundSettings_Click`
- `OpenDisplaySettings_Click`

The original instructions said to leave `OpenSettingsPage()` in `MainWindow.xaml.cs` for now.

Then direct Hotspot API code, `ToggleMobileHotspotAsync()`, and `ToggleHotspot_Click()` were proposed in `MainWindow.QuickSettings.cs`, and the XAML hotspot click handler was to be changed from `OpenHotspotSettings_Click` to `ToggleHotspot_Click`.

**Must verify:** Current file contents, whether the original methods were cut (not duplicated), whether any handler name mismatch remains, whether the new methods are inside the partial class, and whether the project currently builds after these changes.

---

## 7. The major new decision: move the UI to HTML/CSS/JavaScript + WebView2

The user agreed to the following architecture:

**Frontend:** HTML + CSS + JavaScript  
**Desktop host / backend:** Existing WPF + C#  
**Bridge:** Microsoft Edge WebView2 JavaScript ↔ C# messaging

### 7.1 Why this decision was made

The current WPF XAML styling was becoming cumbersome to customize. The user wants cleaner, more fluid Apple-inspired animations, more control over component styling, and easier changes to tile design, hover behaviour, transitions, responsive layout, cards, notifications, and page composition.

A web frontend offers:

- CSS-driven transitions and keyframes.
- Easier control over blur, gradients, shadows, glass, rounded cards and hover states.
- Easier reusable components and design tokens.
- More natural implementation of dynamic notification cards and visual layouts.
- Faster visual iteration without repeatedly changing XAML styles.

WebView2 is preferable to replacing the entire application with Electron or Tauri because NOVA already has a C# backend with Windows API integrations. WebView2 allows the web UI to call into C# while preserving the current project and existing Windows-facing logic.

### 7.2 Target folder layout (proposed)

Suggested gradual structure:

```text
NovaIsland/
├── NovaIsland.csproj
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── MainWindow.QuickSettings.cs
├── MainWindow.Notifications.cs
├── MainWindow.Devices.cs
├── MainWindow.SystemMonitor.cs
├── MainWindow.Media.cs              # optional extraction later
├── MainWindow.Audio.cs              # optional extraction later
├── UI/
│   ├── index.html
│   ├── styles/
│   │   ├── tokens.css
│   │   ├── base.css
│   │   ├── island.css
│   │   ├── controls.css
│   │   ├── animations.css
│   │   └── pages.css
│   ├── js/
│   │   ├── app.js
│   │   ├── bridge.js
│   │   ├── state.js
│   │   ├── components/
│   │   │   ├── island.js
│   │   │   ├── quick-settings.js
│   │   │   ├── notifications.js
│   │   │   ├── media-player.js
│   │   │   └── system-monitor.js
│   │   └── pages/
│   └── assets/
│       ├── icons/
│       └── images/
└── (MSIX packaging project may be added later)
```

This is a proposed target, not an existing verified folder tree. Begin with a minimal `UI/index.html`, one CSS file and one JS bridge, then split files once the bridge is working.

### 7.3 Proposed C# bridge responsibilities

Keep the backend in C#. The frontend should request actions, not directly attempt privileged operations.

Possible bridge message groups:

- `media.getState`, `media.playPause`, `media.next`, `media.previous`, `media.seek`
- `audio.getLevels` or a throttled event stream for island-wide audio reactivity
- `quickSettings.getState`, `quickSettings.setVolume`, `quickSettings.setBrightness`
- `radio.getState`, `radio.setWifi`, `radio.setBluetooth`
- `hotspot.getState`, `hotspot.start`, `hotspot.stop`
- `notifications.get`, `notifications.dismiss`, `notifications.clear`
- `system.getMetrics`
- `devices.getConnected`
- `window.expand`, `window.collapse`, `window.navigate`

Use a documented and validated message schema (e.g. JSON with an action and payload), validate every incoming request, and return success/error responses. Never execute arbitrary C# or shell commands from JavaScript. Restrict navigation and external resource loading. Use WebView2 `CoreWebView2.WebMessageReceived` and `PostWebMessageAsJson` / corresponding supported WebView2 APIs, after verifying the installed NuGet package and API versions.

### 7.4 Migration strategy — do not rewrite everything at once

Recommended staged migration:

1. **Baseline and backup:** Make a Git commit or copy the entire project before any changes. Run `dotnet build` and note baseline errors/warnings.
2. **Install WebView2:** Add the Microsoft WebView2 WPF NuGet package after checking project target and restore compatibility. Ensure the WebView2 Runtime is available on the test PC.
3. **Add a small web UI host:** Keep WPF window, window positioning, transparency, topmost behaviour, expand/collapse, timers and backend. Embed WebView2 into the WPF window.
4. **First visual prototype:** Reproduce only the compact black pill and expanded shell in HTML/CSS. Do not port all controls yet.
5. **Bridge proof of concept:** Add one harmless bridge call, such as requesting current time or window expand/collapse, and prove JS-to-C# and C#-to-JS messaging works.
6. **Port one page:** Start with Quick Settings or the basic shell. Preserve current event handlers and backend operations behind C# bridge methods.
7. **Port media page:** Preserve Windows Media Session and NAudio functionality.
8. **Port notifications/system/devices pages.**
9. **Remove old XAML UI styles only after equivalent web UI is tested.**
10. **MSIX packaging:** Add package identity and capability declarations when the actual package project is configured. Test Hotspot/radios in the installed package.

Do not remove the existing XAML UI before the WebView2 implementation has a working build and a basic feature parity check. Consider a temporary feature flag or branch to revert quickly.

---

## 8. Planned modular code layout

Partial classes are already being used/considered. They allow feature methods to live in multiple source files while remaining part of the same `MainWindow` class and accessing its controls/fields.

Proposed files:

| File | Responsibility | Current status |
|---|---|---|
| `MainWindow.xaml.cs` | Window lifecycle, shell state, page navigation, shared initialization | Existing; large and still needs review |
| `MainWindow.QuickSettings.cs` | Wi-Fi, Bluetooth, volume, brightness, Night Light, Hotspot, Airplane Mode | Created; shortcut methods moved; new Hotspot implementation proposed; verify |
| `MainWindow.Notifications.cs` | Windows notification access/notification centre UI state and actions | Not yet created/implemented |
| `MainWindow.Devices.cs` | Device discovery, connected device information, device actions | Not yet created as a dedicated module; some Bluetooth code remains in main file |
| `MainWindow.SystemMonitor.cs` | CPU, RAM, GPU, battery metrics | Not yet created as a dedicated module |
| `MainWindow.Media.cs` | Windows media session integration | Optional future extraction |
| `MainWindow.Audio.cs` | NAudio loopback capture and audio analysis | Optional future extraction |

When moving methods, cut the old method only after confirming the new partial class file compiles. Do not duplicate method names across partial files. A partial class file must use the exact same namespace and `public partial class MainWindow` declaration.

---

## 9. Known flaws, risks and unresolved issues

### 9.1 Current UI styling / interaction flaws

Reported by the user:

- The Wi-Fi/Bluetooth arrow button visually appears separate from the main tile.
- Hovering over the arrow gives a standard Windows selection tint that clashes with the intended clean Apple-like animation.
- Tile spacing, alignment, and interaction need refinement.
- Existing WPF XAML styling is becoming too cluttered/hard to customize.
- Need a consistent minimal Apple-like font and controls, clean white/neutral accents, smooth sliders/toggles, subtle hover/press animations.

The user agreed to migrate the interface to HTML/CSS/JS inside WebView2. Do not spend a large amount of time polishing the old XAML UI; make only fixes necessary to preserve the current app until the web UI is established.

### 9.2 Wi-Fi status and Bluetooth status

Reported issue: toggles work, but displayed current Wi-Fi and Bluetooth connection statuses do not reliably reflect current connection information.

Potential issue areas to inspect:

- Initialization timing: status refresh may run before `wifiRadio` / `bluetoothRadio` fields are populated.
- Wi-Fi connection retrieval via `netsh` may parse localized or slightly different command output, may need robust process error handling, or may update a different TextBlock than the tile.
- Bluetooth query may return paired devices vs connected devices differently depending on selector, permissions and device class.
- The tile status and picker status are deliberately separate controls; updates may currently target the wrong one.
- The proposed three-second `connectionStatusTimer` may not have been added or may be started before radio initialization.
- A connected Bluetooth device name can be long; UI must truncate rather than stretch the tile.

Do not blindly replace `BluetoothRadioStatusText` with `BluetoothStatusText` or `CurrentWifiText` with `WifiStatusText`. Those pairs serve separate UI regions.

### 9.3 Hotspot does not turn on

Observed: app compiles, but clicking Hotspot does not turn it on.

Likely reason: app is still running unpackaged (`dotnet run`), and Windows tethering API requires app package identity plus `wiFiControl` capability. This is a likely diagnosis, not confirmed without capturing the actual exception and `TetheringCapability` result.

Next debugging steps:

1. Temporarily log/display exact capability result from `GetTetheringCapabilityFromConnectionProfile`.
2. Display exact result status and `AdditionalErrorMessage` for start/stop.
3. Check if no Internet Connection Profile is present or if tethering is disabled by hardware, carrier, adapter or policy.
4. Add MSIX Packaging Project and `wiFiControl` capability in `Package.appxmanifest`.
5. Also declare applicable `radios` and `bluetooth` capabilities according to current Microsoft documentation and APIs actually used.
6. Install and test packaged application; `dotnet run` will remain an unpackaged run and is not a valid test of package capability permissions.
7. Keep fallback to Windows Mobile Hotspot Settings when native control isn't available.

### 9.4 Night Light

Current feature is a settings shortcut. Do not claim it is a working direct on/off toggle. Avoid unsupported registry manipulation. Investigate any supported API only if Microsoft documents it for third-party desktop apps; otherwise use the settings shortcut or an honest “open Night Light settings” interaction.

### 9.5 MSIX packaging is not yet done

User asked whether `wiFiControl` capability needed; they were told it does, but to defer adding it until packaging. The user has not said Visual Studio is installed or that a packaging project exists.

Do not add `<DeviceCapability ...>` elements to ordinary WPF XAML or `.cs` source. Add to the correct package manifest and validate schema/namespace. A basic package may also require `runFullTrust` and appropriate package entry-point configuration, generated by the packaging project template. Do not hand-author an entire manifest without checking the project template output.

### 9.6 Code-behind clutter

The main window file is large. `MainWindow.QuickSettings.cs` exists but the rest of modularization remains incomplete. Ensure code separation is systematic, tests/build between moves, and no duplicate methods.

### 9.7 Unknown source state

This report is based on conversation context, not a direct inspection of the local repository. Some proposed changes may have been inserted, some may have been adjusted by the user, and some may not have landed. The next assistant must not assume that the snippets in this report exactly match the current source.

---

## 10. Features and implementation status

Status key:
- **Existing / reported:** Mentioned as implemented in conversation; still verify source.
- **Partial:** Some UI or code exists, but end-to-end behaviour is incomplete.
- **Planned:** Not yet implemented or not confirmed.

| Feature | Status | Notes / next action |
|---|---|---|
| Floating island shell | Existing / reported | Black pill, transparent surrounding window; verify dimensions and window positioning. |
| Compact/expanded views | Existing / reported | Hover-based expand/collapse. Verify animations and hit testing. |
| Six-page navigation | Existing / reported | Music, System, Calendar, Quick Settings, Notifications, Devices. |
| Mouse-wheel page navigation | Existing / reported | Ignores picker/list/slider controls and navigates pages; verify. |
| Media session integration | Existing / reported | Windows Media Control APIs, album art and transport controls. |
| Lyrics display | Partial / unclear | `CurrentLyricText` exists; data source/lyric implementation not audited. |
| Seek slider / timeline | Existing / reported | Validate seeking and timeline state. |
| NAudio loopback | Existing / reported | Verify device capture, sample processing and stop/dispose lifecycle. |
| Island-wide audio reaction | Existing / reported | Preserve whole-shell reaction across pages. |
| Album-derived accent | Existing / reported | User wants clean white/neutral design; review accent logic. |
| CPU/RAM/GPU/battery | Existing / partial | Metrics controls exist; accuracy/performance audit needed. |
| Clock/date | Existing / reported | Live clock timer; verify. |
| Calendar | Partial / unclear | Page exists; feature depth unknown. |
| Wi-Fi radio toggle | Existing / reported | User says radio toggle works; verify source/runtime after migration. |
| Wi-Fi network list/connection | Existing / partial | Uses `netsh`; test scanning, password handling, connection and parsing. |
| Current Wi-Fi status | Partial / broken | Reported not connected to actual current status correctly. |
| Bluetooth radio toggle | Existing / reported | User says radio toggle works; verify source/runtime. |
| Bluetooth discovery/pairing | Existing / partial | DeviceWatcher / pairing code exists; verify permission, reliability and UX. |
| Current Bluetooth connection status | Partial / broken | Reported status not correctly shown; verify actual connected-device retrieval. |
| Volume slider | Existing / reported | CoreAudio; preserve working behavior. |
| Brightness slider | Existing / reported | WMI; user previously reported hardware brightness works. |
| Night Light direct toggle | Not implemented | Settings shortcut only; no supported public toggle API established. |
| Hotspot start/stop | Partial / not working | API code proposed; fails to turn on in unpackaged app; needs diagnostics and MSIX capability. |
| Airplane Mode direct toggle | Not implemented / unclear | Settings shortcut exists; direct control not confirmed. |
| Notifications centre | Planned | Dedicated file and full UI/functionality to design. |
| Connected devices page | Partial | Page exists; separate module and polished device view planned. |
| WebView2 UI | Agreed / not started | New UI architecture decision; migration not begun. |
| MSIX packaging | Not started | Needed for capability-dependent operations such as tethering. |
| AI desktop actions | Not implemented | Future scope only; user previously explored NOVA OS/AI desktop goals but this WPF app has no confirmed Claude Code integration. |

---

## 11. Recommended next steps (priority order)

### Phase 0 — Secure current working state

1. Have the user open the project folder on the new account or provide the actual project ZIP/Git repository. This report alone is not the source code.
2. Inspect `NovaIsland.csproj`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.QuickSettings.cs`, all other source files, and any current project settings.
3. Create a Git commit or backup copy before changes.
4. Run:

   ```powershell
   cd C:\Projects\NovaIsland
   dotnet clean
   dotnet build
   ```

5. Capture the real build output and note warnings.
6. Search for duplicate methods, event handler mismatches, undefined controls and duplicate XAML names.

### Phase 1 — Stabilize features before the UI migration

1. Confirm Wi-Fi/Bluetooth radio toggle state.
2. Fix live Wi-Fi status using a robust connection query.
3. Fix Bluetooth connected-device status.
4. Verify volume and brightness still work.
5. Add explicit Hotspot diagnostics rather than silently falling back. Capture capability/result/exception.
6. Keep Night Light as a Settings launcher unless a documented supported API is found.
7. Avoid spending time on detailed XAML visual redesign, because web UI migration is now the agreed direction.

### Phase 2 — Create WebView2 proof of concept

1. Add WebView2 WPF NuGet package compatible with .NET 10 project.
2. Add a minimal `UI/index.html`, CSS and JS.
3. Host WebView2 in the existing WPF `MainWindow`.
4. Preserve transparent window, rounded pill, always-on-top positioning, resizing and hover/click interactions. WPF hosting/window chrome may still be required for native window behaviour.
5. Prove JavaScript → C# and C# → JavaScript messaging with one simple action before porting real functionality.
6. Keep existing XAML as a fallback during early migration.

### Phase 3 — Build the new design system

Create tokens for:

- Backgrounds: near-black/graphite, with restrained translucency.
- Primary text: white/off-white.
- Secondary text: soft gray.
- Accent: neutral white by default, optional restrained color only when required.
- Corner radii, spacing, icon sizes, typography, shadows and transitions.

Design elements:

- Compact pill.
- Expanded container.
- Media player card.
- Quick-setting tile with integrated arrow/secondary action (no detached-looking arrow).
- Active/inactive toggles.
- Sliders.
- Notification card.
- Page navigation indicator.
- Device list rows.

Animations must be subtle: no excessive bounce, abrupt scale, harsh hover wash or default Windows selection tint. Design interaction states for hover, active, disabled, loading and focus.

### Phase 4 — Port features page by page

Suggested order:

1. Island shell and navigation.
2. Quick Settings tiles and real status data.
3. Media page and island-wide audio reaction.
4. Notifications centre.
5. System monitor.
6. Calendar/date.
7. Devices page.

### Phase 5 — MSIX packaging and system permissions

1. Choose/create Visual Studio Windows Application Packaging Project or another supported MSIX packaging path.
2. Configure correct package identity and WPF app entry point.
3. Add capability declarations required by actual APIs, including `wiFiControl` for Windows tethering and the correct radio/Bluetooth capabilities as documented.
4. Build and install package.
5. Test all system operations from the installed package, not only `dotnet run`.
6. Keep fallbacks and clear error messages for unsupported adapters, disabled policy, missing permissions, or unsupported hardware.

### Phase 6 — Notifications and remaining features

Create `MainWindow.Notifications.cs` and later corresponding HTML/JS components. Decide whether the notification centre should show only notifications emitted by NOVA or also read Windows toast notifications. Reading the system-wide notification history has privacy/security and API constraints; do not assume a general public API is available. Investigate the supported Windows notification APIs and define a safe scope.

Create `MainWindow.Devices.cs` and `MainWindow.SystemMonitor.cs` once their APIs and lifecycle are audited.

---

## 12. Working rules for the next assistant

The user needs precise instructions. Follow these rules:

1. **Inspect before editing.** Ask the user to upload the project ZIP or provide the relevant files if the new account cannot access the local project. Do not assume this handoff report represents the exact current code.
2. **Give exact replacement instructions.** Say “Find this exact method/line; replace the complete method; paste this code directly below X.” Avoid generic placement instructions.
3. **Do not invent control names.** Use exact XAML `x:Name` values from the source.
4. **Do not duplicate event handlers or methods.** When moving a method, ensure its old copy is removed and the new partial class declaration is correct.
5. **Build after each contained step.** Use `dotnet build`; run the application for runtime checks.
6. **Preserve working features.** In particular, do not regress radio toggles, volume, brightness, media controls, audio loopback, island-wide audio reaction, page navigation or hover expansion.
7. **Avoid large unverified code dumps.** Make one logical change at a time and verify.
8. **Distinguish UI state from real OS state.** A Wi-Fi radio being ON does not mean it is connected. Bluetooth radio ON does not mean any device is connected. A successful build does not mean Hotspot is allowed to start.
9. **Be transparent about Windows capability limitations.** MSIX package identity and device capabilities are separate from compiling ordinary WPF code.
10. **Do not imply background/async work.** Complete each requested step in the active response.
11. The user explicitly agreed to the new web-based UI approach. Do not ask whether they want to switch again; proceed with the agreed WebView2 architecture unless new information changes the plan.
12. Do not use Electron or Tauri as the default now. WebView2 + existing C# backend was selected to preserve Windows integration and reduce rewrite risk.
13. Do not delete or replace the current WPF project. Migrate incrementally and keep a rollback point.

---

## 13. Useful current names and event handlers reference

### Quick Settings / Wi-Fi / Bluetooth XAML names

```text
WifiRadioButton
WifiIconCircle
WifiRadioIcon
WifiRadioLabel
CurrentWifiText

BluetoothRadioButton
BluetoothIconCircle
BluetoothRadioIcon
BluetoothRadioLabel
BluetoothRadioStatusText

WifiNetworksList
BluetoothListBox
WifiStatusText
BluetoothStatusText
BluetoothFooterText
WifiPasswordBox
WifiConnectButton
WifiCurrentSsidText
PairBluetoothButton
VolumeSlider
BrightnessSlider
VolumeValueText
BrightnessValueText
```

### Known handlers

```text
WifiRadioToggle_Click
BluetoothRadioToggle_Click
WifiTile_Click
BluetoothTile_Click
WifiNetworksList_SelectionChanged
ConnectWifi_Click
BluetoothListBox_SelectionChanged
PairBluetooth_Click
VolumeSlider_ValueChanged
BrightnessSlider_ValueChanged
BrightnessDebounceTimer_Tick
ToggleHotspot_Click          # proposed; verify it exists and is wired
OpenNightLightSettings_Click
OpenHotspotSettings_Click    # old handler may have been moved/replaced
OpenAirplaneSettings_Click
OpenWifiSettings_Click
OpenBluetoothSettings_Click
OpenSoundSettings_Click
OpenDisplaySettings_Click
```

### Main page names

```text
PageMusic
PageSystem
PageCalendar
PageQuickSettings
PageNotifications
PageDevices

PageDot0 ... PageDot5
```

### Main shell names

```text
IslandShell
CompactView
ExpandedView
```

---

## 14. First message / handoff prompt for the new account

Copy and paste the following message into the new ChatGPT account after attaching or making the project source available:

> I am continuing my NOVA Island Windows desktop project. Please read `NOVA_Project_Handoff_Report.md` fully before making changes. The actual source project is in `C:\Projects\NovaIsland`; I will provide the ZIP/repository or the relevant files because this report does not include source code. First inspect the real project and compare it against the report. Do not assume all proposed snippets were inserted exactly as described. Confirm the current build status and list any discrepancies. Our agreed architecture is to keep the existing C#/.NET 10 WPF backend and gradually replace the visual UI with HTML/CSS/JavaScript hosted inside WebView2. Do not rewrite the project from scratch or switch to Electron/Tauri. Preserve existing media, NAudio loopback, island-wide audio reaction, volume, brightness, Wi-Fi/Bluetooth, window positioning and page navigation. The user prefers exact instructions about what file to open, what exact code to find, and exactly what to replace/add. Work one small step at a time and build/test after each step. Priorities: establish a backup/Git commit, inspect the current code, create a minimal WebView2 proof of concept, then migrate the UI incrementally. Hotspot currently builds but does not turn on; likely package identity/`wiFiControl` capability issue, not yet diagnosed from an actual exception. Night Light currently opens Windows Settings; don't claim direct toggle support without a documented API. Also audit the Wi-Fi and Bluetooth connection status display, because radio state works but connected network/device status was reported as inaccurate.

---

## 15. Final handoff checklist

Before writing new feature code, the next assistant should confirm:

- [ ] Actual project source has been provided/accessed.
- [ ] Current `.csproj` and target framework have been checked.
- [ ] `MainWindow.xaml` and `MainWindow.xaml.cs` have been read.
- [ ] `MainWindow.QuickSettings.cs` has been read.
- [ ] Current build result has been confirmed.
- [ ] No duplicate method declarations or broken event-handler names exist.
- [ ] Git commit or backup has been created.
- [ ] WebView2 NuGet version/compatibility has been checked.
- [ ] Minimal WebView2 host renders a local HTML page.
- [ ] JS ↔ C# message bridge works before migrating features.
- [ ] Existing WPF functionality remains available during transition.
- [ ] Hotspot capability is tested in an installed MSIX package.
- [ ] Notification scope and Windows API constraints have been investigated.

**End of report.**
