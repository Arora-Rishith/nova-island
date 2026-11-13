using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Threading;

namespace NovaIsland;

public partial class MainWindow
{
    private readonly DispatcherTimer systemTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool refreshingSystem;
    private ulong previousIdle, previousKernel, previousUser;
    private SystemSnapshot systemSnapshot = new(null, null, null, null, "Checking power", 0, 0, null, null, null);
    private long previousNetworkSent;
    private long previousNetworkReceived;
    private DateTime previousNetworkSample = DateTime.MinValue;
    private sealed record SystemSnapshot(double? Cpu, double? Ram, double? Gpu, int? Battery,
        string Power, double UsedGb, double TotalGb, double? NetUpMbps, double? NetDownMbps, double? Disk);

    private void InitializeSystemMonitor()
    {
        systemTimer.Tick += async (_, _) => await RefreshSystemAsync();
        systemTimer.Start();
        _ = RefreshSystemAsync();
    }

    private async Task RefreshSystemAsync()
    {
        if (refreshingSystem || isClosed) return;
        refreshingSystem = true;
        bool includeGpu = isExpanded && currentPageIndex == 1;
        try
        {
            var snapshot = await Task.Run(() => ReadSystemSnapshot(includeGpu));
            if (isClosed) return;
            systemSnapshot = snapshot;
                CpuUsageText.Text = Percent(snapshot.Cpu);
                RamUsageText.Text = Percent(snapshot.Ram);
                GpuUsageText.Text = Percent(snapshot.Gpu);
                BatteryPercentText.Text = snapshot.Battery is int battery ? battery + "%" : "—";
                BatteryStatusText.Text = snapshot.Power;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("System metrics: " + ex.Message); }
        finally { refreshingSystem = false; }
    }

    private static string Percent(double? value) => value.HasValue ? $"{value:0}%" : "—";

    private SystemSnapshot ReadSystemSnapshot(bool includeGpu)
    {
        double? cpu = null, ram = null, gpu = null, netUp = null, netDown = null, disk = null;
        double total = 0, used = 0;
        if (GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            if (previousKernel != 0 && kernel >= previousKernel && user >= previousUser && idle >= previousIdle)
            {
                ulong elapsed = (kernel - previousKernel) + (user - previousUser);
                if (elapsed != 0) cpu = Math.Clamp(100.0 * (1 - (double)(idle - previousIdle) / elapsed), 0, 100);
            }
            previousIdle = idle; previousKernel = kernel; previousUser = user;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory))
        {
            total = memory.TotalPhysical / 1073741824.0;
            used = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
            ram = total > 0 ? used / total * 100 : null;
        }
        int? battery = null;
        string power = "Power status unavailable";
        if (GetSystemPowerStatus(out var status))
        {
            bool hasBattery = status.BatteryFlag != 255 && (status.BatteryFlag & 128) == 0;
            if (hasBattery && status.BatteryPercent <= 100) battery = status.BatteryPercent;
            power = !hasBattery ? "No battery" : (status.BatteryFlag & 8) != 0 ? "Charging"
                : status.AcLineStatus == 1 ? "Plugged in" : status.AcLineStatus == 0 ? "On battery" : "Unknown power source";
        }

        try
        {
            long sent = 0, received = 0;
            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                IPv4InterfaceStatistics statistics = network.GetIPv4Statistics();
                sent += statistics.BytesSent;
                received += statistics.BytesReceived;
            }

            DateTime now = DateTime.UtcNow;
            if (previousNetworkSample != DateTime.MinValue &&
                sent >= previousNetworkSent && received >= previousNetworkReceived)
            {
                double seconds = Math.Max(.1, (now - previousNetworkSample).TotalSeconds);
                netUp = Math.Clamp((sent - previousNetworkSent) * 8d / seconds / 1_000_000d, 0, 10_000);
                netDown = Math.Clamp((received - previousNetworkReceived) * 8d / seconds / 1_000_000d, 0, 10_000);
            }
            previousNetworkSent = sent;
            previousNetworkReceived = received;
            previousNetworkSample = now;
        }
        catch (NetworkInformationException) { }

        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
                disk = Math.Clamp((drive.TotalSize - drive.AvailableFreeSpace) * 100d / drive.TotalSize, 0, 100);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (includeGpu)
        {
            try
            {
                using var search = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                search.Options.Timeout = TimeSpan.FromSeconds(2);
                using var results = search.Get();
                var engines = new Dictionary<string, double>();
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        string name = Convert.ToString(item["Name"]) ?? "";
                        if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                        string engine = Regex.Replace(name, @"^pid_\d+_", "");
                        engines[engine] = engines.GetValueOrDefault(engine) + Convert.ToDouble(item["UtilizationPercentage"]);
                    }
                }
                // Sum processes using each engine, then report the busiest 3D engine.
                if (engines.Count > 0) gpu = Math.Clamp(engines.Values.Max(), 0, 100);
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new(cpu, ram, gpu, battery, power, Math.Round(used, 1), Math.Round(total, 1), netUp, netDown, disk);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus, BatteryFlag, BatteryPercent, SystemStatus;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
