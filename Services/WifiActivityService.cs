using System.Runtime.InteropServices;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Threading;

namespace WindowsLive.Services;

public readonly record struct WifiActivityState(
    bool IsWifiConnected,
    bool IsHotspotActive,
    string? WifiNetworkName,
    string? HotspotClientName,
    int HotspotClientCount);

/// <summary>
/// Tracks the local computer's WLAN connection and Windows Wi-Fi Direct
/// hotspot adapter. It does not scan the local network for unrelated devices.
/// </summary>
public sealed class WifiActivityService
{
    public event Action<WifiActivityState>? StateChanged;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _debounceTimer;
    private WifiActivityState _lastState;
    private bool _started;
    private bool _hasPublishedInitialState;
    private int _isRefreshing = 0;
    private static bool _hotspotClientLookupUnavailable;

    private const uint WlanClientVersion = 2;
    private const uint WlanInterfaceOpcodeCurrentConnection = 7;
    private const int WlanInterfaceStateConnected = 1;

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        uint opcode,
        IntPtr reserved,
        out uint dataSize,
        out IntPtr data,
        out int opcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanInterfaceInfo
    {
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Description;

        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanInterfaceListHeader
    {
        public uint NumberOfItems;
        public uint CurrentIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint Length;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid Ssid;
        public uint BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] Bssid;

        public int PhyType;
        public uint PhyIndex;
        public uint SignalQuality;
        public uint ReceiveRate;
        public uint TransmitRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsSecurityEnabled;

        [MarshalAs(UnmanagedType.Bool)]
        public bool IsOneXEnabled;

        public uint AuthAlgorithm;
        public uint CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public int State;
        public int ConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ProfileName;

        public WlanAssociationAttributes Association;
        public WlanSecurityAttributes Security;
    }

    public WifiActivityService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += (_, _) => QueueRefresh();

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            TriggerBackgroundRefresh();
        };
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        QueueRefresh();
        _pollTimer.Start();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _pollTimer.Stop();
        _debounceTimer.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => QueueRefresh();

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private void TriggerBackgroundRefresh()
    {
        if (Interlocked.CompareExchange(ref _isRefreshing, 1, 0) != 0) return;

        Task.Run(() =>
        {
            try
            {
                var state = QueryCurrentState();
                if (_dispatcher.HasShutdownStarted) return;

                _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (_hasPublishedInitialState && state == _lastState) return;

                    _hasPublishedInitialState = true;
                    _lastState = state;
                    App.Log($"Wi-Fi state changed: Connected={state.IsWifiConnected}, Hotspot={state.IsHotspotActive}, HotspotClients={state.HotspotClientCount}");
                    StateChanged?.Invoke(state);
                });
            }
            catch (Exception ex)
            {
                App.Log($"WifiActivityService refresh exception: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isRefreshing, 0);
            }
        });
    }

    private static WifiActivityState QueryCurrentState()
    {
        bool wifiConnected = false;
        bool hotspotActive = false;
        string? wifiNetworkName = TryGetConnectedWifiName();
        string? hotspotClientName = null;
        int hotspotClientCount = 0;

        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                string name = adapter.Name ?? string.Empty;
                string description = adapter.Description ?? string.Empty;

                if (description.Contains("Microsoft Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase))
                {
                    hotspotActive |= adapter.OperationalStatus == OperationalStatus.Up;
                    continue;
                }

                if (adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 ||
                    adapter.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (description.Contains("Filter Driver", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("Virtual WiFi Filter", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("-WFP", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("-QoS", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("-Native WiFi", StringComparison.OrdinalIgnoreCase) ||
                    description.Contains("Virtual Adapter", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                wifiConnected = true;
            }

            wifiConnected |= !string.IsNullOrWhiteSpace(wifiNetworkName);
            if (hotspotActive)
            {
                (hotspotClientName, hotspotClientCount) = TryGetHotspotClient();
            }
        }
        catch (Exception ex)
        {
            App.Log($"WifiActivityService state query error: {ex.Message}");
        }

        return new WifiActivityState(wifiConnected, hotspotActive, wifiNetworkName, hotspotClientName, hotspotClientCount);
    }

    private static string? TryGetConnectedWifiName()
    {
        IntPtr clientHandle = IntPtr.Zero;
        IntPtr interfaceList = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(WlanClientVersion, IntPtr.Zero, out _, out clientHandle) != 0 || clientHandle == IntPtr.Zero)
            {
                return null;
            }

            if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceList) != 0 || interfaceList == IntPtr.Zero)
            {
                return null;
            }

            var listHeader = Marshal.PtrToStructure<WlanInterfaceListHeader>(interfaceList);
            int firstInterfaceOffset = Marshal.SizeOf<WlanInterfaceListHeader>();
            int interfaceInfoSize = Marshal.SizeOf<WlanInterfaceInfo>();

            for (int index = 0; index < listHeader.NumberOfItems; index++)
            {
                IntPtr interfaceInfoPointer = IntPtr.Add(interfaceList, firstInterfaceOffset + index * interfaceInfoSize);
                var interfaceInfo = Marshal.PtrToStructure<WlanInterfaceInfo>(interfaceInfoPointer);
                if (interfaceInfo.State != WlanInterfaceStateConnected ||
                    interfaceInfo.Description.Contains("Wi-Fi Direct Virtual Adapter", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IntPtr connectionData = IntPtr.Zero;
                try
                {
                    if (WlanQueryInterface(clientHandle, ref interfaceInfo.InterfaceGuid,
                            WlanInterfaceOpcodeCurrentConnection, IntPtr.Zero,
                            out _, out connectionData, out _) != 0 || connectionData == IntPtr.Zero)
                    {
                        continue;
                    }

                    var connection = Marshal.PtrToStructure<WlanConnectionAttributes>(connectionData);
                    int ssidLength = (int)Math.Min(connection.Association.Ssid.Length, 32u);
                    if (connection.State == WlanInterfaceStateConnected &&
                        ssidLength > 0 && connection.Association.Ssid.Bytes != null)
                    {
                        string ssid = Encoding.UTF8.GetString(connection.Association.Ssid.Bytes, 0, ssidLength).Trim();
                        if (!string.IsNullOrWhiteSpace(ssid)) return ssid;
                    }
                }
                finally
                {
                    if (connectionData != IntPtr.Zero) WlanFreeMemory(connectionData);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"Wi-Fi network name query error: {ex.Message}");
        }
        finally
        {
            if (interfaceList != IntPtr.Zero) WlanFreeMemory(interfaceList);
            if (clientHandle != IntPtr.Zero) WlanCloseHandle(clientHandle, IntPtr.Zero);
        }

        return null;
    }

    private static (string? Name, int Count) TryGetHotspotClient()
    {
        if (_hotspotClientLookupUnavailable) return (null, 0);

        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile == null) return (null, 0);

            var tethering = Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager
                .CreateFromConnectionProfile(profile);
            var clients = tethering.GetTetheringClients();
            string? clientName = clients
                .SelectMany(client => client.HostNames)
                .Select(host => host.RawName.Trim().TrimEnd('.'))
                .FirstOrDefault(IsUsableHostName);

            return (clientName, clients.Count);
        }
        catch (Exception ex)
        {
            // The connected-client list can be unavailable unless this app is
            // deployed with Windows' Wi-Fi control capability. Hotspot state
            // remains available from the Wi-Fi Direct adapter above.
            _hotspotClientLookupUnavailable = true;
            App.Log($"Hotspot client name query unavailable: {ex.Message}");
            return (null, 0);
        }
    }

    private static bool IsUsableHostName(string? hostName) =>
        !string.IsNullOrWhiteSpace(hostName) &&
        !System.Net.IPAddress.TryParse(hostName, out _) &&
        !hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase);
}
