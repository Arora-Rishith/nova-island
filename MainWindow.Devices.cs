using System.Management;
using Windows.Devices.Enumeration;

namespace NovaIsland;

public partial class MainWindow
{
    private sealed record ConnectedDevice(string Id, string Name, string Kind, string Status);
    private IReadOnlyList<ConnectedDevice> deviceSnapshot = Array.Empty<ConnectedDevice>();
    private bool refreshingDevices;
    private string deviceStatus = "Refresh to discover connected devices.";

    private async Task RefreshDevicesAsync()
    {
        if (refreshingDevices || isClosed) return;
        refreshingDevices = true;
        deviceStatus = "Refreshing devices…";
        try
        {
            await RefreshBluetoothConnectionStatusAsync();
            var list = connectedBluetooth.Select(d => new ConnectedDevice(d.Id, d.Name, "Bluetooth", "Connected")).ToList();
            var audio = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);
            list.AddRange(audio.Where(d => d.IsEnabled).Select(d => new ConnectedDevice(d.Id, d.Name, "Audio", "Available output")));
            var usb = await Task.Run(() =>
            {
                var found = new List<ConnectedDevice>();
                using var search = new ManagementObjectSearcher("SELECT DeviceID, Name, Status FROM Win32_PnPEntity WHERE Present = True AND PNPClass = 'USB'");
                search.Options.Timeout = TimeSpan.FromSeconds(3);
                using var results = search.Get();
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        string id = Convert.ToString(item["DeviceID"]) ?? "";
                        if (!id.StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase)) continue;
                        found.Add(new(id, Convert.ToString(item["Name"]) ?? "USB device", "USB", Convert.ToString(item["Status"]) ?? "Present"));
                    }
                }
                return found;
            });
            list.AddRange(usb);
            if (isClosed) return;
            deviceSnapshot = list.DistinctBy(d => d.Id).OrderBy(d => d.Kind).ThenBy(d => d.Name).ToArray();
            deviceStatus = $"{deviceSnapshot.Count} devices · {systemSnapshot.Power}";
        }
        catch (Exception ex) { deviceStatus = "Device discovery: " + ex.Message; }
        finally { refreshingDevices = false; }
    }
}
