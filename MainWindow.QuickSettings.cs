
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace NovaIsland
{
    public partial class MainWindow
    {
        // ==========================================
        // NOVA QUICK SETTINGS
        // ==========================================

        private void OpenNightLightSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:nightlight");

        private sealed record HotspotSnapshot(bool Available, bool On, bool Busy, string Capability, string Detail, bool Packaged);
        private HotspotSnapshot hotspotSnapshot = new(false, false, false, "Unchecked", "Checking availability", false);
        private bool changingHotspot;
        private NetworkOperatorTetheringManager? tetheringManager;

        private void RefreshHotspotState()
        {
            if (changingHotspot) return;
            try
            {
                // Retain the manager when the upstream profile vanishes so an active hotspot can still be stopped.
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (tetheringManager?.TetheringOperationalState != TetheringOperationalState.On)
                {
                    tetheringManager = null;
                    if (profile == null)
                    {
                        hotspotSnapshot = new(false, false, false, "NoConnectionProfile", "No active internet connection.", HasPackageIdentity);
                        return;
                    }
                    var capability = NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile);
                    if (capability != TetheringCapability.Enabled)
                    {
                        hotspotSnapshot = new(false, false, false, capability.ToString(),
                            capability == TetheringCapability.DisabledBySystemCapability
                                ? "Install the MSIX package with wiFiControl to enable direct hotspot control."
                                : "Windows reports: " + capability, HasPackageIdentity);
                        return;
                    }
                    tetheringManager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
                }
                var state = tetheringManager.TetheringOperationalState;
                hotspotSnapshot = new(true, state == TetheringOperationalState.On,
                    state == TetheringOperationalState.InTransition, "Enabled", state.ToString(), HasPackageIdentity);
            }
            catch (Exception ex)
            {
                hotspotSnapshot = new(false, false, false, $"HRESULT 0x{ex.HResult:X8}", ex.Message, HasPackageIdentity);
            }
        }

        private async Task SetHotspotAsync(bool enabled)
        {
            if (changingHotspot) throw new InvalidOperationException("Hotspot is already changing state.");
            RefreshHotspotState();
            if (!hotspotSnapshot.Available || tetheringManager == null)
                throw new InvalidOperationException(hotspotSnapshot.Capability + ": " + hotspotSnapshot.Detail);
            if (hotspotSnapshot.Busy) throw new InvalidOperationException("Windows is already changing the hotspot state.");
            if (hotspotSnapshot.On == enabled) return;
            changingHotspot = true;
            hotspotSnapshot = hotspotSnapshot with { Busy = true, Detail = enabled ? "Starting…" : "Stopping…" };
            try
            {
                var result = enabled ? await tetheringManager.StartTetheringAsync() : await tetheringManager.StopTetheringAsync();
                if (result.Status != TetheringOperationStatus.Success)
                    throw new InvalidOperationException($"{result.Status}: {result.AdditionalErrorMessage}");
                if ((tetheringManager.TetheringOperationalState == TetheringOperationalState.On) != enabled)
                    throw new InvalidOperationException("Windows accepted the request but has not confirmed the hotspot state.");
            }
            catch (Exception ex)
            {
                AddNotice("Hotspot", $"{ex.Message} (0x{ex.HResult:X8})");
                throw;
            }
            finally
            {
                changingHotspot = false;
                RefreshHotspotState();
            }
        }

        private async void ToggleHotspot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshHotspotState();
                await SetHotspotAsync(!hotspotSnapshot.On);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "NOVA Hotspot", MessageBoxButton.OK, MessageBoxImage.Information);
                OpenSettingsPage("ms-settings:network-mobilehotspot");
            }
        }
        private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:bluetooth");
        private void OpenWifiSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:network-wifi");
        
        private void OpenSoundSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:sound");
        private void OpenDisplaySettings_Click(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:display");
        private void OpenAirplaneSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsPage("ms-settings:network-airplanemode");

    }
}
