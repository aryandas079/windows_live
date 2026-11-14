using System;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace WindowsLive.Services;

public class ThemeService
{
    public event Action? ThemeChanged;

    public bool IsDarkTheme { get; private set; } = true;
    public bool TransparencyEnabled { get; private set; } = true;
    public Color AccentColor { get; private set; }
    public SolidColorBrush AccentBrush { get; private set; } = new(Colors.DodgerBlue);

    // Dynamic surfaces
    public SolidColorBrush SurfaceDarkBrush { get; private set; } = new(Color.FromArgb(128, 10, 10, 11));
    public SolidColorBrush SurfaceBorderBrush { get; private set; } = new(Color.FromArgb(43, 255, 255, 255));
    public SolidColorBrush SubtleHoverBrush { get; private set; } = new(Color.FromArgb(31, 255, 255, 255));
    public SolidColorBrush TextPrimaryBrush { get; private set; } = new(Color.FromArgb(242, 255, 255, 255));
    public SolidColorBrush TextSecondaryBrush { get; private set; } = new(Color.FromArgb(140, 255, 255, 255));
    public SolidColorBrush CardBackgroundBrush { get; private set; } = new(Color.FromArgb(120, 24, 24, 26));
    public SolidColorBrush PlayButtonBackgroundBrush { get; private set; } = new(Colors.White);
    public SolidColorBrush PlayButtonForegroundBrush { get; private set; } = new(Colors.Black);
    public SolidColorBrush CircleButtonBackgroundBrush { get; private set; } = new(Color.FromArgb(40, 255, 255, 255));

    public ThemeService()
    {
        UpdateTheme();
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            {
                UpdateTheme();
            }
        };
    }

    public void UpdateTheme()
    {
        // 1. Check Light vs Dark theme and Transparency settings
        bool isLight = false;
        bool transparency = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    isLight = intVal == 1;
                }

                var transVal = key.GetValue("EnableTransparency");
                if (transVal is int intTrans)
                {
                    transparency = intTrans == 1;
                }
            }
        }
        catch { }

        // Authentic Dynamic Island is always OLED dark by default
        IsDarkTheme = true;
        TransparencyEnabled = transparency;

        // 2. Fetch system accent color
        Color accent = Color.FromRgb(0, 120, 215); // Fallback Windows Blue
        try
        {
            var uiSettings = new UISettings();
            var winColor = uiSettings.GetColorValue(UIColorType.Accent);
            accent = Color.FromArgb(winColor.A, winColor.R, winColor.G, winColor.B);
        }
        catch
        {
            try
            {
                using var dwmKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dwmKey != null)
                {
                    var col = dwmKey.GetValue("AccentColor");
                    if (col is int abgr)
                    {
                        byte a = 255;
                        byte r = (byte)(abgr & 0xFF);
                        byte g = (byte)((abgr >> 8) & 0xFF);
                        byte b = (byte)((abgr >> 16) & 0xFF);
                        accent = Color.FromArgb(a, r, g, b);
                    }
                }
            }
            catch { }
        }

        AccentColor = accent;
        AccentBrush = new SolidColorBrush(AccentColor);
        AccentBrush.Freeze();

        // 3. Totally Opaque Surface Brushes (Solid 255 alpha)
        if (IsDarkTheme)
        {
            // Dark Mode palette (OLED solid dark matching authentic Dynamic Island)
            SurfaceDarkBrush = new SolidColorBrush(Color.FromRgb(10, 10, 12));
            SurfaceBorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
            SubtleHoverBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            TextPrimaryBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            TextSecondaryBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            CardBackgroundBrush = new SolidColorBrush(Color.FromRgb(24, 24, 28));
            PlayButtonBackgroundBrush = new SolidColorBrush(Colors.White);
            PlayButtonForegroundBrush = new SolidColorBrush(Color.FromRgb(10, 10, 12));
            CircleButtonBackgroundBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
        }
        else
        {
            // Light Mode palette (Matches Windows Light Theme, totally solid/opaque)
            SurfaceDarkBrush = new SolidColorBrush(Color.FromRgb(245, 245, 247));
            SurfaceBorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            SubtleHoverBrush = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
            TextPrimaryBrush = new SolidColorBrush(Color.FromRgb(20, 20, 24));
            TextSecondaryBrush = new SolidColorBrush(Color.FromRgb(95, 95, 105));
            CardBackgroundBrush = new SolidColorBrush(Color.FromRgb(230, 230, 236));
            PlayButtonBackgroundBrush = new SolidColorBrush(Color.FromRgb(20, 20, 24));
            PlayButtonForegroundBrush = new SolidColorBrush(Colors.White);
            CircleButtonBackgroundBrush = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0));
        }

        SurfaceDarkBrush.Freeze();
        SurfaceBorderBrush.Freeze();
        SubtleHoverBrush.Freeze();
        TextPrimaryBrush.Freeze();
        TextSecondaryBrush.Freeze();
        CardBackgroundBrush.Freeze();
        PlayButtonBackgroundBrush.Freeze();
        PlayButtonForegroundBrush.Freeze();
        CircleButtonBackgroundBrush.Freeze();

        ThemeChanged?.Invoke();
    }
}
