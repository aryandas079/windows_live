using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using WindowsLive.Controls;
using WindowsLive.Models;
using WindowsLive.Services;

namespace WindowsLive;

public partial class MainWindow : Window
{
    private readonly MediaManager _mediaManager;
    private readonly VolumeManager _volumeManager;
    private readonly PowerManagerService _powerService;
    private readonly ThemeService _themeService;
    private readonly AppStateTracker _appStateTracker;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _autoCollapseTimer;

    private bool _isExpanded = false;
    private bool _isPinned = false;
    private bool _isNotchMode = true;
    private bool _isDraggingSlider = false;
    private bool _isHoverRevealed = false;

    private const double CompactClockWidth = 240.0;
    private const double CompactMediaWidth = 340.0;
    private const double CompactHeight = 40.0;
    private const double ExpandedWidth = 540.0;
    private const double ExpandedHeight = 195.0;

    private double CurrentCompactWidth => (_mediaManager?.CurrentTrack?.HasMedia ?? false) ? CompactMediaWidth : CompactClockWidth;

    public MainWindow()
    {
        App.Log("MainWindow constructor started");
        try
        {
            InitializeComponent();
            App.Log("MainWindow InitializeComponent finished");
        }
        catch (Exception ex)
        {
            App.Log($"EXCEPTION IN MainWindow.InitializeComponent: {ex}");
            throw;
        }

        _themeService = new ThemeService();
        _themeService.ThemeChanged += OnThemeChanged;
        ApplyThemeResources();

        _appStateTracker = new AppStateTracker();
        _appStateTracker.StateChanged += OnForegroundAppStateChanged;

        _mediaManager = new MediaManager(Dispatcher);
        _volumeManager = new VolumeManager();
        _powerService = new PowerManagerService();

        _mediaManager.TrackChanged += OnTrackChanged;
        _volumeManager.VolumeChanged += OnVolumeChanged;
        _powerService.PowerStatusChanged += OnPowerStatusChanged;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) => UpdateClock();
        _clockTimer.Start();

        _autoCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200)
        };
        _autoCollapseTimer.Tick += (s, e) =>
        {
            _autoCollapseTimer.Stop();
            if (!_isPinned && !IslandContainer.IsMouseOver)
            {
                CollapseDock();
                if (_appStateTracker.CurrentState == ForegroundAppState.Maximized)
                {
                    _isHoverRevealed = false;
                    HideDockSmooth();
                }
            }
        };

        SourceInitialized += (s, e) => App.Log("MainWindow SourceInitialized");
        Loaded += OnLoaded;
        Closing += (s, e) => App.Log($"MainWindow Closing: Cancel={e.Cancel}");
        Closed += (s, e) => App.Log("MainWindow Closed");
        PositionWindow();

        IslandContainer.MouseEnter += OnIslandMouseEnter;
        IslandContainer.MouseLeave += OnIslandMouseLeave;
        IslandContainer.MouseDown += OnIslandMouseDown;
        App.Log("MainWindow constructor finished");
    }

    private void OnForegroundAppStateChanged(ForegroundAppState state)
    {
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case ForegroundAppState.FullScreen:
                    // Fullscreen mode: do not appear at all
                    _isHoverRevealed = false;
                    IslandContainer.Visibility = Visibility.Collapsed;
                    TopEdgeHotspot.Visibility = Visibility.Collapsed;
                    break;

                case ForegroundAppState.Maximized:
                    // Maximized mode: hide by default; reveal only when cursor moves to top edge
                    TopEdgeHotspot.Visibility = Visibility.Visible;
                    if (!IslandContainer.IsMouseOver && !_isHoverRevealed)
                    {
                        HideDockSmooth();
                    }
                    break;

                case ForegroundAppState.Normal:
                    // Desktop / non-maximized apps: normal dynamic island visible at top
                    TopEdgeHotspot.Visibility = Visibility.Collapsed;
                    _isHoverRevealed = false;
                    ShowDockSmooth();
                    break;
            }
        });
    }

    private void TopEdgeHotspot_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_appStateTracker.CurrentState == ForegroundAppState.Maximized)
        {
            _isHoverRevealed = true;
            ShowDockSmooth();
        }
    }

    private void HideDockSmooth()
    {
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180));
        anim.Completed += (s, e) =>
        {
            if (!_isHoverRevealed && _appStateTracker.CurrentState == ForegroundAppState.Maximized)
            {
                IslandContainer.Visibility = Visibility.Collapsed;
            }
        };
        IslandContainer.BeginAnimation(OpacityProperty, anim);
    }

    private void ShowDockSmooth()
    {
        IslandContainer.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180));
        IslandContainer.BeginAnimation(OpacityProperty, anim);
    }

    private void OnThemeChanged()
    {
        Dispatcher.Invoke(() =>
        {
            ApplyThemeResources();
            UpdateControlThemeColors();
        });
    }

    private void ApplyThemeResources()
    {
        Application.Current.Resources["AccentColorBrush"] = _themeService.AccentBrush;
        Application.Current.Resources["AccentLimeBrush"] = _themeService.AccentBrush;
        Application.Current.Resources["SurfaceDarkBrush"] = _themeService.SurfaceDarkBrush;
        Application.Current.Resources["SurfaceBorderBrush"] = _themeService.SurfaceBorderBrush;
        Application.Current.Resources["SubtleHoverBrush"] = _themeService.SubtleHoverBrush;
        Application.Current.Resources["TextPrimaryBrush"] = _themeService.TextPrimaryBrush;
        Application.Current.Resources["TextSecondaryBrush"] = _themeService.TextSecondaryBrush;
        Application.Current.Resources["CardBackgroundBrush"] = _themeService.CardBackgroundBrush;
        Application.Current.Resources["PlayButtonForegroundBrush"] = _themeService.PlayButtonForegroundBrush;

        if (!_themeService.IsDarkTheme)
        {
            IslandShadowEffect.Color = Color.FromArgb(255, 60, 60, 70);
            IslandShadowEffect.Opacity = 0.22;
            IslandShadowEffect.BlurRadius = 18;
            IslandShadow.Fill = new SolidColorBrush(Color.FromRgb(220, 220, 225));
        }
        else
        {
            IslandShadowEffect.Color = Colors.Black;
            IslandShadowEffect.Opacity = 0.5;
            IslandShadowEffect.BlurRadius = 22;
            IslandShadow.Fill = new SolidColorBrush(Colors.Black);
        }

        UpdateControlThemeColors();
    }

    private void UpdateControlThemeColors()
    {
        CompactProgressArt.AccentBrush = _themeService.AccentBrush;
        CompactEqualizer.AccentBrush = _themeService.AccentBrush;

        if (_isPinned)
        {
            PinIcon.Foreground = _themeService.AccentBrush;
        }
        else
        {
            PinIcon.Foreground = _themeService.TextSecondaryBrush;
        }

        PlayPauseIcon.Foreground = _themeService.PlayButtonForegroundBrush;
        ExpandedOrbSymbol.Foreground = _themeService.PlayButtonForegroundBrush;
        ExpandedDefaultOrb.Fill = _themeService.AccentBrush;
    }

    private void PositionWindow()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = (screenWidth - Width) / 2.0;
        Top = 0;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Log("MainWindow OnLoaded started");
        WindowHelper.EnsureTopMost(this);

        var helper = new WindowInteropHelper(this);
        _appStateTracker.SetDockHwnd(helper.Handle);

        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);

        ApplyThemeResources();
        UpdateClock();
        OnPowerStatusChanged();
        _volumeManager.PollVolumeState(forceNotify: true);
        OnTrackChanged(_mediaManager.CurrentTrack);
        App.Log("MainWindow OnLoaded finished");
    }

    private async void ExportSnapshotsAsync()
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(600);
            string outDir = @"C:\Users\aryan\.gemini\antigravity\brain\ac89aa2c-42c4-4c90-b9ee-e64b5d2161f0";
            SaveElementPng(IslandContainer, Path.Combine(outDir, "dock_realigned_compact.png"));

            ExpandDock();
            await System.Threading.Tasks.Task.Delay(400);

            SaveElementPng(IslandContainer, Path.Combine(outDir, "dock_realigned_expanded.png"));

            CollapseDock();
        }
        catch (Exception ex)
        {
            App.Log($"Snapshot export error: {ex}");
        }
    }

    private void SaveElementPng(FrameworkElement element, string filePath)
    {
        element.UpdateLayout();
        int w = (int)Math.Ceiling(Math.Max(1, element.ActualWidth));
        int h = (int)Math.Ceiling(Math.Max(1, element.ActualHeight));

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
        App.Log($"Saved snapshot: {filePath} ({w}x{h})");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WindowHelper.WM_NCHITTEST)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            Point windowPoint = PointFromScreen(new Point(x, y));

            // Check if dock is currently visible and point is inside it
            if (IslandContainer.Visibility == Visibility.Visible && IslandContainer.Opacity > 0.1)
            {
                Point islandPoint = IslandContainer.TranslatePoint(new Point(0, 0), this);
                Rect islandRect = new Rect(islandPoint.X, islandPoint.Y, IslandContainer.ActualWidth, IslandContainer.ActualHeight);

                if (islandRect.Contains(windowPoint))
                {
                    handled = true;
                    return (IntPtr)WindowHelper.HTCLIENT;
                }
            }

            // Check if point is inside top edge hotspot (when maximized)
            if (TopEdgeHotspot.Visibility == Visibility.Visible)
            {
                Point hotspotPoint = TopEdgeHotspot.TranslatePoint(new Point(0, 0), this);
                Rect hotspotRect = new Rect(hotspotPoint.X, hotspotPoint.Y, TopEdgeHotspot.ActualWidth, TopEdgeHotspot.ActualHeight);

                if (hotspotRect.Contains(windowPoint))
                {
                    handled = true;
                    return (IntPtr)WindowHelper.HTCLIENT;
                }
            }

            // Outside island and hotspot: pass click straight through
            handled = true;
            return (IntPtr)WindowHelper.HTTRANSPARENT;
        }

        return IntPtr.Zero;
    }

    private void OnIslandMouseEnter(object sender, MouseEventArgs e)
    {
        _autoCollapseTimer.Stop();
        if (!_isExpanded)
        {
            ExpandDock();
        }
    }

    private void OnIslandMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isExpanded && !_isPinned && !_isDraggingSlider)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Start();
        }
        else if (!_isExpanded && _appStateTracker.CurrentState == ForegroundAppState.Maximized)
        {
            // If collapsed in maximized mode and cursor leaves island, hide it
            _isHoverRevealed = false;
            HideDockSmooth();
        }
    }

    private void OnIslandMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isExpanded)
        {
            ExpandDock();
        }
    }

    private void ExpandDock()
    {
        if (_isExpanded) return;
        _isExpanded = true;

        AnimateIsland(ExpandedWidth, ExpandedHeight);

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (s, e) =>
        {
            CompactView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
            ExpandedView.BeginAnimation(OpacityProperty, fadeIn);
        };
        CompactView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CollapseDock()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        AnimateIsland(CurrentCompactWidth, CompactHeight);

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (s, e) =>
        {
            ExpandedView.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
            CompactView.BeginAnimation(OpacityProperty, fadeIn);
        };
        ExpandedView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void AnimateIsland(double targetWidth, double targetHeight)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var widthAnim = new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = ease
        };

        var heightAnim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = ease
        };

        IslandContainer.BeginAnimation(WidthProperty, widthAnim);
        IslandContainer.BeginAnimation(HeightProperty, heightAnim);
    }

    private void OnTrackChanged(MediaTrackInfo track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track.HasMedia)
            {
                // Media active: Show compact media view
                CompactClockView.Visibility = Visibility.Collapsed;
                CompactMediaView.Visibility = Visibility.Visible;

                CompactTitleText.Text = string.IsNullOrWhiteSpace(track.Title) ? "Now Playing" : track.Title;
                CompactArtistText.Text = string.IsNullOrWhiteSpace(track.Artist) ? track.SourceApp : track.Artist;
                CompactProgressArt.Progress = track.ProgressFraction;
                CompactProgressArt.ImageSource = track.Thumbnail;
                CompactEqualizer.IsPlaying = track.IsPlaying;

                ExpandedTitleText.Text = string.IsNullOrWhiteSpace(track.Title) ? "Now Playing" : track.Title;
                ExpandedArtistText.Text = string.IsNullOrWhiteSpace(track.AlbumTitle)
                    ? track.Artist
                    : $"{track.Artist} • {track.AlbumTitle}";
                ExpandedSourceText.Text = string.IsNullOrWhiteSpace(track.SourceApp) ? "Media Player" : track.SourceApp;

                if (track.Thumbnail != null)
                {
                    ExpandedAlbumImage.Source = track.Thumbnail;
                    ExpandedAlbumImage.Visibility = Visibility.Visible;
                    ExpandedDefaultOrb.Visibility = Visibility.Collapsed;
                    ExpandedOrbSymbol.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ExpandedAlbumImage.Source = null;
                    ExpandedAlbumImage.Visibility = Visibility.Collapsed;
                    ExpandedDefaultOrb.Visibility = Visibility.Visible;
                    ExpandedOrbSymbol.Visibility = Visibility.Visible;
                }

                PositionText.Text = track.PositionString;
                DurationText.Text = track.DurationString;

                if (!_isDraggingSlider)
                {
                    TrackSlider.Maximum = Math.Max(1.0, track.Duration.TotalSeconds);
                    TrackSlider.Value = track.Position.TotalSeconds;
                }

                PlayPauseIcon.Symbol = track.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;

                if (!_isExpanded && Math.Abs(IslandContainer.Width - CompactMediaWidth) > 1.0)
                {
                    AnimateIsland(CompactMediaWidth, CompactHeight);
                }
            }
            else
            {
                // No media active: Default to Time & Date
                CompactMediaView.Visibility = Visibility.Collapsed;
                CompactClockView.Visibility = Visibility.Visible;

                ExpandedTitleText.Text = "No Media Playing";
                ExpandedArtistText.Text = "Play music or video on your system";
                ExpandedSourceText.Text = "Windows Live";
                ExpandedAlbumImage.Source = null;
                ExpandedAlbumImage.Visibility = Visibility.Collapsed;
                ExpandedDefaultOrb.Visibility = Visibility.Visible;
                ExpandedOrbSymbol.Visibility = Visibility.Visible;
                PlayPauseIcon.Symbol = SymbolRegular.Play24;
                CompactEqualizer.IsPlaying = false;

                if (!_isExpanded && Math.Abs(IslandContainer.Width - CompactClockWidth) > 1.0)
                {
                    AnimateIsland(CompactClockWidth, CompactHeight);
                }
            }

            UpdateControlThemeColors();
        });
    }

    private void OnVolumeChanged(float vol, bool isMuted)
    {
        Dispatcher.Invoke(() =>
        {
            if (!_isDraggingSlider)
            {
                VolumeSlider.Value = vol * 100.0;
            }

            if (isMuted || vol <= 0.01f)
            {
                VolumeIcon.Symbol = SymbolRegular.SpeakerMute24;
            }
            else if (vol < 0.35f)
            {
                VolumeIcon.Symbol = SymbolRegular.Speaker024;
            }
            else if (vol < 0.7f)
            {
                VolumeIcon.Symbol = SymbolRegular.Speaker124;
            }
            else
            {
                VolumeIcon.Symbol = SymbolRegular.Speaker224;
            }
        });
    }

    private void OnPowerStatusChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_powerService.HasBattery)
            {
                BatteryChip.Visibility = Visibility.Visible;
                BatteryText.Text = $"{_powerService.BatteryPercent}%";
                BatteryIcon.Symbol = _powerService.IsCharging ? SymbolRegular.BatteryCharge24 : SymbolRegular.Battery1024;
            }
            else
            {
                BatteryChip.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        string timeStr = now.ToString("h:mm tt");
        string dateStr = now.ToString("ddd, MMM d");

        TimeText.Text = timeStr;
        CompactTimeText.Text = timeStr;
        CompactDateText.Text = dateStr;
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaManager.TogglePlayPauseAsync();
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaManager.PreviousAsync();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await _mediaManager.NextAsync();
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        var isAccent = ShuffleButton.Foreground == _themeService.AccentBrush;
        ShuffleButton.Foreground = isAccent ? _themeService.TextSecondaryBrush : _themeService.AccentBrush;
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        var isAccent = RepeatButton.Foreground == _themeService.AccentBrush;
        RepeatButton.Foreground = isAccent ? _themeService.TextSecondaryBrush : _themeService.AccentBrush;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        PinIcon.Foreground = _isPinned ? _themeService.AccentBrush : _themeService.TextSecondaryBrush;
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false;
        PinIcon.Foreground = _themeService.TextSecondaryBrush;
        CollapseDock();
        if (_appStateTracker.CurrentState == ForegroundAppState.Maximized)
        {
            _isHoverRevealed = false;
            HideDockSmooth();
        }
    }

    private void StyleToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isNotchMode = !_isNotchMode;
        IslandNotch.IsNotchMode = _isNotchMode;
        IslandShadow.IsNotchMode = _isNotchMode;

        if (_isNotchMode)
        {
            Top = 0;
            IslandContainer.Margin = new Thickness(0, 0, 0, 0);
        }
        else
        {
            Top = 8;
            IslandContainer.Margin = new Thickness(0, 0, 0, 0);
        }
    }

    private void VolumeButton_Click(object sender, RoutedEventArgs e)
    {
        _volumeManager.ToggleMute();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            _volumeManager.SetVolume((float)(e.NewValue / 100.0));
        }
    }

    private void TrackSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
        _autoCollapseTimer.Stop();
    }

    private async void TrackSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        await _mediaManager.SeekAsync(TimeSpan.FromSeconds(TrackSlider.Value));
    }

    private void TrackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider)
        {
            var pos = TimeSpan.FromSeconds(e.NewValue);
            PositionText.Text = $"{(int)pos.TotalMinutes}:{pos.Seconds:D2}";
        }
    }
}
