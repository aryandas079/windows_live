using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.System.Power;

namespace WindowsLive.Services;

public class PowerManagerService
{
    public event Action? PowerStatusChanged;
    public event Action? ChargingConnected;

    public int BatteryPercent { get; private set; } = 100;
    public bool IsCharging { get; private set; }
    public bool HasBattery { get; private set; } = true;

    private readonly DispatcherTimer _pollTimer;
    private bool _lastChargingState = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Offline, 1 = Online, 255 = Unknown
        public byte BatteryFlag;  // 1 = High, 2 = Low, 4 = Critical, 8 = Charging, 128 = No battery
        public byte BatteryLifePercent; // 0-100 or 255
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public PowerManagerService()
    {
        UpdatePowerStatus(isInitial: true);

        try
        {
            PowerManager.RemainingChargePercentChanged += (s, e) => UpdatePowerStatus();
            PowerManager.BatteryStatusChanged += (s, e) => UpdatePowerStatus();
            PowerManager.PowerSupplyStatusChanged += (s, e) => UpdatePowerStatus();
        }
        catch { }

        // Fast poll timer ensures instant detection of cable plug-in/unplug
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _pollTimer.Tick += (s, e) => UpdatePowerStatus();
        _pollTimer.Start();
    }

    public void TriggerChargingPreview()
    {
        ChargingConnected?.Invoke();
    }

    private void UpdatePowerStatus(bool isInitial = false)
    {
        int newPercent = 100;
        bool newCharging = false;
        bool newHasBattery = true;

        bool readWin32 = false;
        if (GetSystemPowerStatus(out var status))
        {
            readWin32 = true;
            if (status.BatteryLifePercent <= 100)
            {
                newPercent = status.BatteryLifePercent;
            }
            newCharging = status.ACLineStatus == 1 || (status.BatteryFlag & 8) != 0;
            newHasBattery = (status.BatteryFlag & 128) == 0;
        }

        if (!readWin32)
        {
            try
            {
                newPercent = PowerManager.RemainingChargePercent;
                var batStatus = PowerManager.BatteryStatus;
                newCharging = batStatus == BatteryStatus.Charging || PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
                newHasBattery = batStatus != BatteryStatus.NotPresent;
            }
            catch
            {
                newPercent = 100;
                newCharging = true;
                newHasBattery = false;
            }
        }

        bool chargingTransition = !isInitial && !_lastChargingState && newCharging;

        BatteryPercent = newPercent;
        IsCharging = newCharging;
        HasBattery = newHasBattery;
        _lastChargingState = newCharging;

        PowerStatusChanged?.Invoke();

        if (chargingTransition)
        {
            ChargingConnected?.Invoke();
        }
    }
}
