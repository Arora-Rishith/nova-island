using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace NovaIsland.Services;

// Native WLAN APIs return typed data on every Windows display language.
// Credentials are passed to Windows in memory, never written to a temporary file.
internal static class WifiService
{
    internal sealed record Network(Guid Adapter, string Ssid, string Profile, uint Signal,
        bool Secure, uint Authentication, uint Cipher, bool Connected)
    {
        public string Id => Adapter + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Ssid)) + ":" + Authentication;
        public string Security => Authentication switch
        {
            1 => Secure ? "WEP" : "Open",
            4 => "WPA-Personal",
            7 => "WPA2-Personal",
            9 => "WPA3-Personal",
            10 => "Enhanced Open",
            _ => "Enterprise / Windows sign-in"
        };
    }

    public static async Task<IReadOnlyList<Network>> ScanAsync()
    {
        using var client = new Client();
        var adapters = client.Adapters();
        if (adapters.Count == 0) throw new InvalidOperationException("No Wi-Fi adapter is available.");
        foreach (var adapter in adapters)
        {
            Guid id = adapter;
            Check(WlanScan(client.Handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        }
        // Scanning is asynchronous; Windows keeps its network cache updated afterwards.
        await Task.Delay(1600);
        var networks = new List<Network>();
        foreach (var adapter in adapters)
        {
            Guid id = adapter;
            Check(WlanGetAvailableNetworkList(client.Handle, ref id, 0, IntPtr.Zero, out var list));
            try
            {
                int count = Marshal.ReadInt32(list);
                int size = Marshal.SizeOf<AvailableNetwork>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<AvailableNetwork>(IntPtr.Add(list, 8 + i * size));
                    string ssid = Encoding.UTF8.GetString(item.Ssid.Bytes, 0, (int)Math.Min(32, item.Ssid.Length));
                    if (string.IsNullOrWhiteSpace(ssid)) continue;
                    networks.Add(new Network(id, ssid, item.ProfileName, item.SignalQuality,
                        item.SecurityEnabled, item.Authentication, item.Cipher, (item.Flags & 1) != 0));
                }
            }
            finally { WlanFreeMemory(list); }
        }
        return networks.GroupBy(n => n.Id).Select(g => g.OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.Profile.Length).First()).OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.Signal).ToArray();
    }

    public static void Connect(Network network, string password)
    {
        using var client = new Client();
        Guid adapter = network.Adapter;
        string profile = network.Profile;
        if (password.Length != 0 || profile.Length == 0)
        {
            string authentication = network.Authentication switch
            {
                1 when !network.Secure => "open",
                4 => "WPAPSK",
                7 => "WPA2PSK",
                9 => "WPA3SAE",
                _ => throw new InvalidOperationException("Join this security type in Windows Wi-Fi Settings first.")
            };
            if (network.Secure && (password.Length < 8 || password.Length > 63))
                throw new ArgumentException("Enter an 8–63 character password, or leave it blank to use a saved network.");
            string encryption = !network.Secure ? "none" : network.Cipher switch
            {
                2 => "TKIP",
                4 => "AES",
                _ => throw new InvalidOperationException("Use Windows Wi-Fi Settings for this encryption type.")
            };
            profile = network.Ssid;
            string name = SecurityElement.Escape(profile)!;
            string key = SecurityElement.Escape(password)!;
            string hex = Convert.ToHexString(Encoding.UTF8.GetBytes(network.Ssid));
            string xml = $"""
                <?xml version="1.0"?>
                <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
                  <name>{name}</name><SSIDConfig><SSID><hex>{hex}</hex><name>{name}</name></SSID></SSIDConfig>
                  <connectionType>ESS</connectionType><connectionMode>manual</connectionMode>
                  <MSM><security><authEncryption><authentication>{authentication}</authentication>
                    <encryption>{encryption}</encryption><useOneX>false</useOneX></authEncryption>
                    {(network.Secure ? $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{key}</keyMaterial></sharedKey>" : "")}
                  </security></MSM>
                </WLANProfile>
                """;
            uint code = WlanSetProfile(client.Handle, ref adapter, 2, xml, null, true, IntPtr.Zero, out uint reason);
            if (code != 0)
            {
                var description = new StringBuilder(512);
                WlanReasonCodeToString(reason, (uint)description.Capacity, description, IntPtr.Zero);
                throw new InvalidOperationException($"Windows rejected the Wi-Fi profile ({code}): {description}");
            }
        }
        var parameters = new ConnectionParameters { Mode = 0, Profile = profile, BssType = 1 };
        Check(WlanConnect(client.Handle, ref adapter, ref parameters, IntPtr.Zero));
    }

    private static void Check(uint code)
    {
        if (code == 5) throw new InvalidOperationException("Wi-Fi access denied. Enable Location and desktop app access in Windows Settings.");
        if (code != 0) throw new Win32Exception((int)code);
    }

    private sealed class Client : IDisposable
    {
        public IntPtr Handle { get; }
        public Client()
        {
            Check(WlanOpenHandle(2, IntPtr.Zero, out _, out var handle));
            Handle = handle;
        }
        public List<Guid> Adapters()
        {
            Check(WlanEnumInterfaces(Handle, IntPtr.Zero, out var list));
            try
            {
                var result = new List<Guid>();
                for (int i = 0; i < Marshal.ReadInt32(list); i++)
                    result.Add(Marshal.PtrToStructure<InterfaceInfo>(IntPtr.Add(list, 8 + i * Marshal.SizeOf<InterfaceInfo>())).Id);
                return result;
            }
            finally { WlanFreeMemory(list); }
        }
        public void Dispose() => WlanCloseHandle(Handle, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
        public Ssid Ssid;
        public uint BssType, BssidCount;
        [MarshalAs(UnmanagedType.Bool)] public bool Connectable;
        public uint NotConnectableReason, PhyTypeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint SignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint Authentication, Cipher, Flags, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionParameters
    {
        public uint Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid, DesiredBssidList;
        public uint BssType, Flags;
    }

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr handle, ref Guid adapter, IntPtr ssid, IntPtr data, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid adapter, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanSetProfile(IntPtr handle, ref Guid adapter, uint flags, string xml, string? security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reason);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanConnect(IntPtr handle, ref Guid adapter, ref ConnectionParameters parameters, IntPtr reserved);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanReasonCodeToString(uint reason, uint size, StringBuilder text, IntPtr reserved);
}
