using System;
using System.Windows.Media;

namespace WindowsLive.Models;

public class MediaTrackInfo
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string AlbumTitle { get; set; } = string.Empty;
    public string SourceApp { get; set; } = string.Empty;
    public ImageSource? Thumbnail { get; set; }
    public bool IsPlaying { get; set; }
    public TimeSpan Position { get; set; } = TimeSpan.Zero;
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);
    public bool HasMedia { get; set; } = false;
    public bool IsBrowser { get; set; } = false;

    public bool IsDetectedBrowser => IsBrowser ||
        (!string.IsNullOrWhiteSpace(SourceApp) && (
            SourceApp.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("msedge", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Brave", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Opera", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
            SourceApp.Contains("Chromium", StringComparison.OrdinalIgnoreCase)
        ));

    public double ProgressFraction
    {
        get
        {
            if (Duration.TotalSeconds <= 0) return 0.0;
            double frac = Position.TotalSeconds / Duration.TotalSeconds;
            return Math.Clamp(frac, 0.0, 1.0);
        }
    }

    public string PositionString => $"{(int)Position.TotalMinutes}:{Position.Seconds:D2}";
    public string DurationString => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";
}
