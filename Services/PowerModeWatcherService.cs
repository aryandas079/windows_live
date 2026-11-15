using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace WindowsLive.Services;

public enum WindowsPowerMode
{
    BestPowerEfficiency,
    Balanced,
    BestPerformance
}

/// <summary>
/// Monitors Windows 10/11 User Power Mode changes ("Best Power Efficiency", "Balanced", "Best Performance")
/// using the official Effective Power Mode notifications API from PowrProf.dll.
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

    private readonly Dispatcher _dispatcher;
    private EffectivePowerModeCallback? _callback;
    private IntPtr _regHandle = IntPtr.Zero;
    private bool _isFirstCallback = true;

    public PowerModeWatcherService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
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
    }

    public void Stop()
    {
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

        WindowsPowerMode previous = CurrentMode;
        CurrentMode = mode;

        App.Log($"PowerModeWatcherService: RawMode={rawMode} => WindowsPowerMode={mode} (Initial={isFirst})");

        // Suppress notification on cold launch; only notify on actual subsequent changes
        if (!isFirst && mode != previous)
        {
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
