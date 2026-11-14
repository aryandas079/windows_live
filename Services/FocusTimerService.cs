using System;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class FocusTimerService
{
    public event Action? Tick;
    public event Action? Completed;

    private readonly DispatcherTimer _timer;
    private TimeSpan _remaining = TimeSpan.FromMinutes(25);
    private bool _isRunning = false;

    public TimeSpan Remaining => _remaining;
    public string FormattedTime => $"{_remaining.Minutes:D2}:{_remaining.Seconds:D2}";
    public bool IsRunning => _isRunning;

    public FocusTimerService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) =>
        {
            if (_remaining.TotalSeconds > 0)
            {
                _remaining = _remaining.Subtract(TimeSpan.FromSeconds(1));
                Tick?.Invoke();
            }
            else
            {
                Stop();
                Completed?.Invoke();
            }
        };
    }

    public void Start(int minutes = 25)
    {
        _remaining = TimeSpan.FromMinutes(minutes);
        _isRunning = true;
        _timer.Start();
        Tick?.Invoke();
    }

    public void StartSeconds(int seconds)
    {
        _remaining = TimeSpan.FromSeconds(seconds);
        _isRunning = true;
        _timer.Start();
        Tick?.Invoke();
    }

    public void Pause()
    {
        _isRunning = false;
        _timer.Stop();
        Tick?.Invoke();
    }

    public void Resume()
    {
        if (_remaining.TotalSeconds > 0)
        {
            _isRunning = true;
            _timer.Start();
            Tick?.Invoke();
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();
        _remaining = TimeSpan.Zero;
        Tick?.Invoke();
    }

    public void Toggle(int minutes = 25)
    {
        if (_isRunning) Stop();
        else Start(minutes);
    }
}
