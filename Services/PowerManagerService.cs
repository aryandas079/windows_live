using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.System.Power;

namespace WindowsLive.Services;

public enum BatteryAlertType
{
    Charging,
    LowBattery,
    Unplugged,
    PowerSaver
}

public class PowerManagerService
{
    public event Action? PowerStatusChanged;
    public event Action? ChargingConnected;
    public event Action<BatteryAlertType>? BatteryAlert;

    public int BatteryPercent { get; private set; } = 100;
    public bool IsCharging { get; private set; }
    public bool HasBattery { get; private set; } = true;
    public bool IsEnergySaverOn { get; private set; } = false;

    private readonly DispatcherTimer _pollTimer;
    private bool _lastChargingState = false;
    private int _lastPercent = 100;
    private bool _lastEnergySaverState = false;
    private bool _lowBatteryAlerted30 = false;
    private bool _lowBatteryAlerted20 = false;
    private bool _lowBatteryAlerted10 = false;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Offline, 1 = Online, 255 = Unknown
        public byte BatteryFlag;  // 1 = High, 2 = Low, 4 = Critical, 8 = Charging, 128 = No battery
        public byte BatteryLifePercent; // 0-100 or 255
        public byte SystemStatusFlag; // Bit 0 = Battery saver is on
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
            PowerManager.EnergySaverStatusChanged += (s, e) => UpdatePowerStatus();
        }
        catch { }

        // Fast poll timer ensures instant detection of cable plug-in/unplug/battery-saver toggle
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _pollTimer.Tick += (s, e) => UpdatePowerStatus();
        _pollTimer.Start();
    }

    public void TriggerAlert(BatteryAlertType type)
    {
        BatteryAlert?.Invoke(type);
    }

    public void TriggerChargingPreview()
    {
        TriggerAlert(BatteryAlertType.Charging);
    }

    private void UpdatePowerStatus(bool isInitial = false)
    {
        int newPercent = 100;
        bool newCharging = false;
        bool newHasBattery = true;
        bool newEnergySaver = false;

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
            newEnergySaver = (status.SystemStatusFlag & 1) != 0;
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

        try
        {
            if (PowerManager.EnergySaverStatus == EnergySaverStatus.On)
            {
                newEnergySaver = true;
            }
        }
        catch { }

        bool chargingTransition = !isInitial && !_lastChargingState && newCharging;
        bool unpluggedTransition = !isInitial && _lastChargingState && !newCharging;
        bool powerSaverTransition = !isInitial && !_lastEnergySaverState && newEnergySaver;

        bool lowBatteryTransition = false;
        // User requirement: Low battery pop out when battery is below 30%
        if (!newCharging && newPercent < 30)
        {
            if (!_lowBatteryAlerted30)
            {
                _lowBatteryAlerted30 = true;
                lowBatteryTransition = true;
            }
            else if (newPercent <= 20 && !_lowBatteryAlerted20)
            {
                _lowBatteryAlerted20 = true;
                lowBatteryTransition = true;
            }
            else if (newPercent <= 10 && !_lowBatteryAlerted10)
            {
                _lowBatteryAlerted10 = true;
                lowBatteryTransition = true;
            }
        }
        else if (newCharging || newPercent >= 30)
        {
            // Reset low battery flags when plugged in or charged back above 30%
            _lowBatteryAlerted30 = false;
            _lowBatteryAlerted20 = false;
            _lowBatteryAlerted10 = false;
        }

        BatteryPercent = newPercent;
        IsCharging = newCharging;
        HasBattery = newHasBattery;
        IsEnergySaverOn = newEnergySaver;

        _lastChargingState = newCharging;
        _lastPercent = newPercent;
        _lastEnergySaverState = newEnergySaver;

        PowerStatusChanged?.Invoke();

        if (chargingTransition)
        {
            ChargingConnected?.Invoke();
            BatteryAlert?.Invoke(BatteryAlertType.Charging);
        }
        else if (unpluggedTransition)
        {
            if (newPercent < 30)
            {
                BatteryAlert?.Invoke(BatteryAlertType.LowBattery);
            }
            else
            {
                BatteryAlert?.Invoke(BatteryAlertType.Unplugged);
            }
        }
        else if (powerSaverTransition)
        {
            BatteryAlert?.Invoke(BatteryAlertType.PowerSaver);
        }
        else if (lowBatteryTransition)
        {
            BatteryAlert?.Invoke(BatteryAlertType.LowBattery);
        }
    }
}
