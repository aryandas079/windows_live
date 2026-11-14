using System;
using System.Diagnostics;
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
    private readonly NotificationWatcherService _notificationService;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _autoCollapseTimer;
    private readonly DispatcherTimer _chargingAlertTimer;
    private readonly DispatcherTimer _notificationAlertTimer;
    private readonly DispatcherTimer _unlockAlertTimer;
    private readonly DispatcherTimer _topMostTimer;

    private IslandCategory _activeCategory = IslandCategory.MediaPlayback;
    private bool _isExpanded = false;
    private bool _isPinned = false;
    private bool _isNotchMode = true;
    private bool _isDraggingSlider = false;

    private const double CompactDefaultWidth = 160.0;
    private const double CompactMediaWidth = 240.0;
    private const double CompactChargingWidth = 320.0;
    private const double CompactNotificationWidth = 220.0;
    private const double CompactUnlockWidth = 190.0;
    private const double CompactHeight = 36.0;
    private const double ExpandedWidth = 460.0;
    private const double ExpandedHeight = 86.0;
    private const double ExpandedNotificationReplyHeight = 138.0;

    private bool _isChargingAlertActive = false;
    private bool _isNotificationAlertActive = false;
    private bool _isUnlockAlertActive = false;
    private IslandNotification? _currentNotification = null;

    private bool HasActiveContent =>
        _isUnlockAlertActive ||
        (_mediaManager?.CurrentTrack?.HasMedia ?? false) ||
        _isChargingAlertActive ||
        _isNotificationAlertActive;

    private double CurrentCompactWidth
    {
        get
        {
            if (_isUnlockAlertActive) return CompactUnlockWidth;
            if (_isChargingAlertActive) return CompactChargingWidth;
            if (_isNotificationAlertActive && _currentNotification != null) return CompactNotificationWidth;
            if (_mediaManager?.CurrentTrack?.HasMedia ?? false) return CompactMediaWidth;
            return CompactDefaultWidth;
        }
    }

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
            _notificationService = new NotificationWatcherService();

            _mediaManager.TrackChanged += OnTrackChanged;
            _volumeManager.VolumeChanged += OnVolumeChanged;
            _powerService.PowerStatusChanged += OnPowerStatusChanged;
            _powerService.ChargingConnected += OnChargingConnected;
            _focusService.Tick += OnFocusTimerTick;
            _clipboardService.ClipboardTextChanged += OnClipboardTextChanged;
            _notificationService.NotificationReceived += OnNotificationReceived;
            _notificationService.NotificationsChanged += OnNotificationsChanged;

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();

            _notificationAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5.0)
            };
            _notificationAlertTimer.Tick += (s, e) =>
            {
                _notificationAlertTimer.Stop();

                // If user is actively typing in the reply box, give them time to finish
                if (NotificationReplyInput.IsFocused || !string.IsNullOrWhiteSpace(NotificationReplyInput.Text))
                {
                    return;
                }

                // Disappear after 5s if not responded/attended to
                _isNotificationAlertActive = false;
                if (_isExpanded && _activeCategory == IslandCategory.AppNotification)
                {
                    CollapseDock();
                }
                else
                {
                    UpdateCompactViewVisibility();
                }
            };

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

            _chargingAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5.0)
            };
            _chargingAlertTimer.Tick += (s, e) =>
            {
                _chargingAlertTimer.Stop();
                if (_isChargingAlertActive)
                {
                    _isChargingAlertActive = false;

                    // Restore shadow effect from charging glow
                    IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
                    IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
                    IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;

                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactChargingView.Visibility = Visibility.Collapsed;
                        UpdateCompactViewVisibility();
                    };
                    CompactChargingView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _unlockAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _unlockAlertTimer.Tick += (s, e) =>
            {
                _unlockAlertTimer.Stop();
                if (_isUnlockAlertActive)
                {
                    _isUnlockAlertActive = false;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactUnlockView.Visibility = Visibility.Collapsed;
                        UpdateCompactViewVisibility();
                    };
                    CompactUnlockView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            // Listen for Windows device unlock events
            Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

            // Aggressive Topmost Enforcement: keeps dock stubbornly above all other windows
            _topMostTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _topMostTimer.Tick += (s, e) =>
            {
                if (_appStateTracker.CurrentState != ForegroundAppState.Maximized &&
                    _appStateTracker.CurrentState != ForegroundAppState.FullScreen)
                {
                    WindowHelper.EnsureTopMost(this);
                }
            };
            _topMostTimer.Start();

            Deactivated += (s, e) =>
            {
                if (_appStateTracker.CurrentState != ForegroundAppState.Maximized &&
                    _appStateTracker.CurrentState != ForegroundAppState.FullScreen)
                {
                    WindowHelper.EnsureTopMost(this);
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
        IslandContainer.Width = CurrentCompactWidth;
        IslandContainer.Height = CompactHeight;
        _volumeManager.PollVolumeState(forceNotify: true);
        OnTrackChanged(_mediaManager.CurrentTrack);
        _ = _notificationService.StartAsync();

        // Initialize Brightness level
        try
        {
            int b = BrightnessManager.GetBrightness();
            HardwareBrightnessSlider.Value = b;
            HardwareBrightnessText.Text = $"{b}%";
        }
        catch { }

        // Trigger unlock animation on initial launch
        Dispatcher.BeginInvoke(new Action(() =>
        {
            TriggerUnlockAnimation();
        }), System.Windows.Threading.DispatcherPriority.Background);

        App.Log("MainWindow OnLoaded finished");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0046) // WM_WINDOWPOSCHANGING: keep topmost unconditionally unless maximized/fullscreen
        {
            if (_appStateTracker.CurrentState != ForegroundAppState.Maximized &&
                _appStateTracker.CurrentState != ForegroundAppState.FullScreen)
            {
                try
                {
                    var wp = System.Runtime.InteropServices.Marshal.PtrToStructure<WindowHelper.WINDOWPOS>(lParam);
                    wp.hwndInsertAfter = (IntPtr)(-1); // HWND_TOPMOST
                    System.Runtime.InteropServices.Marshal.StructureToPtr(wp, lParam, false);
                }
                catch { }
            }
        }

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

        if (msg == 0x0021) // WM_MOUSEACTIVATE
        {
            // If the user clicks on the notification reply input or send button, activate the window so they can type immediately
            if (_isExpanded && _activeCategory == IslandCategory.AppNotification && NotificationReplyRow.Visibility == Visibility.Visible)
            {
                WindowHelper.SetActivationEnabled(this, true);
                handled = true;
                return (IntPtr)1; // MA_ACTIVATE
            }
        }

        return IntPtr.Zero;
    }

    private void OnIslandMouseEnter(object sender, MouseEventArgs e)
    {
        _autoCollapseTimer.Stop();
        IslandContainer.Cursor = HasActiveContent ? Cursors.Hand : Cursors.Arrow;
        if (!_isExpanded && HasActiveContent)
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
        if (e.LeftButton == MouseButtonState.Pressed && !_isExpanded && HasActiveContent)
        {
            ExpandDock();
        }
    }

    private void ExpandDock()
    {
        if (_isExpanded) return;
        if (!HasActiveContent && !_isNotificationAlertActive && !_notificationService.HasNotifications) return;
        _isExpanded = true;

        if (_isNotificationAlertActive || _activeCategory == IslandCategory.AppNotification)
        {
            _activeCategory = IslandCategory.AppNotification;
        }
        else if (_notificationService.HasNotifications && !(_mediaManager?.CurrentTrack?.HasMedia ?? false))
        {
            _activeCategory = IslandCategory.AppNotification;
        }
        else if (_mediaManager?.CurrentTrack?.HasMedia ?? false)
        {
            _activeCategory = IslandCategory.MediaPlayback;
        }
        else if (_focusService.IsRunning)
        {
            _activeCategory = IslandCategory.FocusTimer;
        }

        double targetHeight = (_activeCategory == IslandCategory.AppNotification && (_currentNotification?.IsMessagingApp ?? false))
            ? ExpandedNotificationReplyHeight
            : ExpandedHeight;

        AnimateIsland(ExpandedWidth, targetHeight);

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

        // Restore non-activating window mode when collapsing
        WindowHelper.SetActivationEnabled(this, false);

        AnimateIsland(CurrentCompactWidth, CompactHeight);

        // Restore shadow effect in case charging green pulse was active
        IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
        IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
        IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(100));
        fadeOut.Completed += (s, e) =>
        {
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            UpdateCompactViewVisibility();
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

        if (_isExpanded)
        {
            double targetHeight = (_activeCategory == IslandCategory.AppNotification && (_currentNotification?.IsMessagingApp ?? false))
                ? ExpandedNotificationReplyHeight
                : ExpandedHeight;
            AnimateIsland(ExpandedWidth, targetHeight);
        }
    }

    private void OnTrackChanged(MediaTrackInfo track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track.HasMedia)
            {
                if (!_isNotificationAlertActive && _activeCategory != IslandCategory.AppNotification)
                {
                    _activeCategory = IslandCategory.MediaPlayback;
                }

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
                    CompactDefaultOrb.Visibility = Visibility.Collapsed;
                    CompactOrbSymbol.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ExpandedAlbumImage.Source = null;
                    ExpandedAlbumImage.Visibility = Visibility.Collapsed;
                    ExpandedDefaultOrb.Visibility = Visibility.Visible;
                    ExpandedOrbSymbol.Visibility = Visibility.Visible;

                    CompactAlbumImage.Source = null;
                    CompactAlbumImage.Visibility = Visibility.Collapsed;
                    CompactDefaultOrb.Visibility = Visibility.Visible;
                    CompactOrbSymbol.Visibility = Visibility.Visible;
                }

                PlayPauseIcon.Symbol = track.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;

                UpdateCompactViewVisibility();
            }
            else
            {
                if (_isExpanded && _activeCategory == IslandCategory.MediaPlayback)
                {
                    CollapseDock();
                }

                ExpandedTitleText.Text = "No Media Playing";
                ExpandedArtistText.Text = "Play music or video on your system";
                ExpandedAlbumImage.Source = null;
                ExpandedAlbumImage.Visibility = Visibility.Collapsed;
                ExpandedDefaultOrb.Visibility = Visibility.Visible;
                ExpandedOrbSymbol.Visibility = Visibility.Visible;
                PlayPauseIcon.Symbol = SymbolRegular.Play24;
                CompactEqualizer.IsPlaying = false;

                UpdateCompactViewVisibility();
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

    private void OnChargingConnected()
    {
        Dispatcher.Invoke(() =>
        {
            ShowChargingAnimation();
        });
    }

    public void ShowChargingAnimation()
    {
        _isChargingAlertActive = true;
        _chargingAlertTimer.Stop();

        int percent = Math.Clamp(_powerService.BatteryPercent, 1, 100);
        CompactChargingText.Text = $"{percent}%";

        // Battery fill calculation (inner capsule usable width is 31px)
        double targetFillWidth = Math.Max(3.0, (percent / 100.0) * 31.0);

        // Hide other compact views and activate charging view matching Image
        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactChargingView.Opacity = 0.0;
        CompactChargingView.Visibility = Visibility.Visible;

        // If currently expanded, collapse into compact charging notch
        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        // Animate island container width to 320.0 (compact height 36.0)
        AnimateIsland(CompactChargingWidth, CompactHeight);

        // Gentle emerald ambient glow around notch while charging alert displays
        IslandShadowEffect.Color = Color.FromRgb(48, 209, 88);
        IslandShadowEffect.Opacity = 0.40;
        IslandShadowEffect.BlurRadius = 24;

        // Smooth fade-in of the charging view
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactChargingView.BeginAnimation(OpacityProperty, fadeIn);

        // Smooth battery fill animation from 0 to targetFillWidth
        var fillAnim = new DoubleAnimation(0.0, targetFillWidth, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
        };
        CompactChargingBatteryFill.BeginAnimation(WidthProperty, fillAnim);

        // Display for exactly 5.0 seconds then smoothly disappear
        _chargingAlertTimer.Start();
    }

    private void CompactChargingPill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowChargingAnimation();
    }

    private void SystemEvents_SessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
        {
            Dispatcher.Invoke(() =>
            {
                TriggerUnlockAnimation();
            });
        }
    }

    public void TriggerUnlockAnimation()
    {
        _isUnlockAlertActive = true;
        _unlockAlertTimer.Stop();

        // If currently expanded, collapse into compact notch
        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        // Hide other compact views and activate unlock view
        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactChargingView.Visibility = Visibility.Collapsed;
        CompactUnlockView.Opacity = 0.0;
        CompactUnlockView.Visibility = Visibility.Visible;

        // Reset shackle transforms to closed/locked state (Image 1)
        LockShackleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        LockShackleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        LockShackleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LockShackleTranslate.X = 0;
        LockShackleTranslate.Y = 0;
        LockShackleRotate.Angle = 0;

        // Animate island container width to 190.0 (compact height 36.0)
        AnimateIsland(CompactUnlockWidth, CompactHeight);

        // Smooth fade-in of the unlock view in fully locked state
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactUnlockView.BeginAnimation(OpacityProperty, fadeIn);

        // 1. Shackle lifts straight up out of the padlock body (unlatching from zero)
        var liftAnim = new DoubleAnimation
        {
            From = 0.0,
            To = -4.0,
            Duration = TimeSpan.FromMilliseconds(240),
            BeginTime = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // 2. Shackle swings/rotates cleanly outward to the right into the open unlocked position (Image 2)
        var rotateAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 38.0,
            Duration = TimeSpan.FromMilliseconds(320),
            BeginTime = TimeSpan.FromMilliseconds(320),
            EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
        };

        // 3. Play crisp mechanical unlock click sound strictly through device speakers synchronized with the shackle pop
        var soundTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        soundTimer.Tick += (s, e) =>
        {
            soundTimer.Stop();
            PlayUnlockSound();
        };
        soundTimer.Start();

        LockShackleTranslate.BeginAnimation(TranslateTransform.YProperty, liftAnim);
        LockShackleRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

        // Stay on screen for 3.0 seconds, then return to normal
        _unlockAlertTimer.Start();
    }

    private static void PlayUnlockSound()
    {
        try
        {
            string soundPath = GetOrGenerateUnlockSound();
            if (!string.IsNullOrEmpty(soundPath) && System.IO.File.Exists(soundPath))
            {
                // Play exclusively through built-in device speakers (bypassing headphones/AirPods/Bluetooth)
                SpeakerAudioService.PlayUnlockSoundOnSpeakers(soundPath);
            }
        }
        catch (Exception ex)
        {
            App.Log($"PlayUnlockSound error: {ex.Message}");
        }
    }

    private static string GetOrGenerateUnlockSound()
    {
        try
        {
            string wavPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unlock_click.wav");
            if (!System.IO.File.Exists(wavPath))
            {
                int sampleRate = 44100;
                double duration = 0.07;
                int sampleCount = (int)(sampleRate * duration);
                byte[] wavData = new byte[44 + sampleCount * 2];

                System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wavData, 0);
                BitConverter.GetBytes(wavData.Length - 8).CopyTo(wavData, 4);
                System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wavData, 8);
                BitConverter.GetBytes(16).CopyTo(wavData, 16);
                BitConverter.GetBytes((short)1).CopyTo(wavData, 20);
                BitConverter.GetBytes((short)1).CopyTo(wavData, 22);
                BitConverter.GetBytes(sampleRate).CopyTo(wavData, 24);
                BitConverter.GetBytes(sampleRate * 2).CopyTo(wavData, 28);
                BitConverter.GetBytes((short)2).CopyTo(wavData, 32);
                BitConverter.GetBytes((short)16).CopyTo(wavData, 34);
                System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wavData, 36);
                BitConverter.GetBytes(sampleCount * 2).CopyTo(wavData, 40);

                for (int i = 0; i < sampleCount; i++)
                {
                    double t = (double)i / sampleRate;
                    double val = 0.0;
                    if (t < 0.012)
                    {
                        double env1 = Math.Exp(-t * 700.0);
                        val += env1 * Math.Sin(2.0 * Math.PI * 2600.0 * t) * 0.40;
                    }
                    if (t >= 0.014 && t < 0.065)
                    {
                        double t2 = t - 0.014;
                        double env2 = Math.Exp(-t2 * 200.0);
                        val += env2 * (Math.Sin(2.0 * Math.PI * 1800.0 * t2) * 0.70 + Math.Sin(2.0 * Math.PI * 3400.0 * t2) * 0.35);
                    }
                    short s16 = (short)Math.Clamp(val * 28000.0, -32767.0, 32767.0);
                    BitConverter.GetBytes(s16).CopyTo(wavData, 44 + i * 2);
                }

                System.IO.File.WriteAllBytes(wavPath, wavData);
            }
            return wavPath;
        }
        catch
        {
            string nav = @"C:\Windows\Media\Windows Navigation Start.wav";
            if (System.IO.File.Exists(nav)) return nav;
            return @"C:\Windows\Media\Windows Unlock.wav";
        }
    }

    private void OnPowerStatusChanged()
    {
        Dispatcher.Invoke(() =>
        {
            CompactBatteryPercentText.Text = $"{_powerService.BatteryPercent}%";
            CompactChargingPill.Visibility = _powerService.IsCharging ? Visibility.Visible : Visibility.Collapsed;

            if (_powerService.HasBattery)
            {
                BatteryDeviceName.Text = $"PC Battery ({_powerService.BatteryPercent}%)";
            }

            if (_isChargingAlertActive)
            {
                CompactChargingText.Text = $"{_powerService.BatteryPercent}%";
                double fillWidth = Math.Max(3.0, (_powerService.BatteryPercent / 100.0) * 31.0);
                CompactChargingBatteryFill.Width = fillWidth;
            }
            else if (!_isExpanded && !(_mediaManager?.CurrentTrack?.HasMedia ?? false))
            {
                AnimateIsland(CurrentCompactWidth, CompactHeight);
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

    private void DismissNotifButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_currentNotification != null)
        {
            _notificationService.Dismiss(_currentNotification.Id);
        }
    }

    private void CompactNotificationView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _activeCategory = IslandCategory.AppNotification;
        _isNotificationAlertActive = true;
        if (_currentNotification != null)
        {
            UpdateNotificationUI(_currentNotification);
        }
        ExpandDock();

        // After clicking/opening notification dock, if no further action or reply within 5s, auto-dismiss and collapse
        _notificationAlertTimer.Stop();
        _notificationAlertTimer.Interval = TimeSpan.FromSeconds(5.0);
        _notificationAlertTimer.Start();
    }

    private void OnNotificationReceived(IslandNotification notif)
    {
        // Don't hijack with notifications from media apps when media track updates (handled natively by media controls)
        if (notif.AppType == KnownNotificationApp.Spotify ||
            notif.AppType == KnownNotificationApp.AppleMusic ||
            notif.AppName.Contains("Apple Music", StringComparison.OrdinalIgnoreCase) ||
            notif.AppName.Contains("Spotify", StringComparison.OrdinalIgnoreCase) ||
            notif.AppName.Contains("Windows Media Player", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        App.Log($"Processing notification in MainWindow: App={notif.AppName}, Title={notif.Title}, Message={notif.Message}");

        Dispatcher.Invoke(() =>
        {
            _currentNotification = notif;
            _isNotificationAlertActive = true;
            UpdateNotificationUI(notif);
            _activeCategory = IslandCategory.AppNotification;

            // Clear any active unlock alert
            if (_isUnlockAlertActive)
            {
                _isUnlockAlertActive = false;
                _unlockAlertTimer.Stop();
                CompactUnlockView.Visibility = Visibility.Collapsed;
            }

            // Expand to show notification banner
            if (_isExpanded)
            {
                ShowActiveCategoryView();
            }
            else
            {
                ExpandDock();
            }

            _notificationAlertTimer.Stop();
            _notificationAlertTimer.Interval = TimeSpan.FromSeconds(5.0);
            _notificationAlertTimer.Start();
        });
    }

    private void OnNotificationsChanged()
    {
        Dispatcher.Invoke(() =>
        {
            _currentNotification = _notificationService.LatestNotification;

            if (_notificationService.HasNotifications)
            {
                if (_currentNotification != null)
                {
                    UpdateNotificationUI(_currentNotification);
                }
            }
            else
            {
                _isNotificationAlertActive = false;
                _notificationAlertTimer.Stop();

                if (_isExpanded && _activeCategory == IslandCategory.AppNotification)
                {
                    CollapseDock();
                }
            }

            UpdateCompactViewVisibility();
        });
    }

    private void UpdateCompactViewVisibility()
    {
        if (_isUnlockAlertActive)
        {
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Visible;
        }
        else if (_isChargingAlertActive)
        {
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Visible;
        }
        else if (_isNotificationAlertActive && _currentNotification != null)
        {
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Visible;
        }
        else if (_mediaManager?.CurrentTrack?.HasMedia ?? false)
        {
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Visible;
        }
        else
        {
            // Empty blank notch: by default nothing is shown
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
        }

        if (!_isExpanded)
        {
            AnimateIsland(CurrentCompactWidth, CompactHeight);
        }
    }

    private void UpdateNotificationUI(IslandNotification notif)
    {
        NotificationTitleText.Text = notif.DisplayTitle;
        NotificationSubtext.Text = string.IsNullOrWhiteSpace(notif.Message) ? "Notification" : notif.Message;
        NotificationTimeText.Text = "now";
        CompactNotifCountText.Text = Math.Max(1, _notificationService.Count).ToString();

        CompactNotifIcon.SetNotification(notif);
        ExpandedNotifIcon.SetNotification(notif);

        if (notif.IsMessagingApp)
        {
            NotificationReplyRow.Visibility = Visibility.Visible;
            NotificationReplyInput.Text = "";
            NotificationReplyPlaceholder.Text = $"Reply to {notif.DisplayTitle}...";
            NotificationReplyPlaceholder.Visibility = Visibility.Visible;

            if (notif.AppType == KnownNotificationApp.WhatsApp)
            {
                NotificationSendButton.Background = new SolidColorBrush(Color.FromRgb(37, 211, 102));
            }
            else if (notif.AppType == KnownNotificationApp.Telegram)
            {
                NotificationSendButton.Background = new SolidColorBrush(Color.FromRgb(36, 161, 222));
            }
            else if (notif.AppType == KnownNotificationApp.Discord)
            {
                NotificationSendButton.Background = new SolidColorBrush(Color.FromRgb(88, 101, 242));
            }
            else if (notif.AppType == KnownNotificationApp.Facebook)
            {
                NotificationSendButton.Background = new SolidColorBrush(Color.FromRgb(24, 119, 242));
            }
            else
            {
                NotificationSendButton.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));
            }
        }
        else
        {
            NotificationReplyRow.Visibility = Visibility.Collapsed;
        }
    }

    private void NotificationReplyBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        WindowHelper.SetActivationEnabled(this, true);
        Activate();
        NotificationReplyInput.Focus();
        Keyboard.Focus(NotificationReplyInput);
    }

    private void NotificationReplyInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        WindowHelper.SetActivationEnabled(this, true);
        Activate();
        NotificationReplyInput.Focus();
        Keyboard.Focus(NotificationReplyInput);
    }

    private void NotificationReplyInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SendNotificationReply();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _isNotificationAlertActive = false;
            CollapseDock();
        }
    }

    private void NotificationReplyInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        NotificationReplyPlaceholder.Visibility = string.IsNullOrEmpty(NotificationReplyInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(NotificationReplyInput.Text))
        {
            _notificationAlertTimer.Stop();
        }
    }

    private void NotificationReplyInput_GotFocus(object sender, RoutedEventArgs e)
    {
        _notificationAlertTimer.Stop();
    }

    private void NotificationReplyInput_LostFocus(object sender, RoutedEventArgs e)
    {
        // When user leaves the input, restore WS_EX_NOACTIVATE unless still expanded with text
        if (string.IsNullOrEmpty(NotificationReplyInput.Text))
        {
            WindowHelper.SetActivationEnabled(this, false);
        }

        if (string.IsNullOrEmpty(NotificationReplyInput.Text) && _isNotificationAlertActive)
        {
            _notificationAlertTimer.Stop();
            _notificationAlertTimer.Interval = TimeSpan.FromSeconds(3.0);
            _notificationAlertTimer.Start();
        }
    }

    private void NotificationSendButton_Click(object sender, RoutedEventArgs e)
    {
        SendNotificationReply();
    }

    private void SendNotificationReply()
    {
        string text = NotificationReplyInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        App.Log($"Sending reply: '{text}' for notification App={_currentNotification?.AppName}");

        if (_currentNotification != null)
        {
            try
            {
                // Always put text on clipboard for instant paste convenience
                try { Clipboard.SetText(text); } catch { }

                if (_currentNotification.AppType == KnownNotificationApp.WhatsApp)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo($"whatsapp://send?text={Uri.EscapeDataString(text)}") { UseShellExecute = true });
                    }
                    catch
                    {
                        // Fallback to web whatsapp if protocol is not registered
                        Process.Start(new ProcessStartInfo($"https://web.whatsapp.com/send?text={Uri.EscapeDataString(text)}") { UseShellExecute = true });
                    }
                }
                else if (_currentNotification.AppType == KnownNotificationApp.Telegram)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo($"tg://msg_url?text={Uri.EscapeDataString(text)}") { UseShellExecute = true });
                    }
                    catch
                    {
                        Process.Start(new ProcessStartInfo($"https://t.me/share/url?text={Uri.EscapeDataString(text)}") { UseShellExecute = true });
                    }
                }
                else
                {
                    Clipboard.SetText(text);
                }

                _notificationService.Dismiss(_currentNotification.Id);
            }
            catch (Exception ex)
            {
                App.Log($"SendNotificationReply exception: {ex.Message}");
            }
        }

        PlayUnlockSound();

        NotificationReplyInput.Text = "";
        _isNotificationAlertActive = false;
        _notificationAlertTimer.Stop();
        CollapseDock();
    }

    private void Start15MinTimer_Click(object sender, RoutedEventArgs e)
    {
        _focusService.Start(5);
        SwitchCategory(IslandCategory.FocusTimer);
    }
}
