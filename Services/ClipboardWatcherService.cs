using System;
using System.Windows;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class ClipboardWatcherService
{
    public event Action<string>? ClipboardTextChanged;
    private readonly DispatcherTimer _pollTimer;
    private string _lastText = string.Empty;
    public string LastCopiedText => _lastText;

    public ClipboardWatcherService()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _pollTimer.Tick += OnTick;
        _pollTimer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(text) && text != _lastText)
                {
                    _lastText = text;
                    ClipboardTextChanged?.Invoke(_lastText);
                }
            }
        }
        catch { }
    }
}
