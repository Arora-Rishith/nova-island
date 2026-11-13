using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace NovaIsland;

public partial class MainWindow
{
    private bool refreshingConnections;
    private string? connectedSsid;
    private IReadOnlyList<DeviceInformation> connectedBluetooth = Array.Empty<DeviceInformation>();
    private bool connectingWifi;

    private async Task ConnectToWifiAsync(WifiNetworkItem network, string password)
    {
        if (connectingWifi) throw new InvalidOperationException("A Wi-Fi connection is already in progress.");
        if (network.Source == null) throw new InvalidOperationException("Refresh the network list first.");
        connectingWifi = true;
        WifiStatusText.Text = "Connecting…";
        try
        {
            await Task.Run(() => Services.WifiService.Connect(network.Source, password));
            for (int i = 0; i < 12 && !isClosed; i++)
            {
                await Task.Delay(1000);
                await RefreshCurrentWifiNameAsync();
                if (connectedSsid == network.Ssid)
                {
                    WifiStatusText.Text = "Connected";
                    return;
                }
            }
            throw new InvalidOperationException("Connection not confirmed. Check the password or open Windows Wi-Fi Settings.");
        }
        catch (Exception ex)
        {
            WifiStatusText.Text = ex.Message;
            throw;
        }
        finally { connectingWifi = false; }
    }

    private async Task RefreshCurrentWifiNameAsync()
    {
        try
        {
            // Query connection profiles rather than parsing localized netsh text.
            string? ssid = await Task.Run(() => NetworkInformation.GetConnectionProfiles()
                .Where(p => p.IsWlanConnectionProfile &&
                    p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
                .OrderByDescending(p => p.GetNetworkConnectivityLevel())
                .Select(p => p.WlanConnectionProfileDetails.GetConnectedSsid())
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));
            if (isClosed) return;
            bool off = wifiRadio != null && wifiRadio.State != RadioState.On;
            connectedSsid = off ? null : ssid;
            CurrentWifiText.Text = off ? "Wi-Fi off" : connectedSsid ?? "Not connected";
            WifiCurrentSsidText.Text = connectedSsid == null ? "Not connected to Wi-Fi" : "Connected to " + connectedSsid;
        }
        catch (Exception)
        {
            connectedSsid = null;
            CurrentWifiText.Text = "Status unavailable";
            WifiCurrentSsidText.Text = "Allow Location access in Windows to read the network name.";
        }
    }

    private async Task RefreshConnectionStatusAsync()
    {
        if (refreshingConnections || isClosed) return;
        refreshingConnections = true;
        try
        {
            UpdateRadioTiles();
            await Task.WhenAll(RefreshCurrentWifiNameAsync(), RefreshBluetoothConnectionStatusAsync());
        }
        finally { refreshingConnections = false; }
    }

    private async Task RefreshBluetoothConnectionStatusAsync()
    {
        try
        {
            if (bluetoothRadio != null && bluetoothRadio.State != RadioState.On)
            {
                connectedBluetooth = Array.Empty<DeviceInformation>();
                BluetoothRadioStatusText.Text = "Off";
                return;
            }
            var classic = DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(
                BluetoothConnectionStatus.Connected)).AsTask();
            var lowEnergy = DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(
                BluetoothConnectionStatus.Connected)).AsTask();
            await Task.WhenAll(classic, lowEnergy);
            if (isClosed) return;
            connectedBluetooth = classic.Result.Concat(lowEnergy.Result).DistinctBy(d => d.Id).ToArray();
            BluetoothRadioStatusText.Text = bluetoothRadio != null && bluetoothRadio.State != RadioState.On
                ? "Off" : connectedBluetooth.Count switch
                {
                    0 => bluetoothRadio == null ? "Unavailable" : "On · Not connected",
                    1 => connectedBluetooth[0].Name,
                    _ => $"{connectedBluetooth.Count} devices connected"
                };
        }
        catch (Exception)
        {
            connectedBluetooth = Array.Empty<DeviceInformation>();
            BluetoothRadioStatusText.Text = "Status unavailable";
        }
    }

    private async Task SetRadioStateAsync(Radio? radio, bool enabled)
    {
        if (radio == null) throw new InvalidOperationException("This radio is unavailable.");
        if (radioToggleInProgress) throw new InvalidOperationException("A radio change is already in progress.");
        radioToggleInProgress = true;
        UpdateRadioTiles();
        try
        {
            if (!radioAccessGranted)
            {
                var access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed)
                    throw new InvalidOperationException("Radio access: " + access);
                radioAccessGranted = true;
            }
            var target = enabled ? RadioState.On : RadioState.Off;
            if (radio.State != target)
            {
                var result = await radio.SetStateAsync(target);
                if (result != RadioAccessStatus.Allowed)
                    throw new InvalidOperationException("Windows denied the radio change: " + result);
                for (int i = 0; i < 10 && radio.State != target && !isClosed; i++) await Task.Delay(150);
                if (radio.State != target) throw new InvalidOperationException("Windows has not changed the radio state.");
            }
            await RefreshConnectionStatusAsync();
        }
        finally
        {
            radioToggleInProgress = false;
            UpdateRadioTiles();
        }
    }
}
