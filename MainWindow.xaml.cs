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
    private const double CompactChargingWidth = 260.0;
    private const double CompactNotificationWidth = 220.0;
    private const double CompactUnlockWidth = 190.0;
    private const double CompactSilentRingWidth = 200.0;
    private const double CompactDndWidth = 240.0;
    private const double CompactNightModeWidth = 240.0;
    private const double CompactAlarmWidth = 250.0;
    private const double CompactBluetoothWidth = 230.0;
    private const double ExpandedBluetoothWidth = 450.0;
    private const double ExpandedBluetoothHeight = 68.0;
    private const double CompactPowerEfficiencyWidth = 250.0;
    private const double CompactPowerBalancedWidth = 200.0;
    private const double CompactPowerPerformanceWidth = 235.0;
    private const double CompactTimerWidth = 240.0;
    private const double CompactRecordingWidth = 230.0;
    private const double CompactScreenRecordingWidth = 240.0;
    private const double CompactScreenRecordingHeight = 48.0;
    private const double CompactSplitLeadingWidth = 126.0;
    private const double DetachedBubbleWidth = 52.0;
    private const double DetachedBubbleGap = 12.0;
    private const double CompactHeight = 36.0;
    private const double ExpandedWidth = 460.0;
    private const double ExpandedHeight = 86.0;

    private bool _isChargingAlertActive = false;
    private bool _isNotificationAlertActive = false;
    private bool _isUnlockAlertActive = false;
    private bool _isSilentRingAlertActive = false;
    private bool _isDndAlertActive = false;
    private bool _isNightModeAlertActive = false;
    private bool _isAlarmActive = false;
    private bool _isBluetoothAlertActive = false;
    private BluetoothDeviceInfo? _activeBluetoothDevice = null;
    private bool _isPowerModeAlertActive = false;
    private WindowsPowerMode _activePowerMode = WindowsPowerMode.Balanced;
    private IslandNotification? _currentNotification = null;

    private readonly DispatcherTimer _silentRingAlertTimer;
    private readonly DispatcherTimer _dndAlertTimer;
    private readonly DispatcherTimer _nightModeAlertTimer;
    private readonly DispatcherTimer _bluetoothAlertTimer;
    private readonly DispatcherTimer _bluetoothHoverCollapseTimer;
    private readonly DispatcherTimer _powerModeAlertTimer;
    private readonly FocusAssistWatcherService _focusAssistService;
    private readonly NightLightWatcherService _nightLightService;
    private readonly AlarmService _alarmService;
    private readonly BluetoothWatcherService _bluetoothService;
    private readonly PowerModeWatcherService _powerModeService;
    private readonly PrivacyIndicatorService _privacyService;
    private readonly WindowsClockWatcherService _windowsClockService;
    private readonly VoiceRecordingService _voiceRecordingService;
    private readonly ScreenRecordingService _screenRecordingService;
    private bool _isPrivacyIndicatorActive = false;
    private bool _lastMuteState = false;
    private bool _muteStateInitialized = false;

    private bool IsVoiceRecordingActive => _voiceRecordingService?.IsRecording ?? false;
    private bool IsScreenRecordingActive => _screenRecordingService?.IsRecording ?? false;

    private bool IsAnyTimerActive =>
        _focusService.IsRunning ||
        _windowsClockService.HasActiveTimer;

    // A song stays active in the dock while playing OR paused until the media app or browser tab is closed
    private bool IsMusicActive =>
        _mediaManager?.CurrentTrack != null &&
        _mediaManager.CurrentTrack.HasMedia &&
        !string.IsNullOrWhiteSpace(_mediaManager.CurrentTrack.Title) &&
        !_mediaManager.CurrentTrack.Title.Equals("No Media Playing", StringComparison.OrdinalIgnoreCase) &&
        !_mediaManager.CurrentTrack.Title.Equals("Connecting…", StringComparison.OrdinalIgnoreCase);

    private bool IsMusicPlaying =>
        _mediaManager?.CurrentTrack?.IsPlaying ?? false;

    private bool IsSplitActive =>
        !_isExpanded &&
        !_isAlarmActive &&
        !_isBluetoothAlertActive &&
        !_isPowerModeAlertActive &&
        !_isUnlockAlertActive &&
        !_isSilentRingAlertActive &&
        !_isDndAlertActive &&
        !_isNightModeAlertActive &&
        !_isChargingAlertActive &&
        !_isNotificationAlertActive &&
        !IsScreenRecordingActive &&
        !IsVoiceRecordingActive &&
        IsAnyTimerActive &&
        IsMusicActive;

    private bool HasActiveContent =>
        _isAlarmActive ||
        _isBluetoothAlertActive ||
        _isPowerModeAlertActive ||
        _isUnlockAlertActive ||
        _isSilentRingAlertActive ||
        _isDndAlertActive ||
        _isNightModeAlertActive ||
        IsScreenRecordingActive ||
        IsVoiceRecordingActive ||
        IsAnyTimerActive ||
        IsMusicActive ||
        _isChargingAlertActive ||
        _isNotificationAlertActive;

    private bool HasActiveAlert =>
        _isPowerModeAlertActive ||
        _isNotificationAlertActive ||
        _isBluetoothAlertActive ||
        _isAlarmActive ||
        _isUnlockAlertActive ||
        _isSilentRingAlertActive ||
        _isDndAlertActive ||
        _isNightModeAlertActive ||
        _isChargingAlertActive;

    private bool _isCameraActive = false;
    private bool _isMicrophoneActive = false;

    private double PrivacyIndicatorExtraWidth
    {
        get
        {
            if (!_isPrivacyIndicatorActive) return 0.0;
            if (_isCameraActive && _isMicrophoneActive) return 30.0;
            return 18.0;
        }
    }

    private double CurrentCompactHeight => IsScreenRecordingActive ? CompactScreenRecordingHeight : CompactHeight;

    private double CurrentCompactWidth
    {
        get
        {
            double baseWidth;
            if (_isAlarmActive) baseWidth = CompactAlarmWidth;
            else if (_isBluetoothAlertActive) baseWidth = CompactBluetoothWidth;
            else if (_isPowerModeAlertActive)
            {
                baseWidth = _activePowerMode switch
                {
                    WindowsPowerMode.BestPowerEfficiency => CompactPowerEfficiencyWidth,
                    WindowsPowerMode.BestPerformance => CompactPowerPerformanceWidth,
                    _ => CompactPowerBalancedWidth
                };
            }
            else if (_isUnlockAlertActive) baseWidth = CompactUnlockWidth;
            else if (_isSilentRingAlertActive) baseWidth = CompactSilentRingWidth;
            else if (_isDndAlertActive) baseWidth = CompactDndWidth;
            else if (_isNightModeAlertActive) baseWidth = CompactNightModeWidth;
            else if (_isChargingAlertActive) baseWidth = CompactChargingWidth;
            else if (_isNotificationAlertActive && _currentNotification != null) baseWidth = CompactNotificationWidth;
            else if (IsScreenRecordingActive) baseWidth = CompactScreenRecordingWidth;
            else if (IsVoiceRecordingActive) baseWidth = CompactRecordingWidth;
            else if (IsAnyTimerActive && IsMusicActive) baseWidth = CompactSplitLeadingWidth;
            else if (IsAnyTimerActive) baseWidth = CompactTimerWidth;
            else if (IsMusicActive) baseWidth = CompactMediaWidth;
            else if (_isPrivacyIndicatorActive) baseWidth = CompactDefaultWidth + 10.0;
            else baseWidth = CompactDefaultWidth;

            return baseWidth + PrivacyIndicatorExtraWidth;
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
            _focusAssistService = new FocusAssistWatcherService();
            _nightLightService = new NightLightWatcherService();
            _alarmService = new AlarmService();
            _bluetoothService = new BluetoothWatcherService();
            _windowsClockService = new WindowsClockWatcherService();
            _voiceRecordingService = new VoiceRecordingService();
            _screenRecordingService = new ScreenRecordingService();

            _mediaManager.TrackChanged += OnTrackChanged;
            _volumeManager.VolumeChanged += OnVolumeChanged;
            _powerService.PowerStatusChanged += OnPowerStatusChanged;
            _powerService.ChargingConnected += OnChargingConnected;
            _powerService.BatteryAlert += OnBatteryAlert;
            _focusService.Tick += OnFocusTimerTick;
            _clipboardService.ClipboardTextChanged += OnClipboardTextChanged;
            _notificationService.NotificationReceived += OnNotificationReceived;
            _notificationService.NotificationsChanged += OnNotificationsChanged;
            _focusAssistService.FocusAssistChanged += OnFocusAssistChanged;
            _focusAssistService.Start();
            _nightLightService.NightModeChanged += OnNightModeChanged;
            _nightLightService.Start();
            _alarmService.AlarmStateChanged += OnAlarmStateChanged;
            _alarmService.SnoozeStarted += OnAlarmSnoozeStarted;
            _bluetoothService.DeviceConnected += OnBluetoothDeviceConnected;
            _bluetoothService.DeviceDisconnected += OnBluetoothDeviceDisconnected;
            _bluetoothService.Start();
            SetupBluetoothTestTriggerWatcher();
            _powerModeService = new PowerModeWatcherService();
            _powerModeService.PowerModeChanged += OnPowerModeChanged;
            _powerModeService.Start();
            _privacyService = new PrivacyIndicatorService();
            _privacyService.PrivacyStateChanged += OnPrivacyStateChanged;
            _privacyService.Start();
            _windowsClockService.TimerStateChanged += OnWindowsClockTimerChanged;
            _windowsClockService.Start();
            _voiceRecordingService.RecordingStateChanged += OnVoiceRecordingStateChanged;
            _voiceRecordingService.RecordingStateChanged += info => _privacyService.SetWasapiMicState(info.IsRecording);
            _voiceRecordingService.Start();
            _screenRecordingService.RecordingStateChanged += OnScreenRecordingStateChanged;
            _screenRecordingService.Start();

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

                // Disappear after exactly 5s once shown
                _isNotificationAlertActive = false;
                if (_currentNotification != null)
                {
                    _notificationService.Dismiss(_currentNotification.Id);
                    _currentNotification = null;
                }

                // If currently showing notification category, switch active category to whatever background task is live
                if (_activeCategory == IslandCategory.AppNotification)
                {
                    if (IsScreenRecordingActive) _activeCategory = IslandCategory.ScreenRecording;
                    else if (IsVoiceRecordingActive) _activeCategory = IslandCategory.VoiceRecording;
                    else if (IsAnyTimerActive) _activeCategory = IslandCategory.FocusTimer;
                    else if (IsMusicActive) _activeCategory = IslandCategory.MediaPlayback;
                    else _activeCategory = IslandCategory.MediaPlayback;
                }

                if (_isExpanded)
                {
                    CollapseDock();
                }
                else
                {
                    UpdateCompactViewVisibility();
                }

                if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                {
                    HideDockSmooth();
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
                    if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert)
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
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
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
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
                    };
                    CompactUnlockView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _silentRingAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _silentRingAlertTimer.Tick += (s, e) =>
            {
                _silentRingAlertTimer.Stop();
                if (_isSilentRingAlertActive)
                {
                    _isSilentRingAlertActive = false;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactSilentRingView.Visibility = Visibility.Collapsed;
                        UpdateCompactViewVisibility();
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
                    };
                    CompactSilentRingView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _dndAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _dndAlertTimer.Tick += (s, e) =>
            {
                _dndAlertTimer.Stop();
                if (_isDndAlertActive)
                {
                    _isDndAlertActive = false;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactDndView.Visibility = Visibility.Collapsed;
                        UpdateCompactViewVisibility();
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
                    };
                    CompactDndView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _nightModeAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4.0)
            };
            _nightModeAlertTimer.Tick += (s, e) =>
            {
                _nightModeAlertTimer.Stop();
                if (_isNightModeAlertActive)
                {
                    _isNightModeAlertActive = false;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactNightModeView.Visibility = Visibility.Collapsed;
                        UpdateCompactViewVisibility();
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
                    };
                    CompactNightModeView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _bluetoothAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5.0)
            };
            _bluetoothAlertTimer.Tick += (s, e) =>
            {
                _bluetoothAlertTimer.Stop();
                if (_isBluetoothAlertActive && !_isExpanded)
                {
                    _isBluetoothAlertActive = false;
                    _activeBluetoothDevice = null;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactBluetoothView.Visibility = Visibility.Collapsed;
                        UpdateCompactViewVisibility();
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
                    };
                    CompactBluetoothView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _bluetoothHoverCollapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5)
            };
            _bluetoothHoverCollapseTimer.Tick += (s, e) =>
            {
                _bluetoothHoverCollapseTimer.Stop();
                if (_isBluetoothAlertActive && _isExpanded && _activeCategory == IslandCategory.BluetoothDevice)
                {
                    _isBluetoothAlertActive = false;
                    _activeBluetoothDevice = null;
                    CollapseDock();
                }
            };

            _powerModeAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _powerModeAlertTimer.Tick += (s, e) =>
            {
                _powerModeAlertTimer.Stop();
                if (_isPowerModeAlertActive)
                {
                    _isPowerModeAlertActive = false;
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        CompactPowerModeView.Visibility = Visibility.Collapsed;
                        // Restore island shadow
                        IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
                        IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
                        IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;
                        UpdateCompactViewVisibility();
                        AnimateIsland(CurrentCompactWidth, CurrentCompactHeight);
                    };
                    CompactPowerModeView.BeginAnimation(OpacityProperty, fadeOut);
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
                WindowHelper.EnsureTopMost(this);
            };
            _topMostTimer.Start();

            Deactivated += (s, e) =>
            {
                WindowHelper.EnsureTopMost(this);
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
            KeyDown += MainWindow_KeyDown;
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
                    // Hotspot active in notch area for hovering popout
                    TopEdgeHotspot.Visibility = Visibility.Visible;
                    if (!HasActiveAlert && !IslandContainer.IsMouseOver)
                    {
                        HideDockSmooth();
                    }
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
        ShowDockSmooth();
        WindowHelper.EnsureTopMost(this);
    }

    private void HideDockSmooth()
    {
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(160));
        anim.Completed += (s, e) =>
        {
            if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
            {
                IslandContainer.Visibility = Visibility.Collapsed;
            }
        };
        IslandContainer.BeginAnimation(OpacityProperty, anim);
    }

    private void ShowDockSmooth()
    {
        IslandContainer.Visibility = Visibility.Visible;
        WindowHelper.EnsureTopMost(this);
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
        UpdateNightModePillVisibility();
        UpdateCompactViewVisibility();
        IslandContainer.Width = CurrentCompactWidth;
        IslandContainer.Height = CurrentCompactHeight;
        _volumeManager.PollVolumeState(forceNotify: true);

        // Breathing/pulsing red recording dot animation
        var pulse = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        ScreenRecPulseDot.BeginAnimation(OpacityProperty, pulse);
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
        if (msg == 0x0046) // WM_WINDOWPOSCHANGING: keep topmost unconditionally
        {
            try
            {
                var wp = System.Runtime.InteropServices.Marshal.PtrToStructure<WindowHelper.WINDOWPOS>(lParam);
                wp.hwndInsertAfter = (IntPtr)(-1); // HWND_TOPMOST
                System.Runtime.InteropServices.Marshal.StructureToPtr(wp, lParam, false);
            }
            catch { }
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

            if (DetachedBubbleContainer.Visibility == Visibility.Visible && DetachedBubbleContainer.Opacity > 0.1)
            {
                Point bubblePoint = DetachedBubbleContainer.TranslatePoint(new Point(0, 0), this);
                Rect bubbleRect = new Rect(bubblePoint.X, bubblePoint.Y, DetachedBubbleContainer.ActualWidth, DetachedBubbleContainer.ActualHeight);

                if (bubbleRect.Contains(windowPoint))
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

        // User explicit requirement: "no need to expand for sound recording, timer, screen recording... it should just show in the notch..."
        if (IsScreenRecordingActive || IsVoiceRecordingActive || IsAnyTimerActive)
        {
            IslandContainer.Cursor = Cursors.Arrow;
            return;
        }

        IslandContainer.Cursor = (IsMusicActive || _isBluetoothAlertActive || HasActiveContent) ? Cursors.Hand : Cursors.Arrow;
        if (!_isExpanded)
        {
            if (_isBluetoothAlertActive)
            {
                _bluetoothAlertTimer.Stop();
                _bluetoothHoverCollapseTimer.Stop();
                ExpandDock(IslandCategory.BluetoothDevice);
                return;
            }

            // Only expand on hover if music is active/paused
            if (IsMusicActive)
            {
                ExpandDock(IslandCategory.MediaPlayback);
            }
        }
        else if (_isBluetoothAlertActive && _activeCategory == IslandCategory.BluetoothDevice)
        {
            _bluetoothHoverCollapseTimer.Stop();
        }
    }

    private void OnIslandMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isExpanded && _activeCategory == IslandCategory.BluetoothDevice)
        {
            _bluetoothHoverCollapseTimer.Stop();
            _bluetoothHoverCollapseTimer.Start();
            return;
        }

        if (_isExpanded && !_isPinned && !_isDraggingSlider)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Start();
        }
        else if (!_isExpanded && _appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert)
        {
            HideDockSmooth();
        }
    }

    private void OnIslandMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            _simBtToggle = !_simBtToggle;
            if (_simBtToggle)
            {
                _bluetoothService.SimulateConnection("Buds T110");
            }
            else
            {
                _bluetoothService.SimulateConnection("Aryan's iPhone", 60);
            }
            return;
        }

        if (e.ChangedButton == MouseButton.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            _privacyService.CycleSimulation();
            return;
        }

        if (e.ChangedButton == MouseButton.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            _simBtToggle = !_simBtToggle;
            if (_simBtToggle)
            {
                _bluetoothService.SimulateConnection("Buds T110");
            }
            else
            {
                _bluetoothService.SimulateConnection("Aryan's iPhone", 60);
            }
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && !_isExpanded)
        {
            // User explicit requirement: "no need to expand for sound recording, timer, screen recording... it should just show in the notch..."
            if (IsScreenRecordingActive || IsVoiceRecordingActive || IsAnyTimerActive)
            {
                return;
            }

            if (HasActiveContent)
            {
                ExpandDock();
            }
        }
    }

    private void ExpandDock(IslandCategory? preferredCategory = null)
    {
        if (_isExpanded) return;

        // User explicit requirement: "no need to expand for sound recording, timer, screen recording... it should just show in the notch..."
        if (preferredCategory == IslandCategory.ScreenRecording ||
            preferredCategory == IslandCategory.VoiceRecording ||
            preferredCategory == IslandCategory.FocusTimer)
        {
            return;
        }

        if (IsScreenRecordingActive || IsVoiceRecordingActive || IsAnyTimerActive)
        {
            return;
        }

        if (!HasActiveContent && !_isNotificationAlertActive) return;
        _isExpanded = true;

        if (preferredCategory.HasValue)
        {
            _activeCategory = preferredCategory.Value;
        }
        else if (_isBluetoothAlertActive)
        {
            _activeCategory = IslandCategory.BluetoothDevice;
        }
        else if (_isNotificationAlertActive && _currentNotification != null)
        {
            _activeCategory = IslandCategory.AppNotification;
        }
        else if (IsSplitActive)
        {
            // By default hovering the split island expands to the music player
            _activeCategory = IslandCategory.MediaPlayback;
        }
        else if (IsMusicActive)
        {
            _activeCategory = IslandCategory.MediaPlayback;
        }
        else
        {
            // Do not expand blank placeholder card when no background task is active
            _isExpanded = false;
            return;
        }

        double targetHeight = _activeCategory == IslandCategory.AppNotification
            ? 72.0
            : _activeCategory == IslandCategory.BluetoothDevice
                ? ExpandedBluetoothHeight
                : ExpandedHeight;

        double targetWidth = _activeCategory == IslandCategory.BluetoothDevice
            ? ExpandedBluetoothWidth
            : ExpandedWidth;

        AnimateIsland(targetWidth, targetHeight);

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

        if (_activeCategory == IslandCategory.BluetoothDevice)
        {
            _isBluetoothAlertActive = false;
            _activeBluetoothDevice = null;
        }

        // Restore non-activating window mode when collapsing
        WindowHelper.SetActivationEnabled(this, false);

        AnimateIsland(CurrentCompactWidth, CurrentCompactHeight);

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

        UpdateSplitBubbleAnimation(targetWidth);
    }

    private void UpdateSplitBubbleAnimation(double mainIslandTargetWidth)
    {
        if (IsSplitActive)
        {
            // Position detached bubble adjacent to the right edge of the main island
            // Offset from center = (mainIslandWidth / 2) + gap + (bubbleWidth / 2)
            double bubbleCenterX = (mainIslandTargetWidth / 2.0) + DetachedBubbleGap + (DetachedBubbleWidth / 2.0);
            DetachedBubbleContainer.Margin = new Thickness(bubbleCenterX * 2.0, 0, 0, 0);

            if (DetachedBubbleContainer.Visibility != Visibility.Visible || DetachedBubbleContainer.Opacity < 0.1)
            {
                DetachedBubbleContainer.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                };
                DetachedBubbleContainer.BeginAnimation(OpacityProperty, fadeIn);
            }
        }
        else
        {
            if (DetachedBubbleContainer.Visibility == Visibility.Visible && DetachedBubbleContainer.Opacity > 0.0)
            {
                var fadeOut = new DoubleAnimation(DetachedBubbleContainer.Opacity, 0.0, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, e) =>
                {
                    DetachedBubbleContainer.Visibility = Visibility.Collapsed;
                };
                DetachedBubbleContainer.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                DetachedBubbleContainer.Visibility = Visibility.Collapsed;
            }
        }
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
        ViewVoiceRecording.Visibility = _activeCategory == IslandCategory.VoiceRecording ? Visibility.Visible : Visibility.Collapsed;
        ViewScreenRecording.Visibility = _activeCategory == IslandCategory.ScreenRecording ? Visibility.Visible : Visibility.Collapsed;
        ViewBluetoothDevice.Visibility = _activeCategory == IslandCategory.BluetoothDevice ? Visibility.Visible : Visibility.Collapsed;

        if (_isExpanded)
        {
            double targetHeight = _activeCategory == IslandCategory.AppNotification
                ? 72.0
                : _activeCategory == IslandCategory.BluetoothDevice
                    ? ExpandedBluetoothHeight
                    : ExpandedHeight;
            double targetWidth = _activeCategory == IslandCategory.BluetoothDevice
                ? ExpandedBluetoothWidth
                : ExpandedWidth;
            AnimateIsland(targetWidth, targetHeight);
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

                    SplitAlbumImage.Source = track.Thumbnail;
                    SplitAlbumImage.Visibility = Visibility.Visible;
                    SplitDefaultOrb.Visibility = Visibility.Collapsed;
                    SplitOrbSymbol.Visibility = Visibility.Collapsed;
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

                    SplitAlbumImage.Source = null;
                    SplitAlbumImage.Visibility = Visibility.Collapsed;
                    SplitDefaultOrb.Visibility = Visibility.Visible;
                    SplitOrbSymbol.Visibility = Visibility.Visible;
                }

                SplitEqualizer.IsPlaying = track.IsPlaying;
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

                SplitAlbumImage.Source = null;
                SplitAlbumImage.Visibility = Visibility.Collapsed;
                SplitDefaultOrb.Visibility = Visibility.Visible;
                SplitOrbSymbol.Visibility = Visibility.Visible;
                SplitEqualizer.IsPlaying = false;

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

            if (!_muteStateInitialized)
            {
                _muteStateInitialized = true;
                _lastMuteState = isMuted;
                return;
            }

            if (isMuted != _lastMuteState)
            {
                _lastMuteState = isMuted;
                ShowSilentRingAnimation(isMuted);
            }
        });
    }

    private void OnFocusAssistChanged(FocusAssistState state)
    {
        Dispatcher.Invoke(() =>
        {
            ShowDndAnimation(state);
        });
    }

    public void ShowSilentRingAnimation(bool isSilent)
    {
        _isSilentRingAlertActive = true;
        _silentRingAlertTimer.Stop();

        // Clear any other temporary compact alerts
        if (_isUnlockAlertActive)
        {
            _isUnlockAlertActive = false;
            _unlockAlertTimer.Stop();
            CompactUnlockView.Visibility = Visibility.Collapsed;
        }
        if (_isDndAlertActive)
        {
            _isDndAlertActive = false;
            _dndAlertTimer.Stop();
            CompactDndView.Visibility = Visibility.Collapsed;
        }
        if (_isNightModeAlertActive)
        {
            _isNightModeAlertActive = false;
            _nightModeAlertTimer.Stop();
            CompactNightModeView.Visibility = Visibility.Collapsed;
        }

        // Configure Silent vs Ring presentation matching Images 1 & 2
        if (isSilent)
        {
            CompactSilentBadge.Visibility = Visibility.Visible;
            CompactRingBadge.Visibility = Visibility.Collapsed;
            CompactSilentRingText.Text = "Silent";
            CompactSilentRingText.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58)); // #FF453A iOS Coral Red
        }
        else
        {
            CompactSilentBadge.Visibility = Visibility.Collapsed;
            CompactRingBadge.Visibility = Visibility.Visible;
            CompactSilentRingText.Text = "Ring";
            CompactSilentRingText.Foreground = Brushes.White;
        }

        // Hide other compact views and show Silent/Ring view
        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactChargingView.Visibility = Visibility.Collapsed;
        CompactUnlockView.Visibility = Visibility.Collapsed;
        CompactDndView.Visibility = Visibility.Collapsed;

        CompactSilentRingView.Opacity = 0.0;
        CompactSilentRingView.Visibility = Visibility.Visible;

        // If currently expanded, collapse into compact notch
        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        AnimateIsland(CompactSilentRingWidth, CompactHeight);

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactSilentRingView.BeginAnimation(OpacityProperty, fadeIn);

        _silentRingAlertTimer.Start();
    }

    public void ShowDndAnimation(FocusAssistState state)
    {
        _isDndAlertActive = true;
        _dndAlertTimer.Stop();

        if (_isUnlockAlertActive)
        {
            _isUnlockAlertActive = false;
            _unlockAlertTimer.Stop();
            CompactUnlockView.Visibility = Visibility.Collapsed;
        }
        if (_isSilentRingAlertActive)
        {
            _isSilentRingAlertActive = false;
            _silentRingAlertTimer.Stop();
            CompactSilentRingView.Visibility = Visibility.Collapsed;
        }
        if (_isNightModeAlertActive)
        {
            _isNightModeAlertActive = false;
            _nightModeAlertTimer.Stop();
            CompactNightModeView.Visibility = Visibility.Collapsed;
        }

        if (state == FocusAssistState.Off)
        {
            CompactDndText.Text = "Focus Off";
            CompactDndText.Foreground = Brushes.White;
            CompactDndBadge.Background = new SolidColorBrush(Color.FromArgb(120, 175, 82, 222));
        }
        else
        {
            CompactDndText.Text = "Do Not Disturb";
            CompactDndText.Foreground = new SolidColorBrush(Color.FromRgb(175, 82, 222)); // #AF52DE iOS Purple
            CompactDndBadge.Background = new SolidColorBrush(Color.FromRgb(175, 82, 222));
        }

        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactChargingView.Visibility = Visibility.Collapsed;
        CompactUnlockView.Visibility = Visibility.Collapsed;
        CompactSilentRingView.Visibility = Visibility.Collapsed;

        CompactDndView.Opacity = 0.0;
        CompactDndView.Visibility = Visibility.Visible;

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        AnimateIsland(CompactDndWidth, CompactHeight);

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactDndView.BeginAnimation(OpacityProperty, fadeIn);

        _dndAlertTimer.Start();
    }

    private void OnNightModeChanged(bool isNightModeOn)
    {
        Dispatcher.Invoke(() =>
        {
            ShowNightModeAnimation(isNightModeOn);
            UpdateNightModePillVisibility();
        });
    }

    public void ShowNightModeAnimation(bool isNightModeOn)
    {
        _isNightModeAlertActive = true;
        _nightModeAlertTimer.Stop();

        if (_isUnlockAlertActive)
        {
            _isUnlockAlertActive = false;
            _unlockAlertTimer.Stop();
            CompactUnlockView.Visibility = Visibility.Collapsed;
        }
        if (_isSilentRingAlertActive)
        {
            _isSilentRingAlertActive = false;
            _silentRingAlertTimer.Stop();
            CompactSilentRingView.Visibility = Visibility.Collapsed;
        }
        if (_isDndAlertActive)
        {
            _isDndAlertActive = false;
            _dndAlertTimer.Stop();
            CompactDndView.Visibility = Visibility.Collapsed;
        }
        if (_isChargingAlertActive)
        {
            _isChargingAlertActive = false;
            _chargingAlertTimer.Stop();
            CompactChargingView.Visibility = Visibility.Collapsed;
        }

        if (isNightModeOn)
        {
            CompactNightModeText.Text = "Night Mode On";
            CompactNightModeText.Foreground = new SolidColorBrush(Color.FromRgb(255, 159, 10)); // #FF9F0A Apple vibrant orange
            CompactNightModeBadge.Background = new SolidColorBrush(Color.FromRgb(255, 149, 0)); // #FF9500
            CompactNightModeMoonIcon.Fill = Brushes.White;
        }
        else
        {
            CompactNightModeText.Text = "Night Mode Off";
            CompactNightModeText.Foreground = Brushes.White;
            CompactNightModeBadge.Background = new SolidColorBrush(Color.FromArgb(120, 255, 149, 0));
            CompactNightModeMoonIcon.Fill = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        }

        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactChargingView.Visibility = Visibility.Collapsed;
        CompactUnlockView.Visibility = Visibility.Collapsed;
        CompactSilentRingView.Visibility = Visibility.Collapsed;
        CompactDndView.Visibility = Visibility.Collapsed;
        CompactLiveTimerView.Visibility = Visibility.Collapsed;
        CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
        CompactScreenRecordingView.Visibility = Visibility.Collapsed;

        CompactNightModeView.Opacity = 0.0;
        CompactNightModeView.Visibility = Visibility.Visible;

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        AnimateIsland(CompactNightModeWidth, CompactHeight);

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactNightModeView.BeginAnimation(OpacityProperty, fadeIn);

        _nightModeAlertTimer.Start();
    }

    private void UpdateNightModePillVisibility()
    {
        bool isNight = _nightLightService?.IsNightModeActive ?? false;
        CompactNightModePill.Visibility = isNight ? Visibility.Visible : Visibility.Collapsed;
        CompactMediaNightMoon.Visibility = isNight ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerNightMoon.Visibility = isNight ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CompactClockView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            e.Handled = true;
            _simBtToggle = !_simBtToggle;
            if (_simBtToggle)
            {
                _bluetoothService.SimulateConnection("Buds T110");
            }
            else
            {
                _bluetoothService.SimulateConnection("Aryan's iPhone", 60);
            }
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _privacyService.CycleSimulation();
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _simBtToggle = !_simBtToggle;
                if (_simBtToggle)
                {
                    _bluetoothService.SimulateConnection("Buds T110");
                }
                else
                {
                    _bluetoothService.SimulateConnection("Aryan's iPhone", 60);
                }
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _voiceRecordingService.SimulateToggle();
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                CycleTestPowerMode();
            }
            else if (_isAlarmActive)
            {
                _alarmService.Dismiss();
            }
            else
            {
                _alarmService.TriggerAlarm("Alarm");
            }
        }
    }

    private void CompactNightModePill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ChangedButton == MouseButton.Right)
        {
            _nightLightService.SimulateToggle();
        }
        else
        {
            _nightLightService.OpenNightLightSettings();
        }
    }

    private void OnAlarmStateChanged(bool isRinging, string alarmTitle)
    {
        Dispatcher.Invoke(() =>
        {
            _isAlarmActive = isRinging;
            CompactAlarmTitleText.Text = string.IsNullOrWhiteSpace(alarmTitle) ? "Alarm" : alarmTitle;

            if (isRinging)
            {
                ShowDockSmooth();
                WindowHelper.EnsureTopMost(this);

                // Clear any temporary compact alerts
                if (_isUnlockAlertActive)
                {
                    _isUnlockAlertActive = false;
                    _unlockAlertTimer.Stop();
                    CompactUnlockView.Visibility = Visibility.Collapsed;
                }
                if (_isSilentRingAlertActive)
                {
                    _isSilentRingAlertActive = false;
                    _silentRingAlertTimer.Stop();
                    CompactSilentRingView.Visibility = Visibility.Collapsed;
                }
                if (_isDndAlertActive)
                {
                    _isDndAlertActive = false;
                    _dndAlertTimer.Stop();
                    CompactDndView.Visibility = Visibility.Collapsed;
                }
                if (_isNightModeAlertActive)
                {
                    _isNightModeAlertActive = false;
                    _nightModeAlertTimer.Stop();
                    CompactNightModeView.Visibility = Visibility.Collapsed;
                }
                if (_isChargingAlertActive)
                {
                    _isChargingAlertActive = false;
                    _chargingAlertTimer.Stop();
                    CompactChargingView.Visibility = Visibility.Collapsed;
                }

                if (_isExpanded)
                {
                    _isExpanded = false;
                    ExpandedContainer.Visibility = Visibility.Collapsed;
                    CompactView.Visibility = Visibility.Visible;
                    CompactView.Opacity = 1.0;
                }

                StartAlarmWiggleAnimation();
            }
            else
            {
                StopAlarmWiggleAnimation();
            }

            UpdateCompactViewVisibility();
        });
    }

    private void OnAlarmSnoozeStarted(TimeSpan duration)
    {
        Dispatcher.Invoke(() =>
        {
            CompactAlarmTitleText.Text = $"Snoozed ({(int)duration.TotalMinutes}m)";
            CompactAlarmView.Visibility = Visibility.Visible;
            AnimateIsland(CompactAlarmWidth, CompactHeight);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (!_isAlarmActive)
                {
                    CompactAlarmView.Visibility = Visibility.Collapsed;
                    UpdateCompactViewVisibility();
                }
            };
            timer.Start();
        });
    }

    private void CompactAlarmSnoozeBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        uint? nid = _alarmService.ActiveNotificationId;
        _alarmService.Snooze(9);
        if (nid.HasValue)
        {
            _notificationService.Dismiss(nid.Value);
        }
    }

    private void CompactAlarmDismissBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        uint? nid = _alarmService.ActiveNotificationId;
        _alarmService.Dismiss();
        if (nid.HasValue)
        {
            _notificationService.Dismiss(nid.Value);
        }
    }

    private void StartAlarmWiggleAnimation()
    {
        var anim = new DoubleAnimation(-8.0, 8.0, TimeSpan.FromMilliseconds(75))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        AlarmIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void StopAlarmWiggleAnimation()
    {
        AlarmIconRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        AlarmIconRotate.Angle = 0;
    }

    private void ClearTemporaryAlerts(bool keepPowerMode = false)
    {
        if (_isUnlockAlertActive)
        {
            _isUnlockAlertActive = false;
            _unlockAlertTimer.Stop();
            CompactUnlockView.Visibility = Visibility.Collapsed;
        }
        if (_isSilentRingAlertActive)
        {
            _isSilentRingAlertActive = false;
            _silentRingAlertTimer.Stop();
            CompactSilentRingView.Visibility = Visibility.Collapsed;
        }
        if (_isDndAlertActive)
        {
            _isDndAlertActive = false;
            _dndAlertTimer.Stop();
            CompactDndView.Visibility = Visibility.Collapsed;
        }
        if (_isNightModeAlertActive)
        {
            _isNightModeAlertActive = false;
            _nightModeAlertTimer.Stop();
            CompactNightModeView.Visibility = Visibility.Collapsed;
        }
        if (_isChargingAlertActive)
        {
            _isChargingAlertActive = false;
            _chargingAlertTimer.Stop();
            CompactChargingView.Visibility = Visibility.Collapsed;
        }
        if (!keepPowerMode && _isPowerModeAlertActive)
        {
            _isPowerModeAlertActive = false;
            _powerModeAlertTimer.Stop();
            CompactPowerModeView.Visibility = Visibility.Collapsed;
        }
    }

    private bool _simBtToggle = false;
    private int _testPowerModeIndex = 0;
    private void CycleTestPowerMode()
    {
        var modes = new[] { WindowsPowerMode.BestPowerEfficiency, WindowsPowerMode.Balanced, WindowsPowerMode.BestPerformance };
        _testPowerModeIndex = (_testPowerModeIndex + 1) % modes.Length;
        _powerModeService.SimulateMode(modes[_testPowerModeIndex]);
    }

    private void OnPowerModeChanged(WindowsPowerMode mode)
    {
        Dispatcher.Invoke(() =>
        {
            ShowPowerModeAlert(mode);
        });
    }

    public void ShowPowerModeAlert(WindowsPowerMode mode)
    {
        ClearTemporaryAlerts(keepPowerMode: true);
        _activePowerMode = mode;
        _isPowerModeAlertActive = true;
        _powerModeAlertTimer.Stop();
        _powerModeAlertTimer.Interval = TimeSpan.FromSeconds(3.0);

        ShowDockSmooth();
        WindowHelper.EnsureTopMost(this);

        Color accentColor;
        SolidColorBrush badgeBrush;
        string title;
        double targetWidth;

        switch (mode)
        {
            case WindowsPowerMode.BestPowerEfficiency:
                title = "Best Power Efficiency";
                accentColor = Color.FromRgb(48, 209, 88); // #30D158 Apple Green
                badgeBrush = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                targetWidth = CompactPowerEfficiencyWidth;
                break;

            case WindowsPowerMode.BestPerformance:
                title = "Best Performance";
                accentColor = Color.FromRgb(255, 59, 48); // #FF3B30 Apple Red
                badgeBrush = new SolidColorBrush(Color.FromRgb(255, 59, 48));
                targetWidth = CompactPowerPerformanceWidth;
                break;

            case WindowsPowerMode.Balanced:
            default:
                title = "Balanced";
                accentColor = Color.FromRgb(0, 122, 255); // #007AFF Apple Blue
                badgeBrush = new SolidColorBrush(Color.FromRgb(0, 122, 255));
                targetWidth = CompactPowerBalancedWidth;
                break;
        }

        CompactPowerModeBadge.Background = badgeBrush;
        PowerModeBadgeGlow.Color = accentColor;
        CompactPowerModeText.Text = title;
        CompactPowerModeText.Foreground = new SolidColorBrush(accentColor);

        // Hide other compact views & show Power Mode view
        UpdateCompactViewVisibility();

        CompactPowerModeView.Opacity = 0.0;
        CompactPowerModeView.Visibility = Visibility.Visible;

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        // Animate island container width
        AnimateIsland(targetWidth, CompactHeight);

        // Ambient edge glow matching mode color
        IslandShadowEffect.Color = accentColor;
        IslandShadowEffect.Opacity = 0.40;
        IslandShadowEffect.BlurRadius = 24;

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactPowerModeView.BeginAnimation(OpacityProperty, fadeIn);

        _powerModeAlertTimer.Start();
    }

    private void CompactPowerModeView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true });
        }
        catch { }
    }

    private FileSystemWatcher? _btTriggerWatcher;
    private void SetupBluetoothTestTriggerWatcher()
    {
        try
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            _btTriggerWatcher = new FileSystemWatcher(dir, "bt_trigger.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _btTriggerWatcher.Changed += (s, e) => HandleTriggerFile(e.FullPath);
            _btTriggerWatcher.Created += (s, e) => HandleTriggerFile(e.FullPath);
        }
        catch { }
    }

    private void HandleTriggerFile(string path)
    {
        try
        {
            System.Threading.Thread.Sleep(60);
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path).Trim();
            App.Log($"HandleTriggerFile: received '{text}'");
            if (string.IsNullOrWhiteSpace(text)) return;
            try { File.Delete(path); } catch { }

            string[] parts = text.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            string name = parts[0].Trim();
            int? battery = null;
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int b))
            {
                battery = b;
            }

            Dispatcher.Invoke(() =>
            {
                _bluetoothService.SimulateConnection(name, battery);
            });
        }
        catch (Exception ex)
        {
            App.Log($"HandleTriggerFile error: {ex.Message}");
        }
    }

    private static BitmapImage? _cachedHeadsetIcon;
    private static BitmapImage? _cachedPhoneIcon;

    private static ImageSource? GetBluetoothDeviceIcon(BluetoothDeviceKind kind)
    {
        try
        {
            if (kind == BluetoothDeviceKind.Smartphone)
            {
                _cachedPhoneIcon ??= new BitmapImage(new Uri("pack://application:,,,/Resources/win_phone.png", UriKind.Absolute));
                return _cachedPhoneIcon;
            }
            else
            {
                _cachedHeadsetIcon ??= new BitmapImage(new Uri("pack://application:,,,/Resources/win_headset.png", UriKind.Absolute));
                return _cachedHeadsetIcon;
            }
        }
        catch
        {
            try
            {
                string relPath = kind == BluetoothDeviceKind.Smartphone ? "Resources/win_phone.png" : "Resources/win_headset.png";
                string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relPath);
                if (System.IO.File.Exists(fullPath))
                {
                    return new BitmapImage(new Uri(fullPath, UriKind.Absolute));
                }
            }
            catch { }
        }
        return null;
    }

    private void OnBluetoothDeviceConnected(BluetoothDeviceInfo dev)
    {
        Dispatcher.Invoke(() =>
        {
            App.Log($"OnBluetoothDeviceConnected: Name='{dev.Name}', Kind={dev.DeviceKind}, Battery={dev.BatteryPercent}%");
            ShowDockSmooth();
            WindowHelper.EnsureTopMost(this);

            _activeBluetoothDevice = dev;
            _isBluetoothAlertActive = true;
            _bluetoothAlertTimer.Stop();
            _bluetoothHoverCollapseTimer.Stop();

            ClearTemporaryAlerts();

            // Set built-in Windows device icon (headset or smartphone)
            var icon = GetBluetoothDeviceIcon(dev.DeviceKind);
            if (icon != null)
            {
                CompactBluetoothDeviceIcon.Source = icon;
                ExpandedBluetoothDeviceIcon.Source = icon;
            }

            // Populate compact glimpse UI (Windows device icon + device name)
            CompactBluetoothDeviceName.Text = dev.Name;
            CompactBluetoothDeviceName.Visibility = Visibility.Visible;

            // Populate expanded UI (Windows device icon + "Connected" + "[Device] ready to use" + Battery Ring)
            ExpandedBluetoothTitleText.Text = $"{dev.Name} ready to use";
            UpdateBluetoothBatteryArc(dev.BatteryPercent);

            // Play the smooth 2-tone chime
            BluetoothWatcherService.PlayConnectionChime();

            // If dock is currently expanded into another category, collapse it back to compact view
            if (_isExpanded && _activeCategory != IslandCategory.BluetoothDevice)
            {
                _isExpanded = false;
                ExpandedContainer.Visibility = Visibility.Collapsed;
                CompactView.Visibility = Visibility.Visible;
                CompactView.Opacity = 1.0;
            }

            UpdateCompactViewVisibility();

            CompactBluetoothView.Opacity = 0.0;
            CompactBluetoothView.Visibility = Visibility.Visible;

            double targetWidth = Math.Max(CompactBluetoothWidth, 190.0 + (dev.Name.Length * 7.0));
            AnimateIsland(targetWidth, CompactHeight);

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
            CompactBluetoothView.BeginAnimation(OpacityProperty, fadeIn);

            // Short glimpse stays for 5 seconds then collapses unless hovered
            _bluetoothAlertTimer.Start();
        });
    }

    private void OnBluetoothDeviceDisconnected(BluetoothDeviceInfo dev)
    {
        Dispatcher.Invoke(() =>
        {
            if (_activeBluetoothDevice?.Id == dev.Id || string.Equals(_activeBluetoothDevice?.Name, dev.Name, StringComparison.OrdinalIgnoreCase))
            {
                _bluetoothAlertTimer.Stop();
                _bluetoothHoverCollapseTimer.Stop();
                if (_isExpanded && _activeCategory == IslandCategory.BluetoothDevice)
                {
                    _isBluetoothAlertActive = false;
                    _activeBluetoothDevice = null;
                    CollapseDock();
                }
                else if (_isBluetoothAlertActive)
                {
                    _isBluetoothAlertActive = false;
                    _activeBluetoothDevice = null;
                    UpdateCompactViewVisibility();
                }
            }
        });
    }

    private void UpdateBluetoothBatteryArc(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        string percentStr = $"{clamped}%";
        ExpandedBluetoothBatteryPercentText.Text = percentStr;
        CompactBluetoothBatteryPercentText.Text = percentStr;

        // Dynamic battery colors: Red (<=10%), Amber (<=20%), Green (>20%)
        SolidColorBrush strokeBrush;
        if (clamped <= 10)
        {
            strokeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        }
        else if (clamped <= 20)
        {
            strokeBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
        }
        else
        {
            strokeBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        }

        BluetoothBatteryArc.Stroke = strokeBrush;
        ExpandedBluetoothBatteryPercentText.Foreground = strokeBrush;

        CompactBluetoothBatteryArc.Stroke = strokeBrush;
        CompactBluetoothBatteryPercentText.Foreground = strokeBrush;

        if (clamped <= 0)
        {
            BluetoothBatteryArc.Data = null;
            CompactBluetoothBatteryArc.Data = null;
            return;
        }

        // Expanded Arc (Grid 44x44, Center 21, 21, Radius 17.5)
        BluetoothBatteryArc.Data = CreateArcGeometry(21.0, 21.0, 17.5, clamped);

        // Compact Arc (Grid 28x28, Center 14, 14, Radius 11.0)
        CompactBluetoothBatteryArc.Data = CreateArcGeometry(14.0, 14.0, 11.0, clamped);
    }

    private static Geometry CreateArcGeometry(double cx, double cy, double r, double clamped)
    {
        double startAngle = -90.0; // 12 o'clock
        double sweepAngle = (clamped / 100.0) * 360.0;
        if (sweepAngle >= 360.0) sweepAngle = 359.99;

        double endAngleRad = (startAngle + sweepAngle) * Math.PI / 180.0;
        double startX = cx;
        double startY = cy - r;
        double endX = cx + r * Math.Cos(endAngleRad);
        double endY = cy + r * Math.Sin(endAngleRad);

        bool isLargeArc = sweepAngle > 180.0;

        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = new Point(startX, startY),
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(endX, endY),
            Size = new Size(r, r),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLargeArc
        });
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void CompactBluetoothView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _bluetoothAlertTimer.Stop();
        _bluetoothHoverCollapseTimer.Stop();
        ExpandDock(IslandCategory.BluetoothDevice);
    }

    private void ViewBluetoothDevice_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnChargingConnected()
    {
        Dispatcher.Invoke(() =>
        {
            ShowBatteryAlert(BatteryAlertType.Charging);
        });
    }

    private void OnBatteryAlert(BatteryAlertType alertType)
    {
        Dispatcher.Invoke(() =>
        {
            ShowBatteryAlert(alertType);
        });
    }

    public void ShowChargingAnimation() => ShowBatteryAlert(BatteryAlertType.Charging);

    public void ShowBatteryAlert(BatteryAlertType alertType)
    {
        ShowDockSmooth();
        WindowHelper.EnsureTopMost(this);

        _isChargingAlertActive = true;
        _chargingAlertTimer.Stop();

        if (_isNightModeAlertActive)
        {
            _isNightModeAlertActive = false;
            _nightModeAlertTimer.Stop();
            CompactNightModeView.Visibility = Visibility.Collapsed;
        }

        int percent = Math.Clamp(_powerService.BatteryPercent, 1, 100);
        CompactChargingText.Text = $"{percent}%";

        // Battery fill calculation (inner capsule usable width is 31px)
        double targetFillWidth = Math.Max(3.0, (percent / 100.0) * 31.0);

        Color glowColor;
        SolidColorBrush fillBrush;
        SolidColorBrush backBrush;
        SolidColorBrush borderBrush;

        switch (alertType)
        {
            case BatteryAlertType.LowBattery:
                CompactBatteryAlertTitle.Text = "Low Battery";
                CompactBatteryAlertTitle.FontWeight = FontWeights.Bold;
                CompactChargingText.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58)); // #FF453A Red
                fillBrush = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                backBrush = new SolidColorBrush(Color.FromArgb(180, 53, 22, 22)); // #351616
                borderBrush = new SolidColorBrush(Color.FromArgb(200, 80, 36, 36));
                glowColor = Color.FromRgb(255, 69, 58);
                break;

            case BatteryAlertType.Unplugged:
                CompactBatteryAlertTitle.Text = "Unplugged";
                CompactBatteryAlertTitle.FontWeight = FontWeights.Bold;
                CompactChargingText.Foreground = new SolidColorBrush(Color.FromRgb(199, 199, 204)); // #C7C7CC
                fillBrush = new SolidColorBrush(Color.FromRgb(142, 142, 147)); // #8E8E93 Silver/Grey
                backBrush = new SolidColorBrush(Color.FromArgb(180, 44, 44, 46)); // #2C2C2E
                borderBrush = new SolidColorBrush(Color.FromArgb(200, 72, 72, 74));
                glowColor = Colors.Black;
                break;

            case BatteryAlertType.PowerSaver:
                CompactBatteryAlertTitle.Text = "Power Saver";
                CompactBatteryAlertTitle.FontWeight = FontWeights.Bold;
                CompactChargingText.Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 10)); // #FFD60A Apple Yellow
                fillBrush = new SolidColorBrush(Color.FromRgb(255, 214, 10));
                backBrush = new SolidColorBrush(Color.FromArgb(180, 53, 48, 16)); // #353010
                borderBrush = new SolidColorBrush(Color.FromArgb(200, 80, 72, 28));
                glowColor = Color.FromRgb(255, 214, 10);
                break;

            case BatteryAlertType.Charging:
            default:
                CompactBatteryAlertTitle.Text = "Charging";
                CompactBatteryAlertTitle.FontWeight = FontWeights.Normal;
                CompactChargingText.Foreground = new SolidColorBrush(Color.FromRgb(48, 209, 88)); // #30D158 Apple Green
                fillBrush = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                backBrush = new SolidColorBrush(Color.FromRgb(22, 53, 25)); // #163519
                borderBrush = new SolidColorBrush(Color.FromRgb(36, 80, 40));
                glowColor = Color.FromRgb(48, 209, 88);
                break;
        }

        CompactChargingBatteryBack.Background = backBrush;
        CompactChargingBatteryBack.BorderBrush = borderBrush;
        CompactChargingBatteryFill.Background = fillBrush;
        CompactChargingBatteryCap.Background = borderBrush;

        // Hide other compact views and activate battery alert view
        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
        CompactChargingView.Opacity = 0.0;
        CompactChargingView.Visibility = Visibility.Visible;

        // If currently expanded, collapse into compact dock
        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        // Animate island container width to 260.0 (compact height 36.0)
        AnimateIsland(CompactChargingWidth, CompactHeight);

        // Ambient glow
        if (alertType == BatteryAlertType.Unplugged)
        {
            IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
            IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
            IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;
        }
        else
        {
            IslandShadowEffect.Color = glowColor;
            IslandShadowEffect.Opacity = 0.38;
            IslandShadowEffect.BlurRadius = 24;
        }

        // Smooth fade-in of the battery alert view
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactChargingView.BeginAnimation(OpacityProperty, fadeIn);

        // Smooth battery fill animation from 0 to targetFillWidth
        var fillAnim = new DoubleAnimation(0.0, targetFillWidth, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
        };
        CompactChargingBatteryFill.BeginAnimation(WidthProperty, fillAnim);

        // Display for at max 5.0 seconds then smoothly disappear
        _chargingAlertTimer.Start();
    }

    private int _testBatteryAlertIndex = 0;
    private void CompactChargingPill_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var alerts = new[] { BatteryAlertType.LowBattery, BatteryAlertType.Unplugged, BatteryAlertType.PowerSaver, BatteryAlertType.Charging };
        ShowBatteryAlert(alerts[_testBatteryAlertIndex % alerts.Length]);
        _testBatteryAlertIndex++;
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

        if (_isNightModeAlertActive)
        {
            _isNightModeAlertActive = false;
            _nightModeAlertTimer.Stop();
            CompactNightModeView.Visibility = Visibility.Collapsed;
        }

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

            // Format "M:SS" or "MM:SS" matching reference (e.g. "0:58", "4:32")
            string formattedTimer = $"{_focusService.Remaining.Minutes}:{_focusService.Remaining.Seconds:D2}";
            CompactLiveTimerCountdownText.Text = formattedTimer;
            DetachedTimerCountdownText.Text = formattedTimer;

            // Update pause/play icon state in the compact pill
            if (_focusService.IsRunning)
            {
                CompactTimerPauseBar1.Visibility = Visibility.Visible;
                CompactTimerPauseBar2.Visibility = Visibility.Visible;
                CompactTimerPlayTriangle.Visibility = Visibility.Collapsed;
            }
            else
            {
                CompactTimerPauseBar1.Visibility = Visibility.Collapsed;
                CompactTimerPauseBar2.Visibility = Visibility.Collapsed;
                CompactTimerPlayTriangle.Visibility = Visibility.Visible;
            }

            UpdateCompactViewVisibility();
        });
    }

    private void CompactTimerPauseBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_windowsClockService.HasActiveTimer)
        {
            if (_windowsClockService.ActiveTimer?.IsRunning == true)
            {
                _windowsClockService.PauseActiveTimer();
                CompactTimerPauseBar1.Visibility = Visibility.Collapsed;
                CompactTimerPauseBar2.Visibility = Visibility.Collapsed;
                CompactTimerPlayTriangle.Visibility = Visibility.Visible;
            }
            else
            {
                _windowsClockService.ResumeActiveTimer();
                CompactTimerPauseBar1.Visibility = Visibility.Visible;
                CompactTimerPauseBar2.Visibility = Visibility.Visible;
                CompactTimerPlayTriangle.Visibility = Visibility.Collapsed;
            }
        }
        else if (_focusService.IsRunning)
        {
            _focusService.Pause();
        }
        else if (_focusService.Remaining.TotalSeconds > 0)
        {
            _focusService.Resume();
        }
    }

    private void CompactTimerCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _windowsClockService.CancelCurrentTimer();
        _focusService.Stop();
        if (_isExpanded && _activeCategory == IslandCategory.FocusTimer)
        {
            CollapseDock();
        }
        UpdateCompactViewVisibility();
    }

    private void OnWindowsClockTimerChanged(WindowsClockTimerInfo? info)
    {
        Dispatcher.Invoke(() =>
        {
            if (info != null && (info.IsRunning || info.RemainingTime.TotalSeconds > 0))
            {
                int totalMins = (int)info.RemainingTime.TotalMinutes;
                int secs = info.RemainingTime.Seconds;
                string formattedTimer = info.RemainingTime.Hours > 0
                    ? $"{info.RemainingTime.Hours}:{info.RemainingTime.Minutes:D2}:{secs:D2}"
                    : $"{totalMins}:{secs:D2}";

                CompactLiveTimerCountdownText.Text = formattedTimer;
                DetachedTimerCountdownText.Text = formattedTimer;
                FocusCountdownText.Text = $"{formattedTimer} remaining";
                FocusTimerTitleText.Text = string.IsNullOrWhiteSpace(info.Name) ? "Timer Active" : $"{info.Name} Active";

                if (info.IsRunning)
                {
                    CompactTimerPauseBar1.Visibility = Visibility.Visible;
                    CompactTimerPauseBar2.Visibility = Visibility.Visible;
                    CompactTimerPlayTriangle.Visibility = Visibility.Collapsed;
                    FocusToggleButton.Content = "Pause";
                }
                else
                {
                    CompactTimerPauseBar1.Visibility = Visibility.Collapsed;
                    CompactTimerPauseBar2.Visibility = Visibility.Collapsed;
                    CompactTimerPlayTriangle.Visibility = Visibility.Visible;
                    FocusToggleButton.Content = "Resume";
                }
            }

            UpdateCompactViewVisibility();
        });
    }

    private static readonly double[] BaseWaveProfile = { 4.5, 7.5, 13.0, 11.0, 6.0, 13.5, 17.0, 13.0, 7.5, 4.5, 8.0, 12.0, 4.5 };
    private static readonly double[] BarSensitivity = { 0.65, 0.85, 1.25, 1.15, 0.75, 1.30, 1.45, 1.30, 0.85, 0.60, 0.95, 1.20, 0.60 };

    private readonly double[] _currentRecBarHeights = new double[13] { 4.5, 7.5, 13.0, 11.0, 6.0, 13.5, 17.0, 13.0, 7.5, 4.5, 8.0, 12.0, 4.5 };
    private readonly double[] _targetRecBarHeights = new double[13] { 4.5, 7.5, 13.0, 11.0, 6.0, 13.5, 17.0, 13.0, 7.5, 4.5, 8.0, 12.0, 4.5 };
    private double _recWavePhase = 0.0;
    private double _voiceEnergyEnvelope = 0.0;
    private DispatcherTimer? _recordingWaveformTimer;

    private void StartRecordingWaveformAnimation()
    {
        if (_recordingWaveformTimer == null)
        {
            _recordingWaveformTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(25) // ~40 FPS high-refresh render loop
            };
            _recordingWaveformTimer.Tick += OnRecordingWaveformTick;
        }

        if (!_recordingWaveformTimer.IsEnabled)
        {
            _recordingWaveformTimer.Start();
        }
    }

    private void StopRecordingWaveformAnimation()
    {
        _recordingWaveformTimer?.Stop();
    }

    private void OnRecordingWaveformTick(object? sender, EventArgs e)
    {
        if (!IsVoiceRecordingActive)
        {
            StopRecordingWaveformAnimation();
            return;
        }

        // 1. Fetch instantaneous microphone audio peak directly with microseconds latency
        float rawPeak = _voiceRecordingService.GetCurrentPeak();

        // 2. Square-root expansion for authentic natural human voice dynamic range
        double voiceEnergy = Math.Sqrt(Math.Clamp(rawPeak * 3.8, 0.0, 1.0));

        // Fast attack / smooth release envelope follower
        if (voiceEnergy > _voiceEnergyEnvelope)
        {
            _voiceEnergyEnvelope += (voiceEnergy - _voiceEnergyEnvelope) * 0.75; // Fast rise
        }
        else
        {
            _voiceEnergyEnvelope += (voiceEnergy - _voiceEnergyEnvelope) * 0.16; // Smooth audio release
        }

        // 3. Increment phase for traveling harmonic ripples
        _recWavePhase += 0.18;

        Border[] bars = { RecBar1, RecBar2, RecBar3, RecBar4, RecBar5, RecBar6, RecBar7, RecBar8, RecBar9, RecBar10, RecBar11, RecBar12, RecBar13 };

        for (int i = 0; i < bars.Length; i++)
        {
            // Organic multi-harmonic wave: living ripple across the pill so bars are never flat dots
            double wave = Math.Sin(_recWavePhase + i * 0.52) * 0.65 + Math.Sin(_recWavePhase * 1.8 + i * 0.95) * 0.35;
            double ambientH = BaseWaveProfile[i] * (0.75 + 0.35 * wave);

            // Spikes: surge upward when voice energy is detected, modulated per-bar
            double spikeMod = 0.8 + 0.4 * Math.Sin(_recWavePhase * 2.2 + i * 0.7);
            double spikeBonus = _voiceEnergyEnvelope * 13.5 * BarSensitivity[i] * spikeMod;

            double targetH = Math.Clamp(ambientH + spikeBonus, 3.5, 26.0);
            _targetRecBarHeights[i] = targetH;

            // Liquid spring interpolation: fast attack, smooth decay
            double delta = _targetRecBarHeights[i] - _currentRecBarHeights[i];
            if (delta > 0)
            {
                _currentRecBarHeights[i] += delta * 0.60;
            }
            else
            {
                _currentRecBarHeights[i] += delta * 0.25;
            }

            double finalH = Math.Clamp(_currentRecBarHeights[i], 3.5, 26.0);
            bars[i].Height = finalH;
        }
    }

    private void OnVoiceRecordingStateChanged(VoiceRecordingInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            if (info.IsRecording)
            {
                int totalMins = (int)info.Duration.TotalMinutes;
                int secs = info.Duration.Seconds;
                string durStr = info.Duration.Hours > 0
                    ? $"{info.Duration.Hours}:{info.Duration.Minutes:D2}:{secs:D2}"
                    : $"{totalMins:D2}:{secs:D2}";

                CompactRecordingDurationText.Text = durStr;
                RecordingDurationSubtext.Text = $"{durStr} recorded";
                RecordingAppNameText.Text = $"{info.AppName} Recording";

                StartRecordingWaveformAnimation();
            }
            else
            {
                StopRecordingWaveformAnimation();
                if (_isExpanded && _activeCategory == IslandCategory.VoiceRecording)
                {
                    CollapseDock();
                }
            }
            UpdateCompactViewVisibility();
        });
    }

    private void OnScreenRecordingStateChanged(ScreenRecordingInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            if (info.IsRecording)
            {
                int totalMins = (int)info.Duration.TotalMinutes;
                int secs = info.Duration.Seconds;
                string durStr = info.Duration.Hours > 0
                    ? $"{info.Duration.Hours}:{info.Duration.Minutes:D2}:{secs:D2}"
                    : $"{totalMins}:{secs:D2}";

                CompactScreenRecDurationText.Text = durStr;
                ScreenRecDurationSubtext.Text = $"{durStr} recorded";
                ScreenRecAppNameText.Text = $"{info.AppName} Active";
            }
            else
            {
                if (_isExpanded && _activeCategory == IslandCategory.ScreenRecording)
                {
                    CollapseDock();
                }
            }
            UpdateCompactViewVisibility();
        });
    }

    private void ScreenRecordingStop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _screenRecordingService.StopActiveRecording();
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
        if (_windowsClockService.HasActiveTimer)
        {
            if (_windowsClockService.ActiveTimer?.IsRunning == true)
            {
                _windowsClockService.PauseActiveTimer();
                FocusToggleButton.Content = "Resume";
            }
            else
            {
                _windowsClockService.ResumeActiveTimer();
                FocusToggleButton.Content = "Pause";
            }
        }
        else
        {
            _focusService.Toggle();
        }
    }

    private void FocusCancelButton_Click(object sender, RoutedEventArgs e)
    {
        _windowsClockService.CancelCurrentTimer();
        _focusService.Stop();
        if (_isExpanded && _activeCategory == IslandCategory.FocusTimer)
        {
            CollapseDock();
        }
        UpdateCompactViewVisibility();
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

        // After clicking, if no reply or action within 5s, dismiss and collapse
        _notificationAlertTimer.Stop();
        _notificationAlertTimer.Interval = TimeSpan.FromSeconds(5.0);
        _notificationAlertTimer.Start();
    }

    private bool IsAlarmNotification(IslandNotification notif)
    {
        if (notif == null) return false;
        string app = notif.AppName ?? "";
        string appId = notif.AppId ?? "";
        string title = notif.Title ?? "";
        string msg = notif.Message ?? "";

        bool isClock = appId.Contains("WindowsAlarms", StringComparison.OrdinalIgnoreCase) ||
                       app.Contains("Clock", StringComparison.OrdinalIgnoreCase) ||
                       app.Contains("Alarms", StringComparison.OrdinalIgnoreCase);

        bool isAlarmWord = title.Contains("Alarm", StringComparison.OrdinalIgnoreCase) ||
                           msg.Contains("Alarm", StringComparison.OrdinalIgnoreCase);

        return (isClock && isAlarmWord) || (isClock && !title.Contains("Timer", StringComparison.OrdinalIgnoreCase));
    }

    private void OnNotificationReceived(IslandNotification notif)
    {
        if (IsAlarmNotification(notif))
        {
            Dispatcher.Invoke(() =>
            {
                _alarmService.TriggerAlarm(notif.Title, notif.Id);
            });
            return;
        }

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

            ShowDockSmooth();
            WindowHelper.EnsureTopMost(this);

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
            if (!_isNotificationAlertActive)
            {
                _currentNotification = null;
            }
            else
            {
                _currentNotification = _notificationService.LatestNotification;
                if (_currentNotification != null)
                {
                    UpdateNotificationUI(_currentNotification);
                }
            }

            if (!_notificationService.HasNotifications || !_isNotificationAlertActive)
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
        if (_isAlarmActive)
        {
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactAlarmView.Visibility = Visibility.Visible;
        }
        else if (_isUnlockAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Visible;
        }
        else if (_isSilentRingAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Visible;
        }
        else if (_isDndAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Visible;
        }
        else if (_isNightModeAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Visible;
        }
        else if (_isChargingAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Visible;
        }
        else if (_isBluetoothAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Visible;
        }
        else if (_isPowerModeAlertActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Visible;
        }
        else if (_isNotificationAlertActive && _currentNotification != null)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Visible;
        }
        else if (IsScreenRecordingActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Visible;
        }
        else if (IsVoiceRecordingActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Visible;
        }
        else if (IsSplitActive)
        {
            // Apple Dynamic Island Split Multi-Activity Presentation:
            // Main notch displays compact music (album artwork on left, pulsing waveform on right)
            // Detached satellite bubble displays amber live countdown on the right
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Visible;
        }
        else if (IsAnyTimerActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Visible;
        }
        else if (IsMusicActive)
        {
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactClockView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Visible;
        }
        else
        {
            // Empty notch / idle dock:
            CompactAlarmView.Visibility = Visibility.Collapsed;
            CompactChargingView.Visibility = Visibility.Collapsed;
            CompactNotificationView.Visibility = Visibility.Collapsed;
            CompactUnlockView.Visibility = Visibility.Collapsed;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
            CompactDndView.Visibility = Visibility.Collapsed;
            CompactNightModeView.Visibility = Visibility.Collapsed;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
            CompactLiveTimerView.Visibility = Visibility.Collapsed;
            SplitLeadingMediaView.Visibility = Visibility.Collapsed;
            CompactMediaView.Visibility = Visibility.Collapsed;
            CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
            CompactScreenRecordingView.Visibility = Visibility.Collapsed;

            if (_nightLightService?.IsNightModeActive == true)
            {
                // When Night Mode is active, show the orange crescent moon pill in the dock
                CompactNightModePill.Visibility = Visibility.Visible;
                CompactClockView.Visibility = Visibility.Visible;
            }
            else
            {
                CompactNightModePill.Visibility = Visibility.Collapsed;
                CompactClockView.Visibility = Visibility.Collapsed;
            }
        }

        UpdateNightModePillVisibility();
        UpdatePrivacyIndicatorsUI(_isCameraActive, _isMicrophoneActive);

        if (!_isExpanded)
        {
            AnimateIsland(CurrentCompactWidth, CurrentCompactHeight);
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
    }

    private void Start15MinTimer_Click(object sender, RoutedEventArgs e)
    {
        _focusService.Start(5);
        SwitchCategory(IslandCategory.FocusTimer);
    }

    private void DetachedBubbleContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCollapseTimer.Stop();
        // User explicit requirement: timer stays in notch / bubble without expanding
    }

    private void DetachedBubbleContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isExpanded && !_isPinned && !_isDraggingSlider)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Start();
        }
    }

    private void DetachedBubbleContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // User explicit requirement: timer stays in notch / bubble without expanding
    }

    private void OnPrivacyStateChanged(bool isCameraInUse, bool isMicInUse)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatePrivacyIndicatorsUI(isCameraInUse, isMicInUse);
        });
    }

    private void UpdatePrivacyIndicatorsUI(bool isCameraInUse, bool isMicInUse)
    {
        bool anyActive = isCameraInUse || isMicInUse;
        _isPrivacyIndicatorActive = anyActive;
        _isCameraActive = isCameraInUse;
        _isMicrophoneActive = isMicInUse;

        Visibility camVis = isCameraInUse ? Visibility.Visible : Visibility.Collapsed;
        Visibility micVis = isMicInUse ? Visibility.Visible : Visibility.Collapsed;
        Visibility anyVis = anyActive ? Visibility.Visible : Visibility.Collapsed;

        // 1. Media View: Equalizer shifts to the left, privacy dots sit on the right
        MediaPrivacyPanel.Visibility = anyVis;
        MediaCameraDot.Visibility = camVis;
        MediaMicDot.Visibility = micVis;

        // 2. Split Leading Media View
        SplitPrivacyPanel.Visibility = anyVis;
        SplitCameraDot.Visibility = camVis;
        SplitMicDot.Visibility = micVis;

        // 3. Voice Recording View
        VoicePrivacyPanel.Visibility = anyVis;
        VoiceCameraDot.Visibility = camVis;
        VoiceMicDot.Visibility = micVis;

        // 4. Screen Recording View
        ScreenRecPrivacyPanel.Visibility = anyVis;
        ScreenRecCameraDot.Visibility = camVis;
        ScreenRecMicDot.Visibility = micVis;

        // 5. Live Timer View
        TimerPrivacyPanel.Visibility = anyVis;
        TimerCameraDot.Visibility = camVis;
        TimerMicDot.Visibility = micVis;

        // 6. Clock View
        ClockPrivacyPanel.Visibility = anyVis;
        ClockCameraDot.Visibility = camVis;
        ClockMicDot.Visibility = micVis;

        // 7. Standalone Notch Privacy Panel (for idle/blank notch when no specific view is showing)
        bool isBlankNotch = !IsMusicActive && !IsVoiceRecordingActive && !IsScreenRecordingActive && !IsAnyTimerActive &&
                            !_isAlarmActive && !_isBluetoothAlertActive && !_isPowerModeAlertActive &&
                            !_isChargingAlertActive && !_isNotificationAlertActive && !_isUnlockAlertActive &&
                            !_isSilentRingAlertActive && !_isDndAlertActive && !_isNightModeAlertActive &&
                            CompactClockView.Visibility != Visibility.Visible;

        PrivacyIndicatorPanel.Visibility = (anyActive && isBlankNotch) ? Visibility.Visible : Visibility.Collapsed;
        CameraPrivacyDot.Visibility = camVis;
        MicPrivacyDot.Visibility = micVis;

        if (anyActive && isBlankNotch)
        {
            var anim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
            PrivacyIndicatorPanel.BeginAnimation(OpacityProperty, anim);
        }
        else if (!anyActive)
        {
            var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
            PrivacyIndicatorPanel.BeginAnimation(OpacityProperty, anim);
        }

        // Animate dock width to smoothly expand/contract
        if (!_isExpanded)
        {
            AnimateIsland(CurrentCompactWidth, CurrentCompactHeight);
        }
    }

    private void MediaPrivacyPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _privacyService.CycleSimulation();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (e.Key == Key.C)
            {
                _privacyService.ToggleCameraSimulation();
                e.Handled = true;
            }
            else if (e.Key == Key.M)
            {
                _privacyService.ToggleMicrophoneSimulation();
                e.Handled = true;
            }
        }
    }
}
