using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace NovaIsland;

public partial class MainWindow
{
    private DeviceWatcher? bluetoothLeWatcher;
    private int discoveryGeneration;
    private readonly DispatcherTimer discoveryTimeout = new() { Interval = TimeSpan.FromSeconds(30) };

    private async Task StartBluetoothDiscoveryAsync(bool throwOnError = false)
    {
        StopBluetoothWatcher();
        int generation = discoveryGeneration;
        bluetoothDevices.Clear();
        PairBluetoothButton.IsEnabled = false;
        try
        {
            if (bluetoothRadio != null && bluetoothRadio.State != RadioState.On)
                throw new InvalidOperationException("Turn on Bluetooth to discover devices.");
            BluetoothStatusText.Text = "Searching for nearby devices…";
            BluetoothFooterText.Text = "Put your device in pairing mode.";
            var paired = DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask();
            var pairedLe = DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask();
            await Task.WhenAll(paired, pairedLe);
            if (isClosed || generation != discoveryGeneration) return;
            foreach (var device in paired.Result.Concat(pairedLe.Result)) AddBluetoothDevice(device);
            bluetoothWatcher = CreateBluetoothWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(false), generation);
            bluetoothLeWatcher = CreateBluetoothWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false), generation);
            bluetoothWatcher.Start();
            bluetoothLeWatcher.Start();
            discoveryTimeout.Tick -= DiscoveryTimeout_Tick;
            discoveryTimeout.Tick += DiscoveryTimeout_Tick;
            discoveryTimeout.Start();
        }
        catch (Exception ex)
        {
            StopBluetoothWatcher();
            BluetoothStatusText.Text = ex.Message;
            BluetoothFooterText.Text = "Windows Bluetooth Settings can complete pairing when confirmation is needed.";
            if (throwOnError) throw;
        }
    }

    private DeviceWatcher CreateBluetoothWatcher(string selector, int generation)
    {
        var watcher = DeviceInformation.CreateWatcher(selector);
        void UpdateUi(Action update)
        {
            if (isClosed || Dispatcher.HasShutdownStarted) return;
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!isClosed && generation == discoveryGeneration) update();
            }));
        }
        watcher.Added += (_, info) => UpdateUi(() => AddBluetoothDevice(info));
        watcher.Updated += (_, info) => UpdateUi(() =>
        {
            var item = bluetoothDevices.FirstOrDefault(d => d.Id == info.Id);
            if (item == null) return;
            item.Device.Update(info);
            int index = bluetoothDevices.IndexOf(item);
            bool selected = ReferenceEquals(BluetoothListBox.SelectedItem, item);
            bluetoothDevices[index] = new BluetoothDeviceItem(item.Device);
            if (selected) BluetoothListBox.SelectedItem = bluetoothDevices[index];
        });
        watcher.Removed += (_, info) => UpdateUi(() =>
        {
            var item = bluetoothDevices.FirstOrDefault(d => d.Id == info.Id);
            if (item != null) bluetoothDevices.Remove(item);
        });
        watcher.EnumerationCompleted += (_, _) => UpdateUi(() =>
            BluetoothStatusText.Text = bluetoothDevices.Count == 0 ? "No devices found. Make a device discoverable." : $"{bluetoothDevices.Count} devices found");
        return watcher;
    }

    private void AddBluetoothDevice(DeviceInformation info)
    {
        if (isClosed || bluetoothDevices.Any(d => d.Id == info.Id)) return;
        bluetoothDevices.Add(new BluetoothDeviceItem(info));
    }

    private void DiscoveryTimeout_Tick(object? sender, EventArgs e)
    {
        StopBluetoothWatcher();
        BluetoothStatusText.Text = $"{bluetoothDevices.Count} devices · Refresh to scan again";
    }

    private void StopBluetoothWatcher()
    {
        discoveryGeneration++;
        discoveryTimeout.Stop();
        foreach (var watcher in new[] { bluetoothWatcher, bluetoothLeWatcher })
        {
            if (watcher == null) continue;
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Bluetooth watcher: " + ex.Message); }
        }
        bluetoothWatcher = null;
        bluetoothLeWatcher = null;
    }
}
