using System;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WindowsLive.Services;

public enum FocusAssistState
{
    Off,            // Unrestricted
    PriorityOnly,   // Priority Only
    AlarmsOnly      // Alarms Only
}

public class FocusAssistWatcherService
{
    public event Action<FocusAssistState>? FocusAssistChanged;

    private readonly DispatcherTimer _pollTimer;
    private FocusAssistState _lastState = FocusAssistState.Off;
    private bool _initialized = false;

    public FocusAssistState CurrentState => _lastState;

    public FocusAssistWatcherService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _pollTimer.Tick += (s, e) => CheckState();
    }

    public void Start()
    {
        CheckState(initial: true);
        _pollTimer.Start();
    }

    private void CheckState(bool initial = false)
    {
        try
        {
            FocusAssistState state = FocusAssistState.Off;

            // 1. ToastNotificationManager
            try
            {
                var manager = Windows.UI.Notifications.ToastNotificationManager.GetDefault();
                if (manager != null)
                {
                    state = manager.NotificationMode switch
                    {
                        Windows.UI.Notifications.ToastNotificationMode.PriorityOnly => FocusAssistState.PriorityOnly,
                        Windows.UI.Notifications.ToastNotificationMode.AlarmsOnly => FocusAssistState.AlarmsOnly,
                        _ => FocusAssistState.Off
                    };
                }
            }
            catch { }

            // 2. Fallback to Windows 11 Registry
            if (state == FocusAssistState.Off)
            {
                try
                {
                    object? toastsEnabled = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED", null);
                    if (toastsEnabled is int val && val == 0)
                    {
                        state = FocusAssistState.PriorityOnly;
                    }
                }
                catch { }
            }

            if (!_initialized)
            {
                _initialized = true;
                _lastState = state;
                return;
            }

            if (state != _lastState)
            {
                App.Log($"FocusAssistWatcherService: Focus state changed to {state}");
                _lastState = state;
                FocusAssistChanged?.Invoke(state);
            }
        }
        catch (Exception ex)
        {
            App.Log($"FocusAssistWatcherService CheckState error: {ex.Message}");
        }
    }
}
