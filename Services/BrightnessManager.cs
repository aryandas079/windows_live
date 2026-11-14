using System;
using System.Management;

namespace WindowsLive.Services;

public class BrightnessManager
{
    public static int GetBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                var val = obj["CurrentBrightness"];
                if (val != null)
                {
                    return Convert.ToInt32(val);
                }
            }
        }
        catch { }
        return 75; // Default fallback
    }

    public static void SetBrightness(int percent)
    {
        try
        {
            percent = Math.Clamp(percent, 0, 100);
            using var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)percent });
                break;
            }
        }
        catch { }
    }
}
