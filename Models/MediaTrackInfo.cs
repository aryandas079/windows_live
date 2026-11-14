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
