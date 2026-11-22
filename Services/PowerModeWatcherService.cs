using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.System.Power;

namespace WindowsLive.Services;

public enum WindowsPowerMode
{
    BestPowerEfficiency,
    Balanced,
    BestPerformance
}

/// <summary>
/// Monitors Windows 10/11 User Power Mode changes ("Best Power Efficiency", "Balanced", "Best Performance")
/// using a dual mechanism: PowrProf.dll effective power mode notifications + registry polling fallback.
/// </summary>
public class PowerModeWatcherService
{
    public event Action<WindowsPowerMode>? PowerModeChanged;

    public WindowsPowerMode CurrentMode { get; private set; } = WindowsPowerMode.Balanced;

    private enum EFFECTIVE_POWER_MODE
    {
        EffectivePowerModeBatterySaver = 0,
        EffectivePowerModeBetterBattery = 1,
        EffectivePowerModeBalanced = 2,
        EffectivePowerModeHighPerformance = 3,
        EffectivePowerModeMaxPerformance = 4,
        EffectivePowerModeGameMode = 5
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EffectivePowerModeCallback(EFFECTIVE_POWER_MODE Mode, IntPtr Context);

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern int PowerRegisterForEffectivePowerModeNotifications(
        uint Version,
        EffectivePowerModeCallback Callback,
        IntPtr Context,
        out IntPtr RegistrationHandle
    );

    [DllImport("PowrProf.dll", SetLastError = true)]
    private static extern int PowerUnregisterFromEffectivePowerModeNotifications(
        IntPtr RegistrationHandle
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _pollTimer;
    private EffectivePowerModeCallback? _callback;
    private IntPtr _regHandle = IntPtr.Zero;
    private bool _isFirstCallback = true;
    private bool _started = false;

    private const string PowerSchemesRegKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

    public PowerModeWatcherService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        CurrentMode = ResolveCurrentSystemMode();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _pollTimer.Tick += (s, e) => CheckSystemPowerMode();
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            _callback = OnEffectivePowerModeChanged;
            const uint EFFECTIVE_POWER_MODE_V2 = 2;
            int hr = PowerRegisterForEffectivePowerModeNotifications(EFFECTIVE_POWER_MODE_V2, _callback, IntPtr.Zero, out _regHandle);
            if (hr != 0)
            {
                const uint EFFECTIVE_POWER_MODE_V1 = 1;
                hr = PowerRegisterForEffectivePowerModeNotifications(EFFECTIVE_POWER_MODE_V1, _callback, IntPtr.Zero, out _regHandle);
            }

            if (hr == 0)
            {
                App.Log($"PowerModeWatcherService registered successfully (Handle={_regHandle})");
            }
            else
            {
                App.Log($"PowerModeWatcherService failed to register notifications: hr=0x{hr:X}");
            }
        }
        catch (Exception ex)
        {
            App.Log($"PowerModeWatcherService Start error: {ex.Message}");
        }

        try
        {
            PowerManager.EnergySaverStatusChanged += (s, e) => CheckSystemPowerMode();
        }
        catch { }

        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
        try
        {
            if (_regHandle != IntPtr.Zero)
            {
                PowerUnregisterFromEffectivePowerModeNotifications(_regHandle);
                _regHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            App.Log($"PowerModeWatcherService Stop error: {ex.Message}");
        }
    }

    public void CheckSystemPowerMode(bool forceNotify = false)
    {
        try
        {
            var resolved = ResolveCurrentSystemMode();
            if (resolved != CurrentMode || forceNotify)
            {
                App.Log($"PowerModeWatcherService: Mode change detected via Registry/EnergySaver: {CurrentMode} -> {resolved} (Force={forceNotify})");
                CurrentMode = resolved;
                _dispatcher.Invoke(() =>
                {
                    PowerModeChanged?.Invoke(resolved);
                });
            }
        }
        catch { }
    }

    private static WindowsPowerMode ResolveCurrentSystemMode()
    {
        try
        {
            // 1. Check Energy Saver
            try
            {
                if (PowerManager.EnergySaverStatus == EnergySaverStatus.On)
                {
                    return WindowsPowerMode.BestPowerEfficiency;
                }
            }
            catch { }

            // 2. Read Active Overlay AC/DC power scheme & ActivePowerScheme
            object? acScheme = Registry.GetValue(PowerSchemesRegKey, "ActiveOverlayAcPowerScheme", null);
            object? dcScheme = Registry.GetValue(PowerSchemesRegKey, "ActiveOverlayDcPowerScheme", null);
            object? activeScheme = Registry.GetValue(PowerSchemesRegKey, "ActivePowerScheme", null);

            bool isAc = true;
            if (GetSystemPowerStatus(out var status))
            {
                isAc = status.ACLineStatus != 0;
            }

            string overlayStr = (isAc
                ? (acScheme as string ?? dcScheme as string)
                : (dcScheme as string ?? acScheme as string)) ?? string.Empty;
            overlayStr = overlayStr.ToLowerInvariant();

            string schemeStr = (activeScheme as string ?? string.Empty).ToLowerInvariant();

            // 961cc777 = Better Battery-life Overlay (matches Win 11 Best Power Efficiency)
            if (overlayStr.Contains("961cc777") || schemeStr.Contains("a1841308"))
            {
                return WindowsPowerMode.BestPowerEfficiency;
            }

            // ded574b5 = Max Performance Overlay, 3af9b8d9 = High Performance Overlay
            // 8c5e7fda = High Performance, e9a42b02 = Ultimate Performance
            if (overlayStr.Contains("ded574b5") || overlayStr.Contains("3af9b8d9") ||
                schemeStr.Contains("8c5e7fda") || schemeStr.Contains("e9a42b02"))
            {
                return WindowsPowerMode.BestPerformance;
            }
        }
        catch { }

        return WindowsPowerMode.Balanced;
    }

    private void OnEffectivePowerModeChanged(EFFECTIVE_POWER_MODE rawMode, IntPtr context)
    {
        WindowsPowerMode mode = rawMode switch
        {
            EFFECTIVE_POWER_MODE.EffectivePowerModeBatterySaver or EFFECTIVE_POWER_MODE.EffectivePowerModeBetterBattery => WindowsPowerMode.BestPowerEfficiency,
            EFFECTIVE_POWER_MODE.EffectivePowerModeHighPerformance or EFFECTIVE_POWER_MODE.EffectivePowerModeMaxPerformance or EFFECTIVE_POWER_MODE.EffectivePowerModeGameMode => WindowsPowerMode.BestPerformance,
            _ => WindowsPowerMode.Balanced
        };

        bool isFirst = _isFirstCallback;
        _isFirstCallback = false;

        App.Log($"PowerModeWatcherService: RawMode={rawMode} => WindowsPowerMode={mode} (Initial={isFirst})");

        if (!isFirst && mode != CurrentMode)
        {
            CurrentMode = mode;
            App.Log($"PowerModeWatcherService: Dispatched PowerModeChanged({mode})");
            _dispatcher.Invoke(() =>
            {
                PowerModeChanged?.Invoke(mode);
            });
        }
    }

    /// <summary>
    /// For instant testing / debugging: simulates a power mode change.
    /// </summary>
    public void SimulateMode(WindowsPowerMode mode)
    {
        CurrentMode = mode;
        _dispatcher.Invoke(() =>
        {
            PowerModeChanged?.Invoke(mode);
        });
    }
}
