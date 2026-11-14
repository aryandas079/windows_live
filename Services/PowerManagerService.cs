using System;
using Windows.System.Power;

namespace WindowsLive.Services;

public class PowerManagerService
{
    public event Action? PowerStatusChanged;

    public int BatteryPercent { get; private set; } = 100;
    public bool IsCharging { get; private set; }
    public bool HasBattery { get; private set; } = true;

    public PowerManagerService()
    {
        try
        {
            UpdatePowerStatus();
            PowerManager.RemainingChargePercentChanged += (s, e) => UpdatePowerStatus();
            PowerManager.BatteryStatusChanged += (s, e) => UpdatePowerStatus();
            PowerManager.PowerSupplyStatusChanged += (s, e) => UpdatePowerStatus();
        }
        catch
        {
            // Fallback for non-laptop or VMs
            BatteryPercent = 100;
            IsCharging = true;
            HasBattery = false;
        }
    }

    private void UpdatePowerStatus()
    {
        try
        {
            BatteryPercent = PowerManager.RemainingChargePercent;
            var status = PowerManager.BatteryStatus;
            IsCharging = status == BatteryStatus.Charging || PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
            HasBattery = status != BatteryStatus.NotPresent;
        }
        catch
        {
            BatteryPercent = 100;
            IsCharging = true;
            HasBattery = false;
        }

        PowerStatusChanged?.Invoke();
    }
}
