using System;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class FocusTimerService
{
    public event Action? Tick;
    public event Action? Completed;

    private readonly DispatcherTimer _timer;
    private TimeSpan _remaining = TimeSpan.Zero;
    private bool _isRunning = false;
    private DateTime _endTime;

    public TimeSpan Remaining => _remaining;
    public string FormattedTime
    {
        get
        {
            int totalSeconds = Math.Max(0, (int)Math.Ceiling(_remaining.TotalSeconds));
            return $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
        }
    }
    public bool IsRunning => _isRunning;

    public FocusTimerService()
    {
        _remaining = TimeSpan.Zero;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) =>
        {
            if (!_isRunning) return;

            _remaining = _endTime - DateTime.Now;
            if (_remaining.TotalSeconds > 0)
            {
                Tick?.Invoke();
            }
            else
            {
                _remaining = TimeSpan.Zero;
                _isRunning = false;
                _timer.Stop();
                Tick?.Invoke();
                Completed?.Invoke();
            }
        };
    }

    public void Start(int minutes = 25)
    {
        _remaining = TimeSpan.FromMinutes(minutes);
        _endTime = DateTime.Now + _remaining;
        _isRunning = true;
        _timer.Start();
        Tick?.Invoke();
    }

    public void StartSeconds(int seconds)
    {
        _remaining = TimeSpan.FromSeconds(seconds);
        _endTime = DateTime.Now + _remaining;
        _isRunning = true;
        _timer.Start();
        Tick?.Invoke();
    }

    public void Pause()
    {
        if (_isRunning) _remaining = _endTime - DateTime.Now;
        if (_remaining < TimeSpan.Zero) _remaining = TimeSpan.Zero;
        _isRunning = false;
        _timer.Stop();
        Tick?.Invoke();
    }

    public void Resume()
    {
        if (_remaining.TotalSeconds > 0)
        {
            _endTime = DateTime.Now + _remaining;
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
