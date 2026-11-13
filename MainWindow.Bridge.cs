using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Devices.Radios;
using Windows.Devices.Enumeration;

namespace NovaIsland;

public partial class MainWindow
{
    private ImageSource? lastWebArtwork;
    private bool scanningWifi;
    private bool pairingBluetooth;
    private DateTime lastPageRefresh = DateTime.MinValue;

    private object GetWebState() => new
    {
        window = new { expanded = isExpanded, page = currentPageIndex },
        time = DateTimeOffset.Now,
        weather = weatherSnapshot,
        media = new
        {
            available = currentSession != null,
            title = ExpandedTitle.Text, artist = ExpandedArtist.Text, album = ExpandedAlbum.Text,
            playing = mediaIsPlaying, position = SeekSlider.Value, duration = SeekSlider.IsEnabled ? SeekSlider.Maximum : 0,
            canSeek = SeekSlider.IsEnabled, lyric = currentLyric, lyricsStatus,
            lyrics = lyricsLines.Select(line => line.Text).ToArray(),
            lyricsSynced, lyricIndex = currentLyricIndex, lyricsLoading = lyricsRequestCancellation != null,
            accent = new { r = albumAccentColor.R, g = albumAccentColor.G, b = albumAccentColor.B }
        },
        quick = new
        {
            wifi = new { available = wifiRadio != null, on = wifiRadio?.State == RadioState.On, status = CurrentWifiText.Text },
            bluetooth = new { available = bluetoothRadio != null, on = bluetoothRadio?.State == RadioState.On, status = BluetoothRadioStatusText.Text },
            radioBusy = radioToggleInProgress,
            volume = VolumeSlider.Value, volumeAvailable = audioEndpoint != null && VolumeSlider.IsEnabled,
            brightness = BrightnessSlider.Value, brightnessAvailable = BrightnessSlider.IsEnabled,
            hotspot = hotspotSnapshot
        },
        wifi = new
        {
            status = WifiStatusText.Text, connectedSsid, busy = scanningWifi || connectingWifi,
            networks = wifiNetworks.Select(n => new
            {
                id = n.Source?.Id, ssid = n.Ssid, security = n.SecurityLabel, signal = n.SignalPercent,
                secure = n.IsSecure, saved = !string.IsNullOrEmpty(n.Source?.Profile), connected = n.Ssid == connectedSsid
            }).ToArray()
        },
        bluetooth = new
        {
            status = BluetoothStatusText.Text, footer = BluetoothFooterText.Text, busy = pairingBluetooth,
            devices = bluetoothDevices.Select(d => new
            {
                id = d.Id, name = d.Name, paired = d.IsPaired, canPair = d.CanPair,
                connected = connectedBluetooth.Any(c => c.Id == d.Id)
            }).ToArray()
        },
        system = systemSnapshot,
        devices = new { items = deviceSnapshot, status = deviceStatus, busy = refreshingDevices },
        notifications = new
        {
            items = localNotifications.Concat(windowsNotifications).OrderByDescending(n => n.Time).ToArray(),
            status = notificationStatus, access = notificationAccess, packaged = HasPackageIdentity
        },
        clipboard = GetClipboardState(),
        voice = GetVoiceState(),
        shortcuts = GetAppShortcuts(),
        ai = aiSnapshot,
        focus = GetFocusState()
    };

    private void PublishWebState()
    {
        if (!webReady || isClosed) return;
        SendWebMessage(new { type = "state", data = GetWebState() });
        if (!ReferenceEquals(lastWebArtwork, ExpandedAlbumArt.Source))
        {
            lastWebArtwork = ExpandedAlbumArt.Source;
            string? image = null;
            if (lastWebArtwork is BitmapSource bitmap)
            {
                using var memory = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(memory);
                image = "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
            }
            SendWebMessage(new { type = "artwork", data = image });
        }
        if (isExpanded && (DateTime.UtcNow - lastPageRefresh).TotalSeconds >= 5)
        {
            lastPageRefresh = DateTime.UtcNow;
            if (currentPageIndex == 3) RefreshHotspotState();
            if (currentPageIndex == 4) _ = RefreshNotificationsAsync();
            if (currentPageIndex == 5) _ = RefreshDevicesAsync();
            if (currentPageIndex == 6) _ = RefreshAiAsync();
        }
    }

    private async Task<object?> HandleWebActionAsync(string action, JsonElement payload)
    {
        switch (action)
        {
            case "app.ready":
                webReady = true;
                ConfigureWebHost();
                IslandShell.Visibility = Visibility.Collapsed;
                webView!.Visibility = Visibility.Visible;
                webView.IsHitTestVisible = true;
                lastWebArtwork = null;
                webStateTimer.Start();
                return new { version = 1 };
            case "app.getState": return GetWebState();
            case "test.holdPointer":
                if (!Environment.GetCommandLineArgs().Contains("--devtools")) throw new InvalidOperationException("Test action unavailable.");
                webTestHoldsPointer = ReadBoolean(payload, "enabled");
                break;
            case "clipboard.dismiss": ClearClipboardPreview(); break;
            case "voice.start": await StartVoiceCommandsAsync(); break;
            case "voice.stop": await StopVoiceCommandsAsync(); break;
            case "shortcut.open": OpenAppShortcut(ReadString(payload, "id", 32)); break;
            case "ai.refresh": await RefreshAiAsync(); break;
            case "ai.open": OpenAiAgent(); await RefreshAiAsync(); break;
            case "focus.start": focusSession.Start((int)ReadNumber(payload, "minutes", 1, 180, integer: true)); break;
            case "focus.toggle": focusSession.Toggle(); break;
            case "focus.reset": focusSession.Reset(); break;
            case "lyrics.refresh":
                if (currentSession == null) throw new InvalidOperationException("Play a song first.");
                lyricsKey = string.Empty;
                await LoadLyricsAsync(artist: ExpandedArtist.Text, title: ExpandedTitle.Text, mediaKey: lastMediaKey);
                break;
            case "window.expand": AnimateIsland(true); break;
            case "window.collapse":
                StopBluetoothWatcher();
                AnimateIsland(false);
                break;
            case "window.navigate":
                int page = (int)ReadNumber(payload, "page", 0, 7, integer: true);
                if (page != 3) StopBluetoothWatcher();
                NavigateToPage(page);
                lastPageRefresh = DateTime.MinValue;
                break;
            case "app.exit":
                _ = Dispatcher.BeginInvoke(new Action(Close));
                break;
            case "media.playPause":
            case "media.previous":
            case "media.next":
            case "media.seek":
                var session = currentSession ?? throw new InvalidOperationException("No active media session.");
                bool accepted;
                if (action == "media.seek")
                {
                    if (!SeekSlider.IsEnabled) throw new InvalidOperationException("This media session does not support seeking.");
                    double seconds = ReadNumber(payload, "seconds", 0, SeekSlider.Maximum);
                    accepted = await session.TryChangePlaybackPositionAsync(session.GetTimelineProperties().StartTime.Ticks + TimeSpan.FromSeconds(seconds).Ticks);
                }
                else accepted = action switch
                {
                    "media.playPause" => await session.TryTogglePlayPauseAsync(),
                    "media.previous" => await session.TrySkipPreviousAsync(),
                    _ => await session.TrySkipNextAsync()
                };
                if (!accepted) throw new InvalidOperationException("The media app did not accept this control.");
                await RefreshMediaAsync();
                break;
            case "quickSettings.setVolume":
                double volume = ReadNumber(payload, "value", 0, 100);
                if (audioEndpoint == null) throw new InvalidOperationException("No audio output is available.");
                audioEndpoint.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100);
                audioEndpoint.AudioEndpointVolume.Mute = false;
                isInitializingQuickSettings = true;
                try { VolumeSlider.Value = volume; }
                finally { isInitializingQuickSettings = false; }
                return volume;
            case "quickSettings.setBrightness":
                int brightness = (int)ReadNumber(payload, "value", 0, 100, integer: true);
                if (!BrightnessSlider.IsEnabled) throw new InvalidOperationException("This display does not expose brightness control.");
                if (isSettingBrightness) throw new InvalidOperationException("A brightness change is in progress.");
                isSettingBrightness = true;
                try
                {
                    if (!await Task.Run(() => SetDisplayBrightness(brightness)))
                        throw new InvalidOperationException("Windows could not set this display's brightness.");
                    lastAppliedBrightness = brightness;
                    isSyncingBrightness = true;
                    BrightnessSlider.Value = brightness;
                }
                finally { isSyncingBrightness = false; isSettingBrightness = false; }
                return brightness;
            case "radio.setWifi": await SetRadioStateAsync(wifiRadio, ReadBoolean(payload, "enabled")); break;
            case "radio.setBluetooth": await SetRadioStateAsync(bluetoothRadio, ReadBoolean(payload, "enabled")); break;
            case "hotspot.set": await SetHotspotAsync(ReadBoolean(payload, "enabled")); break;
            case "wifi.scan":
                if (scanningWifi) throw new InvalidOperationException("A Wi-Fi scan is already in progress.");
                scanningWifi = true;
                try { await RefreshWifiNetworksAsync(throwOnError: true); }
                finally { scanningWifi = false; }
                break;
            case "wifi.connect":
                string networkId = ReadString(payload, "id", 1024);
                var network = wifiNetworks.FirstOrDefault(n => n.Source?.Id == networkId)
                    ?? throw new ArgumentException("Refresh the network list and select a network.");
                await ConnectToWifiAsync(network, ReadString(payload, "password", 63, allowEmpty: true));
                break;
            case "bluetooth.scan": await StartBluetoothDiscoveryAsync(throwOnError: true); break;
            case "bluetooth.stop": StopBluetoothWatcher(); break;
            case "bluetooth.pair":
                string deviceId = ReadString(payload, "id", 2048);
                var device = bluetoothDevices.FirstOrDefault(d => d.Id == deviceId)
                    ?? throw new ArgumentException("Device is no longer available.");
                if (pairingBluetooth) throw new InvalidOperationException("Pairing is already in progress.");
                if (device.IsPaired) return "Already paired";
                if (!device.CanPair) throw new InvalidOperationException("Use Windows Settings to pair this device.");
                pairingBluetooth = true;
                try
                {
                    var result = await device.Device.Pairing.PairAsync();
                    if (result.Status is not (DevicePairingResultStatus.Paired or DevicePairingResultStatus.AlreadyPaired))
                        throw new InvalidOperationException("Pairing: " + result.Status + ". Open Windows Settings to complete pairing.");
                    await StartBluetoothDiscoveryAsync();
                }
                finally { pairingBluetooth = false; }
                break;
            case "system.refresh": await RefreshSystemAsync(); break;
            case "devices.refresh": await RefreshDevicesAsync(); break;
            case "notifications.enable": await EnableNotificationsAsync(); break;
            case "notifications.refresh": await RefreshNotificationsAsync(); break;
            case "notifications.dismiss": await DismissNotificationAsync(ReadString(payload, "id", 80)); break;
            case "notifications.clear": await ClearVisibleNotificationsAsync(); break;
            case "settings.open":
                string destination = ReadString(payload, "page", 32) switch
                {
                    "wifi" => "network-wifi", "bluetooth" => "bluetooth", "hotspot" => "network-mobilehotspot",
                    "airplane" => "network-airplanemode", "nightlight" => "nightlight", "sound" => "sound",
                    "display" => "display", "location" => "privacy-location", "notifications" => "privacy-notifications",
                    _ => throw new ArgumentException("Unknown settings page.")
                };
                OpenSettingsPage("ms-settings:" + destination, throwOnError: true);
                break;
            default: throw new ArgumentException("Unknown action: " + action);
        }
        return null;
    }

    private static JsonElement ReadProperty(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
            throw new ArgumentException("Missing payload property: " + name);
        return value;
    }
    private static double ReadNumber(JsonElement payload, string name, double min, double max, bool integer = false)
    {
        var value = ReadProperty(payload, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) || !double.IsFinite(number) ||
            number < min || number > max || (integer && number != Math.Truncate(number)))
            throw new ArgumentException($"{name} must be {(integer ? "an integer" : "a number")} from {min} to {max}.");
        return number;
    }
    private static bool ReadBoolean(JsonElement payload, string name)
    {
        var value = ReadProperty(payload, name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException(name + " must be a boolean.");
        return value.GetBoolean();
    }
    private static string ReadString(JsonElement payload, string name, int maxLength, bool allowEmpty = false)
    {
        var value = ReadProperty(payload, name);
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException(name + " must be text.");
        string text = value.GetString()!;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text)) || text.Length > maxLength || text.Contains('\0'))
            throw new ArgumentException("Invalid " + name + ".");
        return text;
    }
}
