using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WindowsLive.Services;

public enum BluetoothDeviceKind
{
    AudioHeadset,
    Smartphone,
    Generic
}

public class BluetoothDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Audio Device";
    public BluetoothDeviceKind DeviceKind { get; set; } = BluetoothDeviceKind.AudioHeadset;
    public int BatteryPercent { get; set; } = 85;
    public bool IsConnected { get; set; } = true;
    public DateTime ConnectedTime { get; set; } = DateTime.Now;
}

/// <summary>
/// Monitors Bluetooth device connection and disconnection events using Windows DeviceWatcher APIs.
/// </summary>
public class BluetoothWatcherService
{
    public event Action<BluetoothDeviceInfo>? DeviceConnected;
    public event Action<BluetoothDeviceInfo>? DeviceDisconnected;

    private DeviceWatcher? _watcher;
    private readonly HashSet<string> _knownConnectedIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialEnumerationCompleted = false;
    private readonly Dispatcher _dispatcher;

    public BluetoothWatcherService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
        try
        {
            // AQS selector for connected Bluetooth devices
            string aqs = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            string[] requestedProperties = new[]
            {
                "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2",
                "System.Devices.Aep.IsConnected",
                "System.ItemNameDisplay"
            };

            _watcher = DeviceInformation.CreateWatcher(aqs, requestedProperties, DeviceInformationKind.AssociationEndpoint);

            _watcher.Added += Watcher_Added;
            _watcher.Updated += Watcher_Updated;
            _watcher.Removed += Watcher_Removed;
            _watcher.EnumerationCompleted += (s, e) =>
            {
                _initialEnumerationCompleted = true;
                App.Log($"BluetoothWatcherService: Initial enumeration completed with {_knownConnectedIds.Count} connected devices.");
            };

            _watcher.Start();
            App.Log("BluetoothWatcherService started successfully.");
        }
        catch (Exception ex)
        {
            App.Log($"BluetoothWatcherService Start error: {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            if (_watcher != null)
            {
                _watcher.Stop();
                _watcher = null;
            }
        }
        catch (Exception ex)
        {
            App.Log($"BluetoothWatcherService Stop error: {ex.Message}");
        }
    }

    private void Watcher_Added(DeviceWatcher sender, DeviceInformation info)
    {
        bool wasNew = _knownConnectedIds.Add(info.Id);
        if (_initialEnumerationCompleted && wasNew)
        {
            HandleDeviceConnected(info);
        }
    }

    private void Watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!_initialEnumerationCompleted) return;

        // Check if connection status transitioned to connected
        if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConnObj) && isConnObj is bool isConnected)
        {
            if (isConnected)
            {
                if (_knownConnectedIds.Add(update.Id))
                {
                    HandleDeviceConnected(update.Id);
                }
            }
            else
            {
                if (_knownConnectedIds.Remove(update.Id))
                {
                    _dispatcher.Invoke(() =>
                    {
                        DeviceDisconnected?.Invoke(new BluetoothDeviceInfo { Id = update.Id });
                    });
                }
            }
        }
    }

    private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (_knownConnectedIds.Remove(update.Id))
        {
            _dispatcher.Invoke(() =>
            {
                DeviceDisconnected?.Invoke(new BluetoothDeviceInfo { Id = update.Id });
            });
        }
    }

    public static BluetoothDeviceKind DetectDeviceKind(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BluetoothDeviceKind.AudioHeadset;
        string lower = name.ToLowerInvariant();
        if (lower.Contains("phone") || lower.Contains("iphone") || lower.Contains("galaxy") ||
            lower.Contains("pixel") || lower.Contains("android") || lower.Contains("redmi") ||
            lower.Contains("xiaomi") || lower.Contains("oneplus") || lower.Contains("oppo") ||
            lower.Contains("vivo") || lower.Contains("realme") || lower.Contains("huawei") ||
            lower.Contains("motorola") || lower.Contains("moto") || lower.Contains("samsung"))
        {
            return BluetoothDeviceKind.Smartphone;
        }
        return BluetoothDeviceKind.AudioHeadset;
    }

    private void HandleDeviceConnected(DeviceInformation info)
    {
        string name = info.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Contains("Generic Attribute", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string cleanName = CleanDeviceName(name);
        var kind = DetectDeviceKind(cleanName);

        // Always query BtBatteryReader first for the most accurate, live battery percentage (e.g. from BTHLE or newest timestamp)
        int battery = BtBatteryReader.GetBattery(cleanName);

        if (battery <= 0 || battery > 100)
        {
            if (info.Properties.TryGetValue("{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", out var bVal))
            {
                if (bVal is byte bByte) battery = bByte;
                else if (bVal is int bInt) battery = bInt;
                else if (bVal is short bShort) battery = bShort;
            }
        }

        if (battery <= 0 || battery > 100)
        {
            battery = 85;
        }

        var dev = new BluetoothDeviceInfo
        {
            Id = info.Id,
            Name = cleanName,
            DeviceKind = kind,
            BatteryPercent = Math.Clamp(battery, 1, 100),
            IsConnected = true,
            ConnectedTime = DateTime.Now
        };

        App.Log($"Bluetooth device connected: Name='{dev.Name}', Kind={dev.DeviceKind}, Battery={dev.BatteryPercent}%");

        _dispatcher.Invoke(() =>
        {
            DeviceConnected?.Invoke(dev);
        });
    }

    private async void HandleDeviceConnected(string deviceId)
    {
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(deviceId, new[] { "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2", "System.ItemNameDisplay" });
            if (info != null)
            {
                HandleDeviceConnected(info);
            }
        }
        catch { }
    }

    private static string CleanDeviceName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Audio device";
        string cleaned = raw.Replace("Avrcp Transport", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("Hands-Free", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Audio device" : cleaned;
    }

    /// <summary>
    /// Plays the authentic smooth connection chime.
    /// </summary>
    public static void PlayConnectionChime()
    {
        Task.Run(() =>
        {
            try
            {
                // Generate a gentle, pleasant 2-tone chime (523Hz C5 -> 659Hz E5)
                byte[] wav = GenerateChimeWav();
                using var ms = new MemoryStream(wav);
                using var player = new SoundPlayer(ms);
                player.PlaySync();
            }
            catch
            {
                try
                {
                    string sysSound = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Hardware Insert.wav");
                    if (File.Exists(sysSound))
                    {
                        using var sp = new SoundPlayer(sysSound);
                        sp.Play();
                    }
                    else
                    {
                        SystemSounds.Asterisk.Play();
                    }
                }
                catch { }
            }
        });
    }

    private static byte[] GenerateChimeWav()
    {
        int sampleRate = 44100;
        double durationSeconds = 0.55;
        int totalSamples = (int)(sampleRate * durationSeconds);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF".ToCharArray());
        bw.Write(36 + totalSamples * 2);
        bw.Write("WAVE".ToCharArray());
        bw.Write("fmt ".ToCharArray());
        bw.Write(16); // subchunk1 size (16 for PCM)
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // byte rate
        bw.Write((short)2); // block align
        bw.Write((short)16); // bits per sample
        bw.Write("data".ToCharArray());
        bw.Write(totalSamples * 2);

        // Generate smooth 2-tone melodic chime: C5 (523.25Hz) followed by E5 (659.25Hz)
        int tone1Samples = (int)(sampleRate * 0.18);
        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double sampleValue = 0;

            if (i < tone1Samples)
            {
                // First ding: 523.25 Hz with fast attack and exponential decay
                double env = Math.Exp(-12.0 * t);
                sampleValue = Math.Sin(2.0 * Math.PI * 523.25 * t) * env;
            }
            else
            {
                // Second high chime: 659.25 Hz with rich overtone and smooth ringing decay
                double t2 = (double)(i - tone1Samples) / sampleRate;
                double env = Math.Exp(-6.5 * t2);
                sampleValue = (Math.Sin(2.0 * Math.PI * 659.25 * t2) * 0.85 +
                               Math.Sin(2.0 * Math.PI * 1318.5 * t2) * 0.25) * env;
            }

            short pcm = (short)(sampleValue * 14000.0);
            bw.Write(pcm);
        }

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// For instant testing / debugging: simulates a Bluetooth connection event.
    /// </summary>
    public void SimulateConnection(string name = "Buds T110", int? battery = null)
    {
        int batteryLevel = battery ?? BtBatteryReader.GetBattery(name);
        if (batteryLevel <= 0 || batteryLevel > 100) batteryLevel = 85;

        var dev = new BluetoothDeviceInfo
        {
            Id = "simulated_" + Guid.NewGuid().ToString("N"),
            Name = name,
            DeviceKind = DetectDeviceKind(name),
            BatteryPercent = batteryLevel,
            IsConnected = true,
            ConnectedTime = DateTime.Now
        };
        App.Log($"SimulateConnection: Name='{dev.Name}', Kind={dev.DeviceKind}, Battery={dev.BatteryPercent}%");
        DeviceConnected?.Invoke(dev);
    }
}

internal static class BtBatteryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
        public PROPERTYKEY(Guid guid, uint id)
        {
            fmtid = guid;
            pid = id;
        }
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref PROPERTYKEY propertyKey, out uint propertyType, IntPtr propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, StringBuilder propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_ALLCLASSES = 0x00000004;
    private const uint SPDRP_FRIENDLYNAME = 0x0000000C;

    private static readonly PROPERTYKEY BatteryPropertyKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly PROPERTYKEY BatteryTimestampKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 7);

    public static int GetBattery(string searchPattern)
    {
        try
        {
            // Scan BTHLE first (Low Energy / modern smartphones and earbuds), then BTHENUM, then BTH
            string[] enumerators = { "BTHLE", "BTHENUM", "BTH" };
            int bestBattery = -1;
            DateTime bestTime = DateTime.MinValue;

            foreach (var enumerator in enumerators)
            {
                IntPtr hDevInfo = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
                if (hDevInfo == IntPtr.Zero || hDevInfo == (IntPtr)(-1)) continue;

                try
                {
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf(devInfoData);

                    uint memberIndex = 0;
                    while (SetupDiEnumDeviceInfo(hDevInfo, memberIndex, ref devInfoData))
                    {
                        memberIndex++;

                        uint dummyType, dummySize;
                        var sbName = new StringBuilder(256);
                        SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_FRIENDLYNAME, out dummyType, sbName, (uint)sbName.Capacity, out dummySize);

                        var sbId = new StringBuilder(512);
                        SetupDiGetDeviceInstanceId(hDevInfo, ref devInfoData, sbId, (uint)sbId.Capacity, out dummySize);

                        string name = sbName.ToString();
                        string instId = sbId.ToString();

                        if (!Matches(searchPattern, name, instId))
                        {
                            continue;
                        }

                        uint propType, reqSize;
                        var key = BatteryPropertyKey;
                        SetupDiGetDeviceProperty(hDevInfo, ref devInfoData, ref key, out propType, IntPtr.Zero, 0, out reqSize, 0);
                        if (reqSize > 0)
                        {
                            IntPtr buf = Marshal.AllocHGlobal((int)reqSize);
                            try
                            {
                                uint written;
                                if (SetupDiGetDeviceProperty(hDevInfo, ref devInfoData, ref key, out propType, buf, reqSize, out written, 0))
                                {
                                    byte val = Marshal.ReadByte(buf);
                                    if (val > 0 && val <= 100)
                                    {
                                        DateTime updateTime = DateTime.MinValue;

                                        // Read timestamp (PID 7) if present
                                        var timeKey = BatteryTimestampKey;
                                        uint tPropType, tReqSize;
                                        SetupDiGetDeviceProperty(hDevInfo, ref devInfoData, ref timeKey, out tPropType, IntPtr.Zero, 0, out tReqSize, 0);
                                        if (tReqSize == 8)
                                        {
                                            IntPtr tBuf = Marshal.AllocHGlobal(8);
                                            try
                                            {
                                                uint tWritten;
                                                if (SetupDiGetDeviceProperty(hDevInfo, ref devInfoData, ref timeKey, out tPropType, tBuf, 8, out tWritten, 0))
                                                {
                                                    long ft = Marshal.ReadInt64(tBuf);
                                                    if (ft > 0)
                                                    {
                                                        updateTime = DateTime.FromFileTimeUtc(ft);
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Marshal.FreeHGlobal(tBuf);
                                            }
                                        }

                                        // Choose the freshest timestamp or initial valid value
                                        if (updateTime > bestTime || bestBattery == -1)
                                        {
                                            bestBattery = val;
                                            bestTime = updateTime;
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(buf);
                            }
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(hDevInfo);
                }
            }

            return bestBattery;
        }
        catch (Exception ex)
        {
            App.Log($"BtBatteryReader error: {ex.Message}");
            return -1;
        }
    }

    private static bool Matches(string searchPattern, string name, string instId)
    {
        if (string.IsNullOrWhiteSpace(searchPattern)) return true;

        string normSearch = searchPattern.Replace("’", "'").Trim();
        string normName = name.Replace("’", "'").Trim();
        string normId = instId.Replace("’", "'").Trim();

        if (normName.IndexOf(normSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
            normId.IndexOf(normSearch, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        var parts = normSearch.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Length >= 3 && (normName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  normId.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        return false;
    }
}
