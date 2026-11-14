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
    private readonly FocusTimerService _focusService;
    private readonly ClipboardWatcherService _clipboardService;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _autoCollapseTimer;

    private IslandCategory _activeCategory = IslandCategory.MediaPlayback;
    private bool _isExpanded = false;
    private bool _isPinned = false;
    private bool _isNotchMode = true;
    private bool _isDraggingSlider = false;

    private const double CompactClockWidth = 190.0;
    private const double CompactMediaWidth = 240.0;
    private const double CompactHeight = 38.0;
    private const double ExpandedWidth = 370.0;
    private const double ExpandedHeight = 68.0;

    private double CurrentCompactWidth => (_mediaManager?.CurrentTrack?.HasMedia ?? false) ? CompactMediaWidth : CompactClockWidth;

    public MainWindow()
    {
        App.Log("MainWindow constructor started");
        try
        {
            InitializeComponent();
            App.Log("MainWindow InitializeComponent finished");

            _themeService = new ThemeService();
            _themeService.ThemeChanged += OnThemeChanged;
            ApplyThemeResources();

            _appStateTracker = new AppStateTracker();
            _appStateTracker.StateChanged += OnForegroundAppStateChanged;

            _mediaManager = new MediaManager(Dispatcher);
            _volumeManager = new VolumeManager();
            _powerService = new PowerManagerService();
            _focusService = new FocusTimerService();
            _clipboardService = new ClipboardWatcherService();

            _mediaManager.TrackChanged += OnTrackChanged;
            _volumeManager.VolumeChanged += OnVolumeChanged;
            _powerService.PowerStatusChanged += OnPowerStatusChanged;
            _focusService.Tick += OnFocusTimerTick;
            _clipboardService.ClipboardTextChanged += OnClipboardTextChanged;

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
                        HideDockSmooth();
                    }
                }
            };

            SourceInitialized += (s, e) =>
            {
                App.Log("MainWindow SourceInitialized");
                WindowHelper.MakeToolWindow(this);
            };
            Loaded += OnLoaded;
            Closing += (s, e) => App.Log($"MainWindow Closing: Cancel={e.Cancel}");
            Closed += (s, e) => App.Log("MainWindow Closed");
            PositionWindow();

            IslandContainer.MouseEnter += OnIslandMouseEnter;
            IslandContainer.MouseLeave += OnIslandMouseLeave;
            IslandContainer.MouseDown += OnIslandMouseDown;
            App.Log("MainWindow constructor finished");
        }
        catch (Exception ex)
        {
            App.Log($"FATAL EXCEPTION in MainWindow constructor: {ex}");
            throw;
        }
    }

    private void OnForegroundAppStateChanged(ForegroundAppState state)
    {
        Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case ForegroundAppState.FullScreen:
                case ForegroundAppState.Maximized:
                    // Fullscreen or Maximized mode: disappear completely
                    TopEdgeHotspot.Visibility = Visibility.Collapsed;
                    HideDockSmooth();
                    break;

                case ForegroundAppState.Normal:
                    // Desktop / non-maximized apps: dynamic island visible at top
                    TopEdgeHotspot.Visibility = Visibility.Collapsed;
                    ShowDockSmooth();
                    break;
            }
        });
    }

    private void TopEdgeHotspot_MouseEnter(object sender, MouseEventArgs e)
    {
        // Hotspot disabled when maximized/fullscreen as dock must disappear completely
    }

    private void HideDockSmooth()
    {
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160));
        anim.Completed += (s, e) =>
        {
            if (_appStateTracker.CurrentState != ForegroundAppState.Normal)
            {
                IslandContainer.Visibility = Visibility.Collapsed;
            }
        };
        IslandContainer.BeginAnimation(OpacityProperty, anim);
    }

    private void ShowDockSmooth()
    {
        IslandContainer.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160));
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
        Application.Current.Resources["PlayButtonBackgroundBrush"] = _themeService.PlayButtonBackgroundBrush;
        Application.Current.Resources["PlayButtonForegroundBrush"] = _themeService.PlayButtonForegroundBrush;
        Application.Current.Resources["CircleButtonBackgroundBrush"] = _themeService.CircleButtonBackgroundBrush;

        if (!_themeService.IsDarkTheme)
        {
            IslandShadowEffect.Color = Color.FromArgb(255, 60, 60, 70);
            IslandShadowEffect.Opacity = 0.25;
            IslandShadowEffect.BlurRadius = 18;
            IslandShadow.Fill = new SolidColorBrush(Color.FromRgb(220, 220, 225));
        }
        else
        {
            IslandShadowEffect.Color = Colors.Black;
            IslandShadowEffect.Opacity = 0.55;
            IslandShadowEffect.BlurRadius = 22;
            IslandShadow.Fill = new SolidColorBrush(Colors.Black);
        }

        UpdateControlThemeColors();
    }

    private void UpdateControlThemeColors()
    {
        CompactEqualizer.AccentBrush = _themeService.AccentBrush;

        if (_themeService.IsDarkTheme)
        {
            PlayPauseButton.Background = new SolidColorBrush(Colors.White);
            PlayPauseIcon.Foreground = new SolidColorBrush(Colors.Black);
        }
        else
        {
            PlayPauseButton.Background = _themeService.AccentBrush;
            PlayPauseIcon.Foreground = _themeService.PlayButtonForegroundBrush;
        }

        PreviousButton.Foreground = _themeService.TextSecondaryBrush;
        NextButton.Foreground = _themeService.TextSecondaryBrush;
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
        WindowHelper.MakeToolWindow(this);
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

        // Initialize Brightness level
        try
        {
            int b = BrightnessManager.GetBrightness();
            HardwareBrightnessSlider.Value = b;
            HardwareBrightnessText.Text = $"{b}%";
        }
        catch { }

        App.Log("MainWindow OnLoaded finished");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WindowHelper.WM_NCHITTEST)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            Point windowPoint = PointFromScreen(new Point(x, y));

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

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) =>
        {
            CompactView.Visibility = Visibility.Collapsed;
            ExpandedContainer.Visibility = Visibility.Visible;
            ShowActiveCategoryView();
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120));
            ExpandedContainer.BeginAnimation(OpacityProperty, fadeIn);
        };
        CompactView.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CollapseDock()
    {
        if (!_isExpanded) return;
        _isExpanded = false;

        AnimateIsland(CurrentCompactWidth, CompactHeight);

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) =>
        {
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(120));
            CompactView.BeginAnimation(OpacityProperty, fadeIn);
        };
        ExpandedContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void AnimateIsland(double targetWidth, double targetHeight)
    {
        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };

        var widthAnim = new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = ease
        };

        var heightAnim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = ease
        };

        IslandContainer.BeginAnimation(WidthProperty, widthAnim);
        IslandContainer.BeginAnimation(HeightProperty, heightAnim);
    }

    public void SwitchCategory(IslandCategory cat)
    {
        _activeCategory = cat;
        ShowActiveCategoryView();
    }

    private void ShowActiveCategoryView()
    {
        ViewMediaPlayback.Visibility = _activeCategory == IslandCategory.MediaPlayback ? Visibility.Visible : Visibility.Collapsed;
        ViewDeviceBattery.Visibility = _activeCategory == IslandCategory.DeviceBattery ? Visibility.Visible : Visibility.Collapsed;
        ViewAppNotification.Visibility = _activeCategory == IslandCategory.AppNotification ? Visibility.Visible : Visibility.Collapsed;
        ViewFocusTimer.Visibility = _activeCategory == IslandCategory.FocusTimer ? Visibility.Visible : Visibility.Collapsed;
        ViewLiveActivities.Visibility = _activeCategory == IslandCategory.LiveActivities ? Visibility.Visible : Visibility.Collapsed;
        ViewHardwareLevels.Visibility = _activeCategory == IslandCategory.HardwareVolumeBrightness ? Visibility.Visible : Visibility.Collapsed;
        ViewLiveClipboard.Visibility = _activeCategory == IslandCategory.LiveClipboard ? Visibility.Visible : Visibility.Collapsed;
        ViewWindowSnapping.Visibility = _activeCategory == IslandCategory.WindowSnapping ? Visibility.Visible : Visibility.Collapsed;
        ViewTimerQuickSet.Visibility = _activeCategory == IslandCategory.TimerQuickSet ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTrackChanged(MediaTrackInfo track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track.HasMedia)
            {
                // Dynamic trigger: active media sets default expanded view to media playback
                _activeCategory = IslandCategory.MediaPlayback;

                CompactClockView.Visibility = Visibility.Collapsed;
                CompactMediaView.Visibility = Visibility.Visible;

                CompactEqualizer.IsPlaying = track.IsPlaying;

                ExpandedTitleText.Text = string.IsNullOrWhiteSpace(track.Title) ? "Now Playing" : track.Title;
                ExpandedArtistText.Text = string.IsNullOrWhiteSpace(track.Artist) ? "Audio" : track.Artist;

                if (track.Thumbnail != null)
                {
                    ExpandedAlbumImage.Source = track.Thumbnail;
                    ExpandedAlbumImage.Visibility = Visibility.Visible;
                    ExpandedDefaultOrb.Visibility = Visibility.Collapsed;
                    ExpandedOrbSymbol.Visibility = Visibility.Collapsed;

                    CompactAlbumImage.Source = track.Thumbnail;
                    CompactAlbumImage.Visibility = Visibility.Visible;
                }
                else
                {
                    ExpandedAlbumImage.Source = null;
                    ExpandedAlbumImage.Visibility = Visibility.Collapsed;
                    ExpandedDefaultOrb.Visibility = Visibility.Visible;
                    ExpandedOrbSymbol.Visibility = Visibility.Visible;

                    CompactAlbumImage.Source = null;
                    CompactAlbumImage.Visibility = Visibility.Collapsed;
                }

                PlayPauseIcon.Symbol = track.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;

                if (!_isExpanded && Math.Abs(IslandContainer.Width - CompactMediaWidth) > 1.0)
                {
                    AnimateIsland(CompactMediaWidth, CompactHeight);
                }
            }
            else
            {
                CompactMediaView.Visibility = Visibility.Collapsed;
                CompactClockView.Visibility = Visibility.Visible;

                ExpandedTitleText.Text = "No Media Playing";
                ExpandedArtistText.Text = "Play music or video on your system";
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
            int volPct = (int)Math.Round(vol * 100.0);
            HardwareVolumeBar.Value = volPct;
            HardwareVolumeText.Text = $"{volPct}%";
        });
    }

    private void OnPowerStatusChanged()
    {
        Dispatcher.Invoke(() =>
        {
            if (_powerService.HasBattery)
            {
                BatteryDeviceName.Text = $"PC Battery ({_powerService.BatteryPercent}%)";
            }
        });
    }

    private void OnFocusTimerTick()
    {
        Dispatcher.Invoke(() =>
        {
            FocusCountdownText.Text = $"{_focusService.FormattedTime} remaining";
            FocusToggleButton.Content = _focusService.IsRunning ? "Pause" : "Resume";
        });
    }

    private void OnClipboardTextChanged(string text)
    {
        Dispatcher.Invoke(() =>
        {
            // Dynamic trigger: new clipboard copy automatically brings up clipboard preview
            ClipboardPreviewText.Text = text;
            _activeCategory = IslandCategory.LiveClipboard;
            if (_isExpanded)
            {
                ShowActiveCategoryView();
            }
        });
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        string timeStr = now.ToString("h:mm tt");
        string dateStr = now.ToString("ddd, MMM d");

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

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = false;
        CollapseDock();
        if (_appStateTracker.CurrentState == ForegroundAppState.Maximized)
        {
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

    private void FocusToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _focusService.Toggle();
    }

    private void HardwareBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            int val = (int)e.NewValue;
            HardwareBrightnessText.Text = $"{val}%";
            BrightnessManager.SetBrightness(val);
        }
    }

    private void SnapLeft_Click(object sender, RoutedEventArgs e)
    {
        WindowSnappingService.SnapForegroundWindow(SnapPosition.LeftHalf);
    }

    private void SnapRight_Click(object sender, RoutedEventArgs e)
    {
        WindowSnappingService.SnapForegroundWindow(SnapPosition.RightHalf);
    }

    private void SnapTop_Click(object sender, RoutedEventArgs e)
    {
        WindowSnappingService.SnapForegroundWindow(SnapPosition.TopHalf);
    }

    private void SnapMax_Click(object sender, RoutedEventArgs e)
    {
        WindowSnappingService.SnapForegroundWindow(SnapPosition.Maximize);
    }

    private void StartFocusTimerPreset_Click(object sender, RoutedEventArgs e)
    {
        _focusService.Start(25);
        SwitchCategory(IslandCategory.FocusTimer);
    }

    private void Start15MinTimer_Click(object sender, RoutedEventArgs e)
    {
        _focusService.Start(5);
        SwitchCategory(IslandCategory.FocusTimer);
    }
}
