using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace WindowsLive;

[ComImport]
[Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataTransferManagerInterop
{
    IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
    void ShowShareUIForWindow([In] IntPtr appWindow);
}

public partial class MainWindow : Window
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint GwOwner = 4;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo info);

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
    private readonly LyricsService _lyricsService;
    private const double ExpandedLyricsWidth = 450.0;
    private const double ExpandedLyricsHeight = 94.0;

    private IslandCategory _activeCategory = IslandCategory.MediaPlayback;
    private bool _isExpanded = false;
    private bool _isPinned = false;
    private bool _isNotchMode = true;
    private bool _isDraggingSlider = false;

    private const double CompactDefaultWidth = 120.0; // 0.75x of original 160.0
    private const double CompactMediaWidth = 180.0;   // 0.75x of original 240.0
    private const double CompactWifiWidth = 250.0;
    private const double CompactChargingWidth = 260.0;
    private const double CompactNotificationWidth = 220.0;
    private const double CompactUnlockWidth = 210.0;
    private const double CompactSilentRingWidth = 180.0; // 0.75x of original 240.0
    private const double CompactDndWidth = 260.0;
    private const double CompactNightModeWidth = 180.0;  // 0.75x of original 240.0
    private const double CompactAlarmWidth = 130.0;
    private const double CompactBluetoothWidth = 230.0;
    private const double CompactIncomingCallWidth = 260.0;
    private const double ExpandedBluetoothWidth = 450.0;
    private const double ExpandedBluetoothHeight = 68.0;
    private const double CompactPowerEfficiencyWidth = 250.0;
    private const double CompactPowerBalancedWidth = 200.0;
    private const double CompactPowerPerformanceWidth = 235.0;
    private const double CompactTimerWidth = 180.0;      // 0.75x of original 240.0
    private const double CompactRecordingWidth = 160.0;  // 0.75x of original 210.0
    private const double CompactScreenRecordingWidth = CompactDefaultWidth;
    private const double CompactSplitLeadingWidth = CompactMediaWidth;
    private const double DetachedBubbleWidth = 78.0;
    private const double DetachedBubbleGap = 16.0;
    private const double ActivityIndicatorBlinkIntervalMs = 1000.0;
    private double _lastDetachedRightTargetX = double.NaN;
    private double _lastDetachedLeftTargetX = double.NaN;
    private const double CompactHeight = 36.0;
    private const double ExpandedWidth = 460.0;
    private const double ExpandedHeight = 86.0;

    private bool _isExplicitExit = false;
    private uint _msgTaskbarCreated = 0;

    private bool _isChargingAlertActive = false;
    private bool _isNotificationAlertActive = false;
    private bool _isIncomingCallActive = false;
    private IslandNotification? _currentIncomingCall = null;
    private readonly DispatcherTimer _incomingCallAlertTimer;
    private bool _isUnlockAlertActive = false;
    private bool _isSilentRingAlertActive = false;
    private bool _isDndAlertActive = false;
    private bool _isNightModeAlertActive = false;
    private bool _isAlarmActive = false;
    private bool _isBluetoothAlertActive = false;
    private bool _isWifiConnected = false;
    private bool _isHotspotActive = false;
    private string? _lastWifiNetworkName = null;
    private bool _hasObservedInitialWifiState = false;
    private bool _isWifiAnimationActive = false;
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
    private readonly DispatcherTimer _wifiAlertTimer;
    private readonly DispatcherTimer _dragDropFeedbackTimer;
    private bool _isWifiAlertActive = false;
    private bool _isDragActive = false;
    private bool _isDropFeedbackActive = false;
    private bool IsFeedbackSurfaceActive => _isDragActive || _isDropFeedbackActive;
    private readonly FocusAssistWatcherService _focusAssistService;
    private readonly NightLightWatcherService _nightLightService;
    private readonly AlarmService _alarmService;
    private readonly BluetoothWatcherService _bluetoothService;
    private readonly WifiActivityService _wifiService;
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
    private bool IsRecordingActivityActive => IsVoiceRecordingActive || IsScreenRecordingActive;
    private bool IsWifiActivityActive => _isWifiAlertActive && (_isWifiConnected || _isHotspotActive);

    private bool IsAnyTimerActive =>
        _focusService.IsRunning ||
        _windowsClockService.HasActiveTimer;

    private bool ShowRecordingDotWithMedia =>
        IsMusicActive && IsRecordingActivityActive && !(_voiceRecordingService?.IsCallActive ?? false);

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
        !_isIncomingCallActive &&
        ActiveRunningTasksCount >= 2;

    private int ActiveRunningTasksCount
    {
        get
        {
            int count = 0;
            if (IsMusicActive) count++;
            if (IsAnyTimerActive) count++;
            if (IsVoiceRecordingActive) count++;
            if (IsScreenRecordingActive) count++;
            return count;
        }
    }

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
        IsWifiActivityActive ||
        _isChargingAlertActive ||
        _isNotificationAlertActive ||
        _isIncomingCallActive ||
        (_isPrivacyIndicatorActive && _appStateTracker?.CurrentState != ForegroundAppState.FullScreen);

    private bool HasActiveAlert =>
        _isWifiAlertActive ||
        _isPowerModeAlertActive ||
        _isNotificationAlertActive ||
        _isIncomingCallActive ||
        _isBluetoothAlertActive ||
        _isAlarmActive ||
        _isUnlockAlertActive ||
        _isSilentRingAlertActive ||
        _isDndAlertActive ||
        _isNightModeAlertActive ||
        _isChargingAlertActive ||
        (_isPrivacyIndicatorActive && _appStateTracker?.CurrentState != ForegroundAppState.FullScreen);

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

    private double CurrentCompactHeight => CompactHeight;

    private double CurrentCompactWidth
    {
        get
        {
            double baseWidth;
            if (_isIncomingCallActive) baseWidth = CompactIncomingCallWidth;
            else if (_isAlarmActive) baseWidth = CompactAlarmWidth;
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
            else if (_isWifiAlertActive && (_isWifiConnected || _isHotspotActive)) baseWidth = CompactWifiWidth;
            else if (_isNotificationAlertActive && _currentNotification != null) baseWidth = CompactNotificationWidth;
            else if (IsSplitActive && IsMusicActive) baseWidth = CompactSplitLeadingWidth;
            else if (IsScreenRecordingActive) baseWidth = CompactScreenRecordingWidth;
            else if (IsVoiceRecordingActive && !IsMusicActive) baseWidth = CompactRecordingWidth;
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

            App.Log("Initializing ThemeService...");
            _themeService = new ThemeService();
            _themeService.ThemeChanged += OnThemeChanged;
            ApplyThemeResources();

            App.Log("Initializing AppStateTracker...");
            _appStateTracker = new AppStateTracker();
            _appStateTracker.StateChanged += OnForegroundAppStateChanged;

            App.Log("Initializing MediaManager & VolumeManager...");
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
            _wifiService = new WifiActivityService();
            _windowsClockService = new WindowsClockWatcherService();
            _voiceRecordingService = new VoiceRecordingService();
            _screenRecordingService = new ScreenRecordingService();

            App.Log("Hooking service events...");
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
            _nightLightService.NightModeChanged += OnNightModeChanged;
            _alarmService.AlarmStateChanged += OnAlarmStateChanged;
            _alarmService.SnoozeStarted += OnAlarmSnoozeStarted;
            _bluetoothService.DeviceConnected += OnBluetoothDeviceConnected;
            _bluetoothService.DeviceDisconnected += OnBluetoothDeviceDisconnected;
            _wifiService.StateChanged += OnWifiActivityStateChanged;
            _powerModeService = new PowerModeWatcherService();
            _powerModeService.PowerModeChanged += OnPowerModeChanged;
            _privacyService = new PrivacyIndicatorService();
            _privacyService.PrivacyStateChanged += OnPrivacyStateChanged;
            _windowsClockService.TimerStateChanged += OnWindowsClockTimerChanged;
            _voiceRecordingService.RecordingStateChanged += OnVoiceRecordingStateChanged;
            _voiceRecordingService.RecordingStateChanged += info => _privacyService.SetWasapiMicState(info.IsRecording);
            _screenRecordingService.RecordingStateChanged += OnScreenRecordingStateChanged;

            // Launch heavy background watchers in parallel so window renders instantly without delay
            Task.Run(() =>
            {
                try
                {
                    _focusAssistService.Start();
                    _nightLightService.Start();
                    _bluetoothService.Start();
                    _powerModeService.Start();
                    _privacyService.Start();
                    _windowsClockService.Start();
                    _voiceRecordingService.Start();
                    _screenRecordingService.Start();
                    SetupBluetoothTestTriggerWatcher();
                    App.Log("All background services started asynchronously in parallel.");
                }
                catch (Exception ex)
                {
                    App.Log($"Error starting background services: {ex.Message}");
                }
            });

            App.Log("Initializing LyricsService...");
            _lyricsService = new LyricsService(Dispatcher);
            _lyricsService.ActiveLineChanged += OnLyricsActiveLineChanged;
            _lyricsService.LyricsAvailabilityChanged += OnLyricsAvailabilityChanged;
            _lyricsService.SetPlaybackProviders(
                () => _mediaManager?.GetLivePosition() ?? TimeSpan.Zero,
                () => _mediaManager?.CurrentTrack?.IsPlaying ?? false
            );

            App.Log("All services started in constructor.");

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

            _incomingCallAlertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30.0) // Standard ringing timeout before auto-dismiss
            };
            _incomingCallAlertTimer.Tick += (s, e) =>
            {
                _incomingCallAlertTimer.Stop();
                DismissIncomingCall(userAction: false);
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

            _chargingAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(5.0)
            };
            _chargingAlertTimer.Tick += (s, e) =>
            {
                _chargingAlertTimer.Stop();
                if (_isChargingAlertActive)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isChargingAlertActive = false;
                        CompactChargingView.BeginAnimation(OpacityProperty, null);
                        CompactChargingView.Opacity = 1.0;
                        CompactChargingView.Visibility = Visibility.Collapsed;

                        // Restore shadow effect from charging glow
                        IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
                        IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
                        IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;

                        UpdateCompactViewVisibility();
                        if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
                        {
                            HideDockSmooth();
                        }
                    };
                    CompactChargingView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _unlockAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _unlockAlertTimer.Tick += (s, e) =>
            {
                _unlockAlertTimer.Stop();
                if (_isUnlockAlertActive)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isUnlockAlertActive = false;
                        CompactUnlockView.BeginAnimation(OpacityProperty, null);
                        CompactUnlockView.Opacity = 1.0;
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

            _silentRingAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _silentRingAlertTimer.Tick += (s, e) =>
            {
                _silentRingAlertTimer.Stop();
                if (_isSilentRingAlertActive)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isSilentRingAlertActive = false;
                        CompactSilentRingView.BeginAnimation(OpacityProperty, null);
                        CompactSilentRingView.Opacity = 1.0;
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

            _dndAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _dndAlertTimer.Tick += (s, e) =>
            {
                _dndAlertTimer.Stop();
                if (_isDndAlertActive)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isDndAlertActive = false;
                        CompactDndView.BeginAnimation(OpacityProperty, null);
                        CompactDndView.Opacity = 1.0;
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

            _nightModeAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(4.0)
            };
            _nightModeAlertTimer.Tick += (s, e) =>
            {
                _nightModeAlertTimer.Stop();
                if (_isNightModeAlertActive)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isNightModeAlertActive = false;
                        CompactNightModeView.BeginAnimation(OpacityProperty, null);
                        CompactNightModeView.Opacity = 1.0;
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

            _bluetoothAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(5.0)
            };
            _bluetoothAlertTimer.Tick += (s, e) =>
            {
                _bluetoothAlertTimer.Stop();
                if (_isBluetoothAlertActive && !_isExpanded)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isBluetoothAlertActive = false;
                        _activeBluetoothDevice = null;
                        CompactBluetoothView.BeginAnimation(OpacityProperty, null);
                        CompactBluetoothView.Opacity = 1.0;
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

            _powerModeAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(3.0)
            };
            _powerModeAlertTimer.Tick += (s, e) =>
            {
                _powerModeAlertTimer.Stop();
                if (_isPowerModeAlertActive)
                {
                    var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
                    fadeOut.Completed += (s2, e2) =>
                    {
                        _isPowerModeAlertActive = false;
                        CompactPowerModeView.BeginAnimation(OpacityProperty, null);
                        CompactPowerModeView.Opacity = 1.0;
                        CompactPowerModeView.Visibility = Visibility.Collapsed;
                        // Restore island shadow
                        IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
                        IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
                        IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;
                        UpdateCompactViewVisibility();
                    };
                    CompactPowerModeView.BeginAnimation(OpacityProperty, fadeOut);
                }
            };

            _dragDropFeedbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.0)
            };
            _dragDropFeedbackTimer.Tick += (s, e) =>
            {
                _dragDropFeedbackTimer.Stop();
                ResetDragDropState();
            };

            _wifiAlertTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(5.0)
            };
            _wifiAlertTimer.Tick += (s, e) =>
            {
                DismissWifiAlert();
            };

            // Listen for Windows device unlock, power mode (sleep/wake), and display settings change events
            Microsoft.Win32.SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

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
                _msgTaskbarCreated = WindowHelper.RegisterWindowMessage("TaskbarCreated");
            };
            Loaded += OnLoaded;
            Closing += (s, e) =>
            {
                if (!_isExplicitExit)
                {
                    e.Cancel = true;
                    CollapseDock();
                    App.Log("MainWindow Closing prevented - dock continues running persistently in background");
                }
                else
                {
                    App.Log($"MainWindow Closing: Cancel={e.Cancel}");
                }
            };
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
                    // In FullScreen mode, privacy indicator and dock must not appear
                    TopEdgeHotspot.Visibility = Visibility.Visible;
                    if (!IslandContainer.IsMouseOver)
                    {
                        HideDockSmooth();
                    }
                    break;

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
        // Active work (playback, recording, or a timer) keeps the notch present even
        // when the foreground window is fullscreen or the pointer leaves the dock.
        if (HasActiveContent || HasActiveAlert) return;

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
        _wifiService.Start();
        IslandContainer.Width = CurrentCompactWidth;
        IslandContainer.Height = CurrentCompactHeight;
        _volumeManager.PollVolumeState(forceNotify: true);

        // Simple recording indicator: blink once per second without scaling or glow.
        var pulse = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(500))
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
        if (_msgTaskbarCreated != 0 && (uint)msg == _msgTaskbarCreated)
        {
            App.Log("TaskbarCreated received: Explorer restarted, re-aligning dock");
            PositionWindow();
            WindowHelper.EnsureTopMost(this);
        }

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

            if (_isDragActive || (IslandContainer.Visibility == Visibility.Visible && IslandContainer.Opacity > 0.1))
            {
                Point islandPoint = IslandContainer.TranslatePoint(new Point(0, 0), this);
                // Expand hit-test boundary slightly during drag for responsive target capture
                double hitWidth = Math.Max(IslandContainer.ActualWidth, DragHoverWidth);
                double hitHeight = Math.Max(IslandContainer.ActualHeight, DragHoverHeight);
                Rect islandRect = new Rect(islandPoint.X - (hitWidth - IslandContainer.ActualWidth) / 2.0, islandPoint.Y, hitWidth, hitHeight);

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

            if (DetachedLeftContainer.Visibility == Visibility.Visible && DetachedLeftContainer.Opacity > 0.1)
            {
                Point leftBubblePoint = DetachedLeftContainer.TranslatePoint(new Point(0, 0), this);
                Rect leftBubbleRect = new Rect(leftBubblePoint.X, leftBubblePoint.Y, DetachedLeftContainer.ActualWidth, DetachedLeftContainer.ActualHeight);

                if (leftBubbleRect.Contains(windowPoint))
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

        if (_activeCategory == IslandCategory.LiveLyrics)
        {
            return;
        }

        // User explicit requirement: "no need to expand for sound recording, timer, screen recording... it should just show in the notch..."
        // If music is active in the notch, allow expanding music player
        if (!IsMusicActive && (IsScreenRecordingActive || IsVoiceRecordingActive || IsAnyTimerActive || IsWifiActivityActive))
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
        if (_activeCategory == IslandCategory.LiveLyrics)
        {
            // Keep showing live lyrics, do not auto collapse
            return;
        }

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
            if (IsScreenRecordingActive || IsVoiceRecordingActive || IsAnyTimerActive || IsWifiActivityActive)
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
        if (IsFeedbackSurfaceActive) return;
        if (_isExpanded) return;

        // User explicit requirement: "no need to expand for sound recording, timer, screen recording... it should just show in the notch..."
        if (preferredCategory == IslandCategory.ScreenRecording ||
            preferredCategory == IslandCategory.VoiceRecording ||
            preferredCategory == IslandCategory.FocusTimer)
        {
            return;
        }

        if (!IsMusicActive && !_isNotificationAlertActive && (IsScreenRecordingActive || IsVoiceRecordingActive || IsAnyTimerActive))
        {
            return;
        }

        if (!HasActiveContent && !_isNotificationAlertActive) return;
        _isExpanded = true;

        if (preferredCategory.HasValue)
        {
            _activeCategory = preferredCategory.Value;
        }
        else if (_isIncomingCallActive)
        {
            _activeCategory = IslandCategory.IncomingCall;
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
            ? 86.0
            : _activeCategory == IslandCategory.IncomingCall
                ? 82.0
                : _activeCategory == IslandCategory.BluetoothDevice
                    ? ExpandedBluetoothHeight
                    : _activeCategory == IslandCategory.LiveLyrics
                        ? ExpandedLyricsHeight
                        : ExpandedHeight;

        double targetWidth = _activeCategory == IslandCategory.BluetoothDevice
            ? ExpandedBluetoothWidth
            : _activeCategory == IslandCategory.IncomingCall
                ? 440.0
                : _activeCategory == IslandCategory.LiveLyrics
                    ? ExpandedLyricsWidth
                    : ExpandedWidth;

        DetachedBubbleContainer.Visibility = Visibility.Collapsed;
        DetachedLeftContainer.Visibility = Visibility.Collapsed;

        AnimateIsland(targetWidth, targetHeight);

        CompactView.BeginAnimation(OpacityProperty, null);
        CompactView.Opacity = 0.0;
        CompactView.Visibility = Visibility.Collapsed;

        ExpandedContainer.Visibility = Visibility.Visible;
        ShowActiveCategoryView();
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
        ExpandedContainer.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void CollapseDock()
    {
        if (IsFeedbackSurfaceActive) return;
        if (!_isExpanded) return;
        _isExpanded = false;

        if (_activeCategory == IslandCategory.BluetoothDevice)
        {
            _isBluetoothAlertActive = false;
            _activeBluetoothDevice = null;
        }
        else if (_activeCategory == IslandCategory.LiveLyrics)
        {
            _activeCategory = IslandCategory.MediaPlayback;
            UpdateLyricsIconState();
        }

        // Restore non-activating window mode when collapsing
        WindowHelper.SetActivationEnabled(this, false);

        AnimateIsland(CurrentCompactWidth, CurrentCompactHeight);

        // Restore shadow effect in case charging green pulse was active
        IslandShadowEffect.Color = _themeService.IsDarkTheme ? Colors.Black : Color.FromArgb(255, 60, 60, 70);
        IslandShadowEffect.Opacity = _themeService.IsDarkTheme ? 0.55 : 0.25;
        IslandShadowEffect.BlurRadius = _themeService.IsDarkTheme ? 22 : 18;

        ExpandedContainer.BeginAnimation(OpacityProperty, null);
        ExpandedContainer.Opacity = 0.0;
        ExpandedContainer.Visibility = Visibility.Collapsed;

        CompactView.Visibility = Visibility.Visible;
        UpdateCompactViewVisibility();
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
        CompactView.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void AnimateIsland(double targetWidth, double targetHeight)
    {
        // Copy/share feedback owns both the dock surface and its geometry. Background
        // services can keep reporting state, but cannot resize or morph the dock until
        // the feedback timeout restores the normal presentation.
        if (IsFeedbackSurfaceActive)
        {
            return;
        }

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
            bool showTimerOnRight = IsAnyTimerActive && (IsMusicActive || IsRecordingActivityActive);
            bool showRecordingOnLeft = false;
            // Position detached satellite capsules smoothly on either side of the notch:
            // Right Pill (e.g. Timer): (mainIslandTargetWidth / 2) + DetachedBubbleGap + (RightPillWidth / 2)
            // Left Pill (e.g. Call / Recording): -((mainIslandTargetWidth / 2) + DetachedBubbleGap + (LeftPillWidth / 2))
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(220);

            if (showTimerOnRight)
            {
                double rightPillW = DetachedBubbleContainer.ActualWidth > 0 ? DetachedBubbleContainer.ActualWidth : DetachedBubbleWidth;
                double rightX = (mainIslandTargetWidth / 2.0) + DetachedBubbleGap + (rightPillW / 2.0);
                if (double.IsNaN(_lastDetachedRightTargetX) || Math.Abs(_lastDetachedRightTargetX - rightX) > 0.5)
                {
                    var rightAnim = new DoubleAnimation { To = rightX, Duration = dur, EasingFunction = ease };
                    DetachedRightTranslate.BeginAnimation(TranslateTransform.XProperty, rightAnim);
                    _lastDetachedRightTargetX = rightX;
                }

                if (DetachedBubbleContainer.Visibility != Visibility.Visible || DetachedBubbleContainer.Opacity < 0.1)
                {
                    DetachedBubbleContainer.Visibility = Visibility.Visible;
                    var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
                    DetachedBubbleContainer.BeginAnimation(OpacityProperty, fadeIn);
                }
            }

            // Left Pill: If 3 tasks are running, show a detached capsule on the left side of the notch as well
            if (showRecordingOnLeft)
            {
                double leftPillW = DetachedLeftContainer.ActualWidth > 0 ? DetachedLeftContainer.ActualWidth : DetachedBubbleWidth;
                double leftX = -((mainIslandTargetWidth / 2.0) + DetachedBubbleGap + (leftPillW / 2.0));

                if (double.IsNaN(_lastDetachedLeftTargetX) || Math.Abs(_lastDetachedLeftTargetX - leftX) > 0.5)
                {
                    var leftAnim = new DoubleAnimation
                    {
                        To = leftX,
                        Duration = dur,
                        EasingFunction = ease
                    };
                    DetachedLeftTranslate.BeginAnimation(TranslateTransform.XProperty, leftAnim);
                    _lastDetachedLeftTargetX = leftX;
                }

                UpdateDetachedLeftContent();

                if (DetachedLeftContainer.Visibility != Visibility.Visible || DetachedLeftContainer.Opacity < 0.1)
                {
                    DetachedLeftContainer.Visibility = Visibility.Visible;
                    var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = ease
                    };
                    DetachedLeftContainer.BeginAnimation(OpacityProperty, fadeIn);
                }
            }
            else
            {
                if (DetachedLeftContainer.Visibility == Visibility.Visible)
                {
                    var fadeOut = new DoubleAnimation(DetachedLeftContainer.Opacity, 0.0, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseIn }
                    };
                    fadeOut.Completed += (s, e) =>
                    {
                        DetachedLeftContainer.Visibility = Visibility.Collapsed;
                    };
                    DetachedLeftContainer.BeginAnimation(OpacityProperty, fadeOut);
                }
            }

            if (!showTimerOnRight)
            {
                _lastDetachedRightTargetX = double.NaN;
                if (DetachedBubbleContainer.Visibility == Visibility.Visible)
                {
                    var fadeOut = new DoubleAnimation(DetachedBubbleContainer.Opacity, 0.0, TimeSpan.FromMilliseconds(150));
                    fadeOut.Completed += (s, e) => DetachedBubbleContainer.Visibility = Visibility.Collapsed;
                    DetachedBubbleContainer.BeginAnimation(OpacityProperty, fadeOut);
                }
                else
                {
                    DetachedBubbleContainer.Visibility = Visibility.Collapsed;
                }
            }
        }
        else
        {
            _lastDetachedRightTargetX = double.NaN;
            _lastDetachedLeftTargetX = double.NaN;
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

            if (DetachedLeftContainer.Visibility == Visibility.Visible && DetachedLeftContainer.Opacity > 0.0)
            {
                var fadeOut = new DoubleAnimation(DetachedLeftContainer.Opacity, 0.0, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseIn }
                };
                fadeOut.Completed += (s, e) =>
                {
                    DetachedLeftContainer.Visibility = Visibility.Collapsed;
                };
                DetachedLeftContainer.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                DetachedLeftContainer.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateDetachedLeftContent()
    {
        if (IsVoiceRecordingActive)
        {
            bool isCall = _voiceRecordingService?.IsCallActive ?? false;
            DetachedLeftMicIcon.Visibility = isCall ? Visibility.Collapsed : Visibility.Visible;
            DetachedLeftCallIcon.Visibility = isCall ? Visibility.Visible : Visibility.Collapsed;
            DetachedLeftTimerIcon.Visibility = Visibility.Collapsed;

            DetachedLeftTimerText.Foreground = isCall ? CallGreenBrush : RecRedBrush;
            DetachedLeftTimerText.Text = isCall ? "call" : "rec";
        }
        else if (IsAnyTimerActive)
        {
            DetachedLeftMicIcon.Visibility = Visibility.Collapsed;
            DetachedLeftCallIcon.Visibility = Visibility.Collapsed;
            DetachedLeftTimerIcon.Visibility = Visibility.Visible;

            DetachedLeftTimerText.Foreground = Brushes.White;
            DetachedLeftTimerText.Text = DetachedTimerCountdownText.Text;
        }
    }

    public void SwitchCategory(IslandCategory cat)
    {
        _activeCategory = cat;
        ShowActiveCategoryView();
    }

    private void ShowActiveCategoryView()
    {
        if (IsFeedbackSurfaceActive) return;
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
        ViewBluetoothDevice.Visibility = _activeCategory == IslandCategory.BluetoothDevice ? Visibility.Visible : Visibility.Collapsed;
        ViewIncomingCall.Visibility = _activeCategory == IslandCategory.IncomingCall ? Visibility.Visible : Visibility.Collapsed;
        ViewLiveLyrics.Visibility = _activeCategory == IslandCategory.LiveLyrics ? Visibility.Visible : Visibility.Collapsed;

        if (_isExpanded)
        {
            double targetHeight = _activeCategory == IslandCategory.AppNotification
                ? 86.0
                : _activeCategory == IslandCategory.IncomingCall
                    ? 82.0
                    : _activeCategory == IslandCategory.BluetoothDevice
                        ? ExpandedBluetoothHeight
                        : _activeCategory == IslandCategory.LiveLyrics
                            ? ExpandedLyricsHeight
                            : ExpandedHeight;
            double targetWidth = _activeCategory == IslandCategory.BluetoothDevice
                ? ExpandedBluetoothWidth
                : _activeCategory == IslandCategory.IncomingCall
                    ? 440.0
                    : _activeCategory == IslandCategory.LiveLyrics
                        ? ExpandedLyricsWidth
                        : ExpandedWidth;
            DetachedBubbleContainer.Visibility = Visibility.Collapsed;
            DetachedLeftContainer.Visibility = Visibility.Collapsed;

            AnimateIsland(targetWidth, targetHeight);
        }
    }

    private void OnTrackChanged(MediaTrackInfo track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track.HasMedia)
            {
                if (!_isNotificationAlertActive && _activeCategory != IslandCategory.AppNotification && _activeCategory != IslandCategory.LiveLyrics)
                {
                    _activeCategory = IslandCategory.MediaPlayback;
                }

                if (track.IsDetectedBrowser)
                {
                    ExpandedLyricsButton.Visibility = Visibility.Collapsed;
                    if (_activeCategory == IslandCategory.LiveLyrics)
                    {
                        _activeCategory = IslandCategory.MediaPlayback;
                        ShowActiveCategoryView();
                        AnimateIsland(ExpandedWidth, ExpandedHeight);
                        UpdateLyricsIconState();
                    }
                }

                _lyricsService.LoadTrack(track.Artist, track.Title, track.Duration, track.IsPlaying, track.IsDetectedBrowser);

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
                if (_isExpanded && (_activeCategory == IslandCategory.MediaPlayback || _activeCategory == IslandCategory.LiveLyrics))
                {
                    CollapseDock();
                }

                _lyricsService.LoadTrack(string.Empty, string.Empty, TimeSpan.Zero, false, false);
                ExpandedLyricsButton.Visibility = Visibility.Collapsed;

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
        _silentRingAlertTimer.Stop();

        ClearTemporaryAlerts();
        _isSilentRingAlertActive = true;

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

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        UpdateCompactViewVisibility();

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactSilentRingView.BeginAnimation(OpacityProperty, fadeIn);

        _silentRingAlertTimer.Start();
    }

    public void ShowDndAnimation(FocusAssistState state)
    {
        _dndAlertTimer.Stop();

        ClearTemporaryAlerts();
        _isDndAlertActive = true;

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

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        UpdateCompactViewVisibility();

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
        _nightModeAlertTimer.Stop();

        ClearTemporaryAlerts();
        _isNightModeAlertActive = true;

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

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        UpdateCompactViewVisibility();

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
            string cleanTitle = UwpResourceResolver.Clean(alarmTitle, "Alarm");
            CompactAlarmTitleText.Text = string.IsNullOrWhiteSpace(cleanTitle) ? "Alarm" : cleanTitle;

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

    private void CompactAlarmView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        uint? nid = _alarmService.ActiveNotificationId;
        _alarmService.Dismiss();
        if (nid.HasValue)
        {
            _notificationService.Dismiss(nid.Value);
        }
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

    private void ClearTemporaryAlerts(bool keepPowerMode = false, bool keepWifi = false)
    {
        if (_isUnlockAlertActive)
        {
            _isUnlockAlertActive = false;
            _unlockAlertTimer.Stop();
            CompactUnlockView.BeginAnimation(OpacityProperty, null);
            CompactUnlockView.Opacity = 1.0;
            CompactUnlockView.Visibility = Visibility.Collapsed;
        }
        if (_isSilentRingAlertActive)
        {
            _isSilentRingAlertActive = false;
            _silentRingAlertTimer.Stop();
            CompactSilentRingView.BeginAnimation(OpacityProperty, null);
            CompactSilentRingView.Opacity = 1.0;
            CompactSilentRingView.Visibility = Visibility.Collapsed;
        }
        if (_isDndAlertActive)
        {
            _isDndAlertActive = false;
            _dndAlertTimer.Stop();
            CompactDndView.BeginAnimation(OpacityProperty, null);
            CompactDndView.Opacity = 1.0;
            CompactDndView.Visibility = Visibility.Collapsed;
        }
        if (_isNightModeAlertActive)
        {
            _isNightModeAlertActive = false;
            _nightModeAlertTimer.Stop();
            CompactNightModeView.BeginAnimation(OpacityProperty, null);
            CompactNightModeView.Opacity = 1.0;
            CompactNightModeView.Visibility = Visibility.Collapsed;
        }
        if (_isChargingAlertActive)
        {
            _isChargingAlertActive = false;
            _chargingAlertTimer.Stop();
            CompactChargingView.BeginAnimation(OpacityProperty, null);
            CompactChargingView.Opacity = 1.0;
            CompactChargingView.Visibility = Visibility.Collapsed;
        }
        if (_isBluetoothAlertActive)
        {
            _isBluetoothAlertActive = false;
            _bluetoothAlertTimer.Stop();
            _activeBluetoothDevice = null;
            CompactBluetoothView.BeginAnimation(OpacityProperty, null);
            CompactBluetoothView.Opacity = 1.0;
            CompactBluetoothView.Visibility = Visibility.Collapsed;
        }
        if (_isNotificationAlertActive)
        {
            _isNotificationAlertActive = false;
            _notificationAlertTimer.Stop();
            CompactNotificationView.BeginAnimation(OpacityProperty, null);
            CompactNotificationView.Opacity = 1.0;
            CompactNotificationView.Visibility = Visibility.Collapsed;
        }
        if (!keepPowerMode)
        {
            _isPowerModeAlertActive = false;
            _powerModeAlertTimer.Stop();
            CompactPowerModeView.BeginAnimation(OpacityProperty, null);
            CompactPowerModeView.Opacity = 1.0;
            CompactPowerModeView.Visibility = Visibility.Collapsed;
        }
        if (!keepWifi)
        {
            _isWifiAlertActive = false;
            _wifiAlertTimer.Stop();
            CompactWifiView.BeginAnimation(OpacityProperty, null);
            CompactWifiView.Opacity = 1.0;
            CompactWifiView.Visibility = Visibility.Collapsed;
        }
    }

    public void TriggerWifiConnectedAlert(string networkName, bool isHotspot)
    {
        App.Log($"TriggerWifiConnectedAlert: Network='{networkName}', Hotspot={isHotspot}");

        // Clear other alerts but keep this Wi-Fi alert active
        ClearTemporaryAlerts(keepWifi: true);
        _isWifiAlertActive = true;

        CompactWifiStatusText.Text = networkName;
        CompactWifiIcon.Visibility = isHotspot ? Visibility.Collapsed : Visibility.Visible;
        CompactHotspotIcon.Visibility = isHotspot ? Visibility.Visible : Visibility.Collapsed;

        if (_isExpanded)
        {
            _isExpanded = false;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Opacity = 1.0;
        }

        UpdateCompactViewVisibility();

        CompactWifiView.BeginAnimation(OpacityProperty, null);
        CompactWifiView.Opacity = 0.0;
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactWifiView.BeginAnimation(OpacityProperty, fadeIn);

        // Strict 5-second auto-dismiss timer
        _wifiAlertTimer.Stop();
        _wifiAlertTimer.Interval = TimeSpan.FromSeconds(5.0);
        _wifiAlertTimer.Start();

        ShowDockSmooth();
        WindowHelper.EnsureTopMost(this);
    }

    public void DismissWifiAlert()
    {
        _wifiAlertTimer.Stop();
        if (!_isWifiAlertActive && CompactWifiView.Visibility == Visibility.Collapsed) return;

        App.Log("DismissWifiAlert: 5s alert elapsed; fading out Wi-Fi notch view.");
        _isWifiAlertActive = false;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (s, e) =>
        {
            CompactWifiView.BeginAnimation(OpacityProperty, null);
            CompactWifiView.Opacity = 1.0;
            CompactWifiView.Visibility = Visibility.Collapsed;
            UpdateCompactViewVisibility();

            if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
            {
                HideDockSmooth();
            }
        };
        CompactWifiView.BeginAnimation(OpacityProperty, fadeOut);
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
        CompactPowerModeLeafIcon.Visibility = (mode == WindowsPowerMode.BestPowerEfficiency) ? Visibility.Visible : Visibility.Collapsed;
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
    private FileSystemWatcher? _testTriggerWatcher;
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

            _testTriggerWatcher = new FileSystemWatcher(dir, "test_trigger.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _testTriggerWatcher.Changed += (s, e) => HandleTriggerFile(e.FullPath);
            _testTriggerWatcher.Created += (s, e) => HandleTriggerFile(e.FullPath);
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

            if (name.Equals("dnd", StringComparison.OrdinalIgnoreCase) || name.Equals("dnd_on", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ShowDndAnimation(FocusAssistState.PriorityOnly));
                return;
            }
            if (name.Equals("dnd_off", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ShowDndAnimation(FocusAssistState.Off));
                return;
            }
            if (name.Equals("wifi", StringComparison.OrdinalIgnoreCase))
            {
                string net = parts.Length > 1 ? parts[1].Trim() : "Jio_5";
                Dispatcher.Invoke(() => TriggerWifiConnectedAlert(net, false));
                return;
            }
            if (name.Equals("hotspot", StringComparison.OrdinalIgnoreCase))
            {
                string net = parts.Length > 1 ? parts[1].Trim() : "iPhone Hotspot";
                Dispatcher.Invoke(() => TriggerWifiConnectedAlert(net, true));
                return;
            }
            if (name.Equals("power_save", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("low_power", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("power_efficiency", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ShowPowerModeAlert(WindowsPowerMode.BestPowerEfficiency));
                return;
            }
            if (name.Equals("power_balanced", StringComparison.OrdinalIgnoreCase) || name.Equals("balanced", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ShowPowerModeAlert(WindowsPowerMode.Balanced));
                return;
            }
            if (name.Equals("power_perf", StringComparison.OrdinalIgnoreCase) || name.Equals("power_performance", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(() => ShowPowerModeAlert(WindowsPowerMode.BestPerformance));
                return;
            }
            if (name.Equals("lyrics", StringComparison.OrdinalIgnoreCase) || name.Equals("music", StringComparison.OrdinalIgnoreCase))
            {
                string artist = parts.Length > 1 ? parts[1].Trim() : "Alec Benjamin";
                string title = parts.Length > 2 ? parts[2].Trim() : "Let Me Down Slowly";
                int posSec = parts.Length > 3 && int.TryParse(parts[3].Trim(), out int p) ? p : 45;
                string app = parts.Length > 4 ? parts[4].Trim() : "Media Player";

                Dispatcher.Invoke(() =>
                {
                    _mediaManager.SimulateTrack(artist, title, TimeSpan.FromSeconds(posSec), null, app);
                    if (name.Equals("lyrics", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_activeCategory != IslandCategory.LiveLyrics)
                        {
                            ToggleLiveLyrics();
                        }
                    }
                });
                return;
            }
            if (name.Equals("lyrics_toggle", StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.Invoke(ToggleLiveLyrics);
                return;
            }
            if (name.Equals("lyrics_lead", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length > 1 && double.TryParse(parts[1].Trim(), out double leadMs))
                {
                    _lyricsService.SetLeadTime(leadMs);
                }
                return;
            }
            if (name.Equals("notif", StringComparison.OrdinalIgnoreCase) || name.Equals("notification", StringComparison.OrdinalIgnoreCase))
            {
                string app = parts.Length > 1 ? parts[1].Trim() : "Blinkit";
                string title = parts.Length > 2 ? parts[2].Trim() : app;
                string msg = parts.Length > 3 ? parts[3].Trim().Replace("\\n", "\n") : "Steal deals at Winter Sale 👇 • ✨ GO24 Steel Insulated Coffee Mug @ ₹̵8̵4̵9̵ ₹649\n✨ Femora Tea Cup Set @ ₹̵8̵5̵0̵ ₹729";
                Dispatcher.Invoke(() =>
                {
                    var notif = new IslandNotification
                    {
                        AppName = app,
                        Title = title,
                        Message = msg,
                        ReceivedTime = DateTime.Now
                    };
                    OnNotificationReceived(notif);
                });
                return;
            }

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
    private static BitmapImage? _cachedBluetoothLogo;

    private static ImageSource? GetBluetoothDeviceIcon(BluetoothDeviceKind kind)
    {
        try
        {
            if (kind == BluetoothDeviceKind.Smartphone)
            {
                _cachedPhoneIcon ??= new BitmapImage(new Uri("pack://application:,,,/Resources/mobile_icon.png", UriKind.Absolute));
                return _cachedPhoneIcon;
            }
            else if (kind == BluetoothDeviceKind.AudioHeadset)
            {
                _cachedHeadsetIcon ??= new BitmapImage(new Uri("pack://application:,,,/Resources/headphones_icon.png", UriKind.Absolute));
                return _cachedHeadsetIcon;
            }
            else
            {
                _cachedBluetoothLogo ??= new BitmapImage(new Uri("pack://application:,,,/Resources/bluetooth_logo.png", UriKind.Absolute));
                return _cachedBluetoothLogo;
            }
        }
        catch
        {
            try
            {
                string relPath = kind switch
                {
                    BluetoothDeviceKind.Smartphone => "Resources/mobile_icon.png",
                    BluetoothDeviceKind.AudioHeadset => "Resources/headphones_icon.png",
                    _ => "Resources/bluetooth_logo.png"
                };
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

    private void OnWifiActivityStateChanged(WifiActivityState state)
    {
        Dispatcher.Invoke(() =>
        {
            bool wasActive = _isWifiConnected || _isHotspotActive;
            bool nowActive = state.IsWifiConnected || state.IsHotspotActive;

            // Detect if this is a fresh connection event:
            // 1) Wi-Fi transitioned from disconnected to connected, OR connected to a new/different Wi-Fi SSID
            // 2) Hotspot transitioned from off to on
            bool isNewWifiConnection = state.IsWifiConnected && (!wasActive || !string.Equals(_lastWifiNetworkName, state.WifiNetworkName, StringComparison.OrdinalIgnoreCase));
            bool isNewHotspotActivation = state.IsHotspotActive && !_isHotspotActive;
            bool shouldTriggerAlert = isNewWifiConnection || isNewHotspotActivation;

            _isWifiConnected = state.IsWifiConnected;
            _isHotspotActive = state.IsHotspotActive;
            _lastWifiNetworkName = state.WifiNetworkName;

            CompactWifiIcon.Visibility = state.IsHotspotActive ? Visibility.Collapsed : Visibility.Visible;
            CompactHotspotIcon.Visibility = state.IsHotspotActive ? Visibility.Visible : Visibility.Collapsed;

            string networkTooltip = state.IsHotspotActive
                ? state.HotspotClientCount > 0
                    ? $"Hotspot device: {state.HotspotClientName ?? "Device connected"}" +
                      (string.IsNullOrWhiteSpace(state.WifiNetworkName) ? string.Empty : $"\nWi-Fi: {state.WifiNetworkName}")
                    : "Windows mobile hotspot is active"
                : state.IsWifiConnected && !string.IsNullOrWhiteSpace(state.WifiNetworkName)
                    ? $"Connected to {state.WifiNetworkName}"
                    : "Connected to Wi-Fi";

            if (state.IsHotspotActive && state.HotspotClientCount > 0)
            {
                string connectedName = string.IsNullOrWhiteSpace(state.HotspotClientName)
                    ? "Device connected"
                    : state.HotspotClientName;
                int additionalDevices = state.HotspotClientCount - 1;
                CompactWifiStatusText.Text = additionalDevices > 0
                    ? $"{connectedName} +{additionalDevices}"
                    : connectedName;
            }
            else if (state.IsWifiConnected && !string.IsNullOrWhiteSpace(state.WifiNetworkName))
            {
                CompactWifiStatusText.Text = state.WifiNetworkName;
            }
            else
            {
                CompactWifiStatusText.Text = state.IsHotspotActive ? "Hotspot On" : "Wi-Fi connected";
            }

            CompactWifiView.ToolTip = networkTooltip;

            if (!_hasObservedInitialWifiState)
            {
                _hasObservedInitialWifiState = true;
                if (nowActive)
                {
                    TriggerWifiConnectedAlert(CompactWifiStatusText.Text, state.IsHotspotActive);
                }
            }
            else if (shouldTriggerAlert)
            {
                TriggerWifiConnectedAlert(CompactWifiStatusText.Text, state.IsHotspotActive);
            }
            else if (!nowActive)
            {
                DismissWifiAlert();
            }

            foreach (WifiNetworkGlyph glyph in new[]
                     { MediaWifiStatusGlyph, TimerWifiStatusGlyph, VoiceWifiStatusGlyph, ScreenRecordingWifiStatusGlyph })
            {
                glyph.IsHotspot = state.IsHotspotActive;
                glyph.ToolTip = networkTooltip;
                glyph.Visibility = IsWifiActivityActive ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateCompactViewVisibility();
        });
    }

    private void SetWifiActivityAnimation(bool active)
    {
        if (_isWifiAnimationActive == active) return;
        _isWifiAnimationActive = active;

        UIElement[] signalParts =
        {
            CompactWifiIcon,
            CompactHotspotIcon,
            MediaWifiStatusGlyph,
            TimerWifiStatusGlyph,
            VoiceWifiStatusGlyph,
            ScreenRecordingWifiStatusGlyph
        };

        if (!active)
        {
            foreach (UIElement part in signalParts)
            {
                if (part == null) continue;
                try
                {
                    part.BeginAnimation(OpacityProperty, null);
                    part.Opacity = 1.0;
                }
                catch { }
            }
            return;
        }

        TimeSpan[] offsets =
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(140),
            TimeSpan.FromMilliseconds(280),
            TimeSpan.FromMilliseconds(420)
        };

        for (int i = 0; i < signalParts.Length; i++)
        {
            var part = signalParts[i];
            if (part == null) continue;

            try
            {
                part.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.35,
                    TimeSpan.FromMilliseconds(520))
                {
                    BeginTime = offsets[i % offsets.Length],
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
            catch (Exception ex)
            {
                App.Log($"SetWifiActivityAnimation error on index {i}: {ex.Message}");
            }
        }
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

            // Set device icon (Smartphone, Headphones, or Bluetooth Logo)
            var iconSource = GetBluetoothDeviceIcon(dev.DeviceKind);
            CompactBluetoothIcon.Source = iconSource;
            ExpandedBluetoothIcon.Source = iconSource;

            // Populate compact glimpse UI (Bluetooth/device icon + device name)
            CompactBluetoothDeviceName.Text = dev.Name;
            CompactBluetoothDeviceName.Visibility = Visibility.Visible;

            // Populate expanded UI (Bluetooth/device icon + "Connected" + "[Device] ready to use" + Battery Cell)
            ExpandedBluetoothTitleText.Text = $"{dev.Name} ready to use";
            UpdateBluetoothBatteryCell(dev.BatteryPercent);

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

    private void UpdateBluetoothBatteryCell(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        string percentStr = $"{clamped}%";
        ExpandedBluetoothBatteryPercentText.Text = percentStr;
        CompactBluetoothBatteryPercentText.Text = percentStr;

        // Dynamic battery colors: Red (<=10%), Amber (<=20%), Green (>20%)
        SolidColorBrush fillBrush;
        if (clamped <= 10)
        {
            fillBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        }
        else if (clamped <= 20)
        {
            fillBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
        }
        else
        {
            fillBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        }

        ExpandedBluetoothBatteryPercentText.Foreground = fillBrush;
        ExpandedBluetoothBatteryFill.Background = fillBrush;
        ExpandedBluetoothBatteryFill.Width = Math.Max(2.5, (clamped / 100.0) * 22.0);

        CompactBluetoothBatteryPercentText.Foreground = fillBrush;
        CompactBluetoothBatteryFill.Background = fillBrush;
        CompactBluetoothBatteryFill.Width = Math.Max(2.0, (clamped / 100.0) * 18.0);
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
        App.Log($"SystemEvents_SessionSwitch: Reason={e.Reason}");
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshOnWake();
                TriggerUnlockAnimation();
            });

            Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(RefreshOnWake));
            Task.Delay(1200).ContinueWith(_ => Dispatcher.Invoke(RefreshOnWake));
        }
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
        {
            Dispatcher.Invoke(() =>
            {
                TriggerLockAnimation();
            });
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        App.Log($"SystemEvents_PowerModeChanged: Mode={e.Mode}");
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
        {
            Dispatcher.Invoke(RefreshOnWake);

            // Staggered post-wake re-checks to guarantee proper positioning as display drivers wake up
            Task.Delay(400).ContinueWith(_ => Dispatcher.Invoke(RefreshOnWake));
            Task.Delay(1200).ContinueWith(_ => Dispatcher.Invoke(RefreshOnWake));
        }
    }

    private void RefreshOnWake()
    {
        App.Log("Waking from sleep / power resume: refreshing layout, window position and services");
        PositionWindow();
        WindowHelper.EnsureTopMost(this);
        UpdateClock();
        OnPowerStatusChanged();
        UpdateNightModePillVisibility();
        UpdateCompactViewVisibility();
        if (IslandContainer.Visibility != Visibility.Visible || IslandContainer.Opacity < 0.1)
        {
            ShowDockSmooth();
        }
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            App.Log("DisplaySettingsChanged: re-aligning dock to primary monitor");
            PositionWindow();
            WindowHelper.EnsureTopMost(this);
        });
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

        // Reset shackle transforms to initial closed/locked position (Locked state from image)
        LockShackleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        LockShackleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        LockShackleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LockShackleTranslate.X = 0;
        LockShackleTranslate.Y = 0;
        LockShackleRotate.Angle = 0;

        // Animate island container width to 210.0 (compact height 36.0)
        AnimateIsland(CompactUnlockWidth, CompactHeight);

        // Smooth fade-in of the unlock view in fully locked state
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactUnlockView.BeginAnimation(OpacityProperty, fadeIn);

        // Transition from Locked to Unlocked:
        // 1. Shackle lifts cleanly up out of the padlock body
        var liftAnim = new DoubleAnimation
        {
            From = 0.0,
            To = -4.5,
            Duration = TimeSpan.FromMilliseconds(220),
            BeginTime = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // 2. Shackle swings/rotates smoothly outward into open unlocked position
        var rotateAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 42.0,
            Duration = TimeSpan.FromMilliseconds(280),
            BeginTime = TimeSpan.FromMilliseconds(320),
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
        };

        LockShackleTranslate.BeginAnimation(TranslateTransform.YProperty, liftAnim);
        LockShackleRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

        // Stay on screen for 2.8 seconds, then fade out smoothly
        _unlockAlertTimer.Interval = TimeSpan.FromSeconds(2.8);
        _unlockAlertTimer.Start();
    }

    public void TriggerLockAnimation()
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

        // Hide other compact views and activate lock view
        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactChargingView.Visibility = Visibility.Collapsed;
        CompactUnlockView.Opacity = 0.0;
        CompactUnlockView.Visibility = Visibility.Visible;

        // Start shackle in open unlocked position (unlocked arch)
        LockShackleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        LockShackleRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        LockShackleTranslate.Y = -4.5;
        LockShackleRotate.Angle = 42.0;

        // Animate island container width to 210.0
        AnimateIsland(CompactUnlockWidth, CompactHeight);

        // Smooth fade-in
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(160));
        CompactUnlockView.BeginAnimation(OpacityProperty, fadeIn);

        // Transition from Unlocked to Locked:
        // 1. Shackle rotates back from open (42 deg) to straight (0 deg)
        var rotateCloseAnim = new DoubleAnimation
        {
            From = 42.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            BeginTime = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        // 2. Shackle drops securely into the lock body (-4.5 to 0)
        var dropAnim = new DoubleAnimation
        {
            From = -4.5,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(160),
            BeginTime = TimeSpan.FromMilliseconds(420),
            EasingFunction = new BounceEase { Bounces = 1, Bounciness = 2, EasingMode = EasingMode.EaseOut }
        };

        LockShackleRotate.BeginAnimation(RotateTransform.AngleProperty, rotateCloseAnim);
        LockShackleTranslate.BeginAnimation(TranslateTransform.YProperty, dropAnim);

        _unlockAlertTimer.Interval = TimeSpan.FromSeconds(2.5);
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
            FocusCountdownText.Text = $"{FormatRemainingTime(_focusService.Remaining)} remaining";
            FocusToggleButton.Content = _focusService.IsRunning ? "Pause" : "Resume";
            UpdateTimerPillCountdown();

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
                string formattedTimer = FormatRemainingTime(info.RemainingTime);
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

            UpdateTimerPillCountdown();
            UpdateCompactViewVisibility();
        });
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        int totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{totalSeconds / 60}:{seconds:D2}";
    }

    private void UpdateTimerPillCountdown()
    {
        TimeSpan? remaining = null;
        var clockTimer = _windowsClockService.ActiveTimer;
        if (_windowsClockService.HasActiveTimer && clockTimer != null)
        {
            remaining = clockTimer.RemainingTime;
        }
        else if (_focusService.IsRunning && _focusService.Remaining > TimeSpan.Zero)
        {
            remaining = _focusService.Remaining;
        }

        if (remaining.HasValue)
        {
            string formatted = FormatRemainingTime(remaining.Value);
            CompactLiveTimerCountdownText.Text = formatted;
            DetachedTimerCountdownText.Text = formatted;
        }
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

    private static readonly SolidColorBrush CallGreenBrush = new(Color.FromRgb(0x30, 0xD1, 0x58)); // #30D158 Vibrant Call Green
    private static readonly SolidColorBrush RecRedBrush = new(Color.FromRgb(0xFF, 0x45, 0x3A));   // #FF453A Vibrant Recording Red

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

                // User Requirement: The green telephone icon & waveform bars should ONLY be shown during calls.
                // When sound/voice/screen recording is on, show red colored bar and recording icon.
                bool isCall = info.IsCall;
                var themeBrush = isCall ? CallGreenBrush : RecRedBrush;

                CompactCallIcon.Visibility = isCall ? Visibility.Visible : Visibility.Collapsed;
                CompactRecordingMicIcon.Visibility = isCall ? Visibility.Collapsed : Visibility.Visible;
                CompactRecordingDurationText.Foreground = themeBrush;

                // Update all waveform bars color dynamically
                Border[] bars = { RecBar1, RecBar2, RecBar3, RecBar4, RecBar5, RecBar6, RecBar7, RecBar8, RecBar9, RecBar10, RecBar11, RecBar12, RecBar13 };
                foreach (var bar in bars)
                {
                    bar.Background = themeBrush;
                }

                StartRecordingWaveformAnimation();
        if (IsSplitActive && !IsScreenRecordingActive)
        {
            UpdateDetachedLeftContent();
        }
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
            if (!info.IsRecording)
            {
                if (_isExpanded && _activeCategory == IslandCategory.ScreenRecording) CollapseDock();
            }

            UpdateSplitBubbleAnimation(CurrentCompactWidth);
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
            _activeCategory = IslandCategory.LiveClipboard;
            // A clipboard change can be raised by our own drag-and-drop copy operation.
            // Keep the drop confirmation as the sole dock content until its timeout ends.
            if (_isDropFeedbackActive)
            {
                return;
            }

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

        if (!isClock) return false;

        // If it's a timer notification, it belongs to the timer notification flow, NOT the alarm ringer
        if (title.Contains("Timer", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Timer", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("TimerDone", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
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

        // Apple Dynamic Island Incoming Call Banner
        if (notif.IsIncomingCall)
        {
            Dispatcher.Invoke(() =>
            {
                ShowIncomingCall(notif);
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

        // Fix mismatch/clash: Filter out Windows system power & energy saver toast notifications
        // because PowerModeWatcherService handles power mode transitions in its dedicated compact pill
        if (notif.AppName.Contains("Power & Battery", StringComparison.OrdinalIgnoreCase) ||
            notif.AppName.Contains("Energy saver", StringComparison.OrdinalIgnoreCase) ||
            notif.Title.Contains("Energy saver", StringComparison.OrdinalIgnoreCase) ||
            notif.Title.Contains("Battery saver", StringComparison.OrdinalIgnoreCase) ||
            notif.Message.Contains("Energy saver is on", StringComparison.OrdinalIgnoreCase) ||
            notif.Message.Contains("Energy saver is off", StringComparison.OrdinalIgnoreCase))
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

            // If an alarm is ringing and its notification was dismissed from Windows Action Center or banner, dismiss the alarm in dock
            if (_isAlarmActive && _alarmService.ActiveNotificationId.HasValue)
            {
                uint alarmNotifId = _alarmService.ActiveNotificationId.Value;
                bool stillPresent = _notificationService.ActiveNotifications.Any(n => n.Id == alarmNotifId);
                if (!stillPresent)
                {
                    App.Log($"Alarm notification {alarmNotifId} is no longer present in Windows notifications; dismissing dock alarm.");
                    _alarmService.Dismiss();
                }
            }

            UpdateCompactViewVisibility();
        });
    }

    private void CollapseAllCompactViews()
    {
        CompactClockView.Visibility = Visibility.Collapsed;
        CompactMediaView.Visibility = Visibility.Collapsed;
        CompactChargingView.Visibility = Visibility.Collapsed;
        CompactNotificationView.Visibility = Visibility.Collapsed;
        CompactUnlockView.Visibility = Visibility.Collapsed;
        CompactSilentRingView.Visibility = Visibility.Collapsed;
        CompactDndView.Visibility = Visibility.Collapsed;
        CompactLiveTimerView.Visibility = Visibility.Collapsed;
        CompactVoiceRecordingView.Visibility = Visibility.Collapsed;
        CompactIncomingCallView.Visibility = Visibility.Collapsed;
        CompactScreenRecordingView.Visibility = Visibility.Collapsed;
        CompactNightModeView.Visibility = Visibility.Collapsed;
        CompactPowerModeView.Visibility = Visibility.Collapsed;
        CompactAlarmView.Visibility = Visibility.Collapsed;
        CompactBluetoothView.Visibility = Visibility.Collapsed;
        CompactWifiView.Visibility = Visibility.Collapsed;
        SplitLeadingMediaView.Visibility = Visibility.Collapsed;
        CompactNightModePill.Visibility = Visibility.Collapsed;
        CompactChargingPill.Visibility = Visibility.Collapsed;
    }

    private void UpdateCompactViewVisibility()
    {
        if (_isExpanded)
        {
            SetWifiActivityAnimation(false);
            CompactView.Visibility = Visibility.Collapsed;
            DetachedBubbleContainer.Visibility = Visibility.Collapsed;
            DetachedLeftContainer.Visibility = Visibility.Collapsed;
            return;
        }

        if (_isDragActive || _isDropFeedbackActive || _dragDropFeedbackTimer.IsEnabled)
        {
            SetWifiActivityAnimation(false);
            CompactView.Visibility = Visibility.Collapsed;
            ExpandedContainer.Visibility = Visibility.Collapsed;
            DetachedBubbleContainer.Visibility = Visibility.Collapsed;
            DetachedLeftContainer.Visibility = Visibility.Collapsed;
            return;
        }

        // Recover from stale expanded/animation visibility state. A background
        // activity should always have a visible compact presentation when collapsed.
        CompactView.Visibility = Visibility.Visible;
        ExpandedContainer.Visibility = Visibility.Collapsed;
        if (HasActiveContent &&
            (IslandContainer.Visibility != Visibility.Visible || IslandContainer.Opacity < 0.99))
        {
            ShowDockSmooth();
        }

        bool showRecordingDotWithMedia = ShowRecordingDotWithMedia;
        MediaPrivacyPanel.Visibility = _isPrivacyIndicatorActive || showRecordingDotWithMedia
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (showRecordingDotWithMedia)
        {
            CompactRecordingActivityDot.Visibility = Visibility.Visible;
            if (!CompactRecordingActivityDot.HasAnimatedProperties)
            {
                CompactRecordingActivityDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.15,
                    TimeSpan.FromMilliseconds(ActivityIndicatorBlinkIntervalMs / 2.0))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
        }
        else
        {
            CompactRecordingActivityDot.BeginAnimation(OpacityProperty, null);
            CompactRecordingActivityDot.Opacity = 1.0;
            CompactRecordingActivityDot.Visibility = Visibility.Collapsed;
        }

        if (!IsSplitActive || HasActiveAlert)
        {
            DetachedBubbleContainer.Visibility = Visibility.Collapsed;
            DetachedLeftContainer.Visibility = Visibility.Collapsed;
        }

        // 1. Collapse all compact subviews first to guarantee ZERO overlapping views
        CollapseAllCompactViews();

        // 2. Activate ONLY the single active compact view by Apple Dynamic Island priority
        if (_isIncomingCallActive)
        {
            CompactIncomingCallView.Visibility = Visibility.Visible;
        }
        else if (_isAlarmActive)
        {
            CompactAlarmView.Visibility = Visibility.Visible;
        }
        else if (_isUnlockAlertActive)
        {
            CompactUnlockView.Visibility = Visibility.Visible;
        }
        else if (_isSilentRingAlertActive)
        {
            CompactSilentRingView.Visibility = Visibility.Visible;
        }
        else if (_isDndAlertActive)
        {
            CompactDndView.Visibility = Visibility.Visible;
        }
        else if (_isNightModeAlertActive)
        {
            CompactNightModeView.Visibility = Visibility.Visible;
        }
        else if (_isChargingAlertActive)
        {
            CompactChargingView.Visibility = Visibility.Visible;
        }
        else if (_isBluetoothAlertActive)
        {
            CompactBluetoothView.Visibility = Visibility.Visible;
        }
        else if (_isPowerModeAlertActive)
        {
            CompactPowerModeView.Visibility = Visibility.Visible;
        }
        else if (_isWifiAlertActive && (_isWifiConnected || _isHotspotActive))
        {
            CompactWifiView.Visibility = Visibility.Visible;
        }
        else if (_isNotificationAlertActive && _currentNotification != null)
        {
            CompactNotificationView.Visibility = Visibility.Visible;
        }
        else if (IsSplitActive)
        {
            SplitLeadingMediaView.Visibility = Visibility.Visible;
        }
        else if (IsScreenRecordingActive)
        {
            CompactScreenRecordingView.Visibility = Visibility.Visible;
        }
        else if (IsVoiceRecordingActive)
        {
            CompactVoiceRecordingView.Visibility = Visibility.Visible;
        }
        else if (IsAnyTimerActive)
        {
            CompactLiveTimerView.Visibility = Visibility.Visible;
        }
        else if (IsMusicActive)
        {
            CompactMediaView.Visibility = Visibility.Visible;
        }
        else
        {
            // Empty notch / idle dock: all subviews remain collapsed
        }

        SetWifiActivityAnimation(IsWifiActivityActive);
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

    #region Incoming Call Notifications (Apple Dynamic Island)

    public void ShowIncomingCall(IslandNotification notif)
    {
        _currentIncomingCall = notif;
        _isIncomingCallActive = true;

        ClearTemporaryAlerts();

        // Update Compact incoming call UI
        CompactIncomingCallerText.Text = string.IsNullOrWhiteSpace(notif.Title) ? notif.AppName : notif.Title;

        // Update Expanded incoming call UI
        IncomingCallerNameText.Text = string.IsNullOrWhiteSpace(notif.Title) ? notif.AppName : notif.Title;
        IncomingCallAppNameText.Text = $"{notif.AppName} Audio Call";

        if (notif.AppLogo != null)
        {
            IncomingCallerAvatarImage.Source = notif.AppLogo;
            IncomingCallerAvatarImage.Visibility = Visibility.Visible;
            IncomingCallerAvatarBorder.Visibility = Visibility.Visible;
            IncomingCallAppIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            IncomingCallerAvatarImage.Source = null;
            IncomingCallerAvatarImage.Visibility = Visibility.Collapsed;
            IncomingCallerAvatarBorder.Visibility = Visibility.Collapsed;
            IncomingCallAppIcon.Visibility = Visibility.Visible;
            IncomingCallAppIcon.SetNotification(notif);
        }

        // Ringing animation on compact phone icon
        var ringAnim = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(450))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        CompactIncomingPhoneIcon.BeginAnimation(OpacityProperty, ringAnim);

        ShowDockSmooth();
        WindowHelper.EnsureTopMost(this);

        // Auto-expand into Apple Dynamic Island Incoming Call Card
        _activeCategory = IslandCategory.IncomingCall;
        if (_isExpanded)
        {
            ShowActiveCategoryView();
        }
        else
        {
            ExpandDock();
        }

        // Standard 30s ringing timeout
        _incomingCallAlertTimer.Stop();
        _incomingCallAlertTimer.Interval = TimeSpan.FromSeconds(30.0);
        _incomingCallAlertTimer.Start();
    }

    private void DeclineCall_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DismissIncomingCall(userAction: true);
    }

    private void AcceptCall_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _incomingCallAlertTimer.Stop();
        CompactIncomingPhoneIcon.BeginAnimation(OpacityProperty, null);
        CompactIncomingPhoneIcon.Opacity = 1.0;

        uint? notifId = _currentIncomingCall?.Id;
        string callerName = _currentIncomingCall?.Title ?? "Call";
        string appName = _currentIncomingCall?.AppName ?? "Phone";

        _isIncomingCallActive = false;
        _currentIncomingCall = null;

        if (notifId.HasValue)
        {
            _notificationService.Dismiss(notifId.Value);
        }

        // Transition smoothly to active call state:
        // Set voice recording service simulated call state or show ongoing call pill
        _voiceRecordingService.SimulateCall(callerName, appName);

        if (_isExpanded)
        {
            CollapseDock();
        }
        else
        {
            UpdateCompactViewVisibility();
        }
    }

    public void DismissIncomingCall(bool userAction = false)
    {
        _incomingCallAlertTimer.Stop();
        CompactIncomingPhoneIcon.BeginAnimation(OpacityProperty, null);
        CompactIncomingPhoneIcon.Opacity = 1.0;

        if (_currentIncomingCall != null)
        {
            _notificationService.Dismiss(_currentIncomingCall.Id);
            _currentIncomingCall = null;
        }

        _isIncomingCallActive = false;

        if (_isExpanded && _activeCategory == IslandCategory.IncomingCall)
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
    }

    private void SimulateTestIncomingCall()
    {
        var testNotif = new IslandNotification
        {
            Id = 999999,
            AppName = "WhatsApp",
            Title = "Aryan Das",
            Message = "Incoming voice call...",
            ReceivedTime = DateTime.Now
        };
        ShowIncomingCall(testNotif);
    }

    #endregion

    private void Start15MinTimer_Click(object sender, RoutedEventArgs e)
    {
        _focusService.Start(5);
        SwitchCategory(IslandCategory.FocusTimer);
    }

    private void DetachedLeftContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCollapseTimer.Stop();
    }

    private void DetachedLeftContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isExpanded && !_isPinned && !_isDraggingSlider)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Start();
        }
    }

    private void DetachedLeftContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
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

    private void DetachedSecondaryContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCollapseTimer.Stop();
    }

    private void DetachedSecondaryContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isExpanded && !_isPinned && !_isDraggingSlider)
        {
            _autoCollapseTimer.Stop();
            _autoCollapseTimer.Start();
        }
    }

    private void DetachedSecondaryContainer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // User explicit requirement: secondary satellite remains docked / compact
    }

    #region Live Lyrics / Captions

    private void OnLyricsAvailabilityChanged(bool hasLyrics)
    {
        Dispatcher.Invoke(() =>
        {
            ExpandedLyricsButton.Visibility = hasLyrics ? Visibility.Visible : Visibility.Collapsed;
            if (!hasLyrics && _activeCategory == IslandCategory.LiveLyrics)
            {
                if (IslandContainer.IsMouseOver)
                {
                    _activeCategory = IslandCategory.MediaPlayback;
                    ShowActiveCategoryView();
                    AnimateIsland(ExpandedWidth, ExpandedHeight);
                }
                else
                {
                    CollapseDock();
                }
                UpdateLyricsIconState();
            }
        });
    }

    private void UpdateLyricsIconState()
    {
        bool isLyrics = _activeCategory == IslandCategory.LiveLyrics;
        if (ExpandedLyricsIconPath != null)
        {
            ExpandedLyricsIconPath.Fill = isLyrics
                ? (Brush)FindResource("AccentColorBrush")
                : (Brush)FindResource("TextSecondaryBrush");
        }
    }

    private bool _isLyricsLayerAActive = true;

    private void ToggleLiveLyrics()
    {
        if (!IsMusicActive || !_lyricsService.HasLyrics) return;

        if (_activeCategory == IslandCategory.LiveLyrics)
        {
            // Toggle back to media player if mouse is over, or collapse
            if (IslandContainer.IsMouseOver)
            {
                _activeCategory = IslandCategory.MediaPlayback;
                ShowActiveCategoryView();
                AnimateIsland(ExpandedWidth, ExpandedHeight);
            }
            else
            {
                CollapseDock();
            }
            UpdateLyricsIconState();
        }
        else
        {
            _activeCategory = IslandCategory.LiveLyrics;
            _isExpanded = true;
            _autoCollapseTimer.Stop();

            _lyricsService.StartTracking();

            CompactView.BeginAnimation(OpacityProperty, null);
            CompactView.Opacity = 0.0;
            CompactView.Visibility = Visibility.Collapsed;

            ExpandedContainer.Visibility = Visibility.Visible;
            ShowActiveCategoryView();
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150));
            ExpandedContainer.BeginAnimation(OpacityProperty, fadeIn);

            AnimateIsland(ExpandedLyricsWidth, ExpandedLyricsHeight);
            UpdateLyricsIconState();

            // Reset layer transforms and force immediate line render
            _isLyricsLayerAActive = true;
            LyricsLayerA.BeginAnimation(OpacityProperty, null);
            LyricsLayerB.BeginAnimation(OpacityProperty, null);
            LyricsTransA.BeginAnimation(TranslateTransform.YProperty, null);
            LyricsTransB.BeginAnimation(TranslateTransform.YProperty, null);
            LyricsLayerA.Visibility = Visibility.Visible;
            LyricsLayerA.Opacity = 1.0;
            LyricsTransA.Y = 0;
            LyricsLayerB.Visibility = Visibility.Collapsed;
            LyricsLayerB.Opacity = 0.0;
            LyricsTransB.Y = 0;

            _lyricsService.ForceRefresh();
        }
    }

    private void OnLyricsActiveLineChanged(string prev, string curr, string next, string nextPlusOne, int newIdx, int oldIdx)
    {
        Dispatcher.Invoke(() =>
        {
            string cleanCurr = string.IsNullOrWhiteSpace(curr) ? "♪ Music ♪" : curr;

            if (_activeCategory != IslandCategory.LiveLyrics || ViewLiveLyrics.Visibility != Visibility.Visible || oldIdx == -2)
            {
                // Immediate update without animation
                var actPrev = _isLyricsLayerAActive ? LyricsA_Prev : LyricsB_Prev;
                var actCurr = _isLyricsLayerAActive ? LyricsA_Curr : LyricsB_Curr;
                var actNext = _isLyricsLayerAActive ? LyricsA_Next : LyricsB_Next;
                if (actPrev != null) actPrev.Text = prev;
                if (actCurr != null) actCurr.Text = cleanCurr;
                if (actNext != null) actNext.Text = next;
                return;
            }

            var incomingLayer = _isLyricsLayerAActive ? LyricsLayerB : LyricsLayerA;
            var outgoingLayer = _isLyricsLayerAActive ? LyricsLayerA : LyricsLayerB;
            var incomingTrans = _isLyricsLayerAActive ? LyricsTransB : LyricsTransA;
            var outgoingTrans = _isLyricsLayerAActive ? LyricsTransA : LyricsTransB;
            var incomingScale = _isLyricsLayerAActive ? LyricsB_Scale : LyricsA_Scale;

            var inPrev = _isLyricsLayerAActive ? LyricsB_Prev : LyricsA_Prev;
            var inCurr = _isLyricsLayerAActive ? LyricsB_Curr : LyricsA_Curr;
            var inNext = _isLyricsLayerAActive ? LyricsB_Next : LyricsA_Next;

            if (inPrev != null) inPrev.Text = prev;
            if (inCurr != null) inCurr.Text = cleanCurr;
            if (inNext != null) inNext.Text = next;

            incomingLayer.Visibility = Visibility.Visible;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(260);

            if (newIdx == oldIdx + 1)
            {
                // Natural forward roll: lines glide smoothly UPWARDS
                var outSlide = new DoubleAnimation(0.0, -18.0, dur) { EasingFunction = ease };
                var outFade = new DoubleAnimation(1.0, 0.0, dur) { EasingFunction = ease };

                var inSlide = new DoubleAnimation(18.0, 0.0, dur) { EasingFunction = ease };
                var inFade = new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease };
                var inScaleAnim = new DoubleAnimation(0.95, 1.0, dur) { EasingFunction = ease };

                outgoingTrans.BeginAnimation(TranslateTransform.YProperty, outSlide);
                outgoingLayer.BeginAnimation(OpacityProperty, outFade);

                incomingTrans.BeginAnimation(TranslateTransform.YProperty, inSlide);
                incomingLayer.BeginAnimation(OpacityProperty, inFade);
                incomingScale?.BeginAnimation(ScaleTransform.ScaleXProperty, inScaleAnim);
                incomingScale?.BeginAnimation(ScaleTransform.ScaleYProperty, inScaleAnim);
            }
            else if (newIdx == oldIdx - 1)
            {
                // Backward roll: lines glide smoothly DOWNWARDS
                var outSlide = new DoubleAnimation(0.0, 18.0, dur) { EasingFunction = ease };
                var outFade = new DoubleAnimation(1.0, 0.0, dur) { EasingFunction = ease };

                var inSlide = new DoubleAnimation(-18.0, 0.0, dur) { EasingFunction = ease };
                var inFade = new DoubleAnimation(0.0, 1.0, dur) { EasingFunction = ease };

                outgoingTrans.BeginAnimation(TranslateTransform.YProperty, outSlide);
                outgoingLayer.BeginAnimation(OpacityProperty, outFade);

                incomingTrans.BeginAnimation(TranslateTransform.YProperty, inSlide);
                incomingLayer.BeginAnimation(OpacityProperty, inFade);
            }
            else
            {
                // Seek or skip: gentle cross-fade
                var crossDur = TimeSpan.FromMilliseconds(160);
                var outFade = new DoubleAnimation(1.0, 0.0, crossDur) { EasingFunction = ease };
                var inFade = new DoubleAnimation(0.0, 1.0, crossDur) { EasingFunction = ease };

                outgoingTrans.BeginAnimation(TranslateTransform.YProperty, null);
                outgoingTrans.Y = 0;
                incomingTrans.BeginAnimation(TranslateTransform.YProperty, null);
                incomingTrans.Y = 0;

                outgoingLayer.BeginAnimation(OpacityProperty, outFade);
                incomingLayer.BeginAnimation(OpacityProperty, inFade);
            }

            _isLyricsLayerAActive = !_isLyricsLayerAActive;
        });
    }

    private void ExpandedLyricsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLiveLyrics();
    }

    private void ViewLiveLyrics_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            ToggleLiveLyrics();
        }
    }

    #endregion

    private void OnPrivacyStateChanged(bool isCameraInUse, bool isMicInUse)
    {
        Dispatcher.Invoke(() =>
        {
            if (isCameraInUse || isMicInUse)
            {
                // In FullScreen mode, privacy indicator and dock must not appear
                if (_appStateTracker.CurrentState != ForegroundAppState.FullScreen)
                {
                    ShowDockSmooth();
                    WindowHelper.EnsureTopMost(this);
                }
            }
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
        MediaPrivacyPanel.Visibility = anyActive || ShowRecordingDotWithMedia
            ? Visibility.Visible
            : Visibility.Collapsed;
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
                            !_isChargingAlertActive && !_isNotificationAlertActive && !_isIncomingCallActive && !_isUnlockAlertActive &&
                            !_isSilentRingAlertActive && !_isDndAlertActive && !_isNightModeAlertActive && !_isWifiAlertActive &&
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
            if (_appStateTracker.CurrentState != ForegroundAppState.Normal && !HasActiveAlert && !IslandContainer.IsMouseOver)
            {
                HideDockSmooth();
            }
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
            else if (e.Key == Key.I)
            {
                // Test incoming call simulation
                SimulateTestIncomingCall();
                e.Handled = true;
            }
            else if (e.Key == Key.W)
            {
                // Test Wi-Fi connected alert (5s auto-dismiss)
                TriggerWifiConnectedAlert(_lastWifiNetworkName ?? "Jio_5", false);
                e.Handled = true;
            }
            else if (e.Key == Key.D)
            {
                // Test DND alert
                ShowDndAnimation(FocusAssistState.PriorityOnly);
                e.Handled = true;
            }
        }
    }

    #region Open Clipboard Drag-and-Drop Interaction

    private const double DragHoverWidth = 300.0;
    private const double DragHoverHeight = 44.0;

    private void IslandContainer_PreviewDragEnter(object sender, DragEventArgs e)
    {
        App.Log($"DragEnter: DataFormats={string.Join(", ", e.Data.GetFormats())}");
        e.Handled = true;
        _isDragActive = true;
        _isDropFeedbackActive = false;
        _dragDropFeedbackTimer.Stop();
        _isExpanded = false;
        CompactView.BeginAnimation(OpacityProperty, null);
        ExpandedContainer.BeginAnimation(OpacityProperty, null);
        CompactView.Visibility = Visibility.Collapsed;
        ExpandedContainer.Visibility = Visibility.Collapsed;
        DetachedBubbleContainer.Visibility = Visibility.Collapsed;
        DetachedLeftContainer.Visibility = Visibility.Collapsed;
        IslandContainer.BeginAnimation(WidthProperty, null);
        IslandContainer.BeginAnimation(HeightProperty, null);
        IslandContainer.Width = DragHoverWidth;
        IslandContainer.Height = DragHoverHeight;

        // Switch synchronously to the drag surface so no expanded/compact content can
        // render beneath the success indicator during the WPF drop event sequence.
        DragDropFeedbackOverlay.Visibility = Visibility.Visible;
        DragDropFeedbackOverlay.Opacity = 1.0;
        DragCopiedSuccessIndicator.Visibility = Visibility.Collapsed;
        DragShareSuccessIndicator.Visibility = Visibility.Collapsed;

        UpdateDragHoverIndicators(e.GetPosition(IslandContainer));
        e.Effects = DragDropEffects.Copy;
    }

    private void IslandContainer_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        _isDragActive = true;
        UpdateDragHoverIndicators(e.GetPosition(IslandContainer));
        e.Effects = DragDropEffects.Copy;
    }

    private void UpdateDragHoverIndicators(Point pos)
    {
        double width = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : DragHoverWidth;
        double ratio = Math.Clamp(pos.X / width, 0.0, 1.0);

        if (ratio < 0.35)
        {
            // Hovering on Left Edge -> Show Green Tick + Copy
            DragCopyIndicator.Visibility = Visibility.Visible;
            DragShareIndicator.Visibility = Visibility.Collapsed;
        }
        else if (ratio > 0.65)
        {
            // Hovering on Right Edge -> Show Share Icon + Share
            DragCopyIndicator.Visibility = Visibility.Collapsed;
            DragShareIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            // Middle zone -> Dock is enlarged and blank as requested
            DragCopyIndicator.Visibility = Visibility.Collapsed;
            DragShareIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void IslandContainer_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        // DragLeave is not a reliable cancel signal: Windows raises it while a valid drag
        // crosses hit-test boundaries. Only restore the dock when WPF reports cancellation.
        if (_isDragActive && e.Action == DragAction.Cancel)
        {
            ResetDragDropState();
        }
    }

    private void IslandContainer_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        Point dropPos = e.GetPosition(IslandContainer);
        double width = IslandContainer.ActualWidth > 0 ? IslandContainer.ActualWidth : DragHoverWidth;
        double ratio = Math.Clamp(dropPos.X / width, 0.0, 1.0);
        App.Log($"PreviewDrop: ratio={ratio:F2}, Pos=({dropPos.X:F1}, {dropPos.Y:F1}), Formats={string.Join(", ", e.Data.GetFormats())}");

        DragCopyIndicator.Visibility = Visibility.Collapsed;
        DragShareIndicator.Visibility = Visibility.Collapsed;

        if (ratio < 0.35)
        {
            // Left Edge Drop -> Copy to Windows Clipboard
            BeginDropFeedback();
            ExecuteCopyDrop(e.Data);
        }
        else if (ratio > 0.65)
        {
            // Right Edge Drop -> Open Windows Share Menu
            BeginDropFeedback();
            ExecuteShareDrop(e.Data);
        }
        else
        {
            // Dropped in neutral center -> collapse back
            ResetDragDropState();
        }
    }

    private async void ExecuteCopyDrop(IDataObject data)
    {
        try
        {
            bool copied = false;
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    bool isImage = files.Length == 1 && IsImageExtension(Path.GetExtension(files[0]));
                    if (isImage && File.Exists(files[0]))
                    {
                        try
                        {
                            var bmp = new System.Windows.Media.Imaging.BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bmp.UriSource = new Uri(files[0]);
                            bmp.EndInit();
                            bmp.Freeze();

                            // Put pixels, rather than a file reference, on the standard
                            // clipboard. This lets paste targets and clipboard history
                            // receive an actual image instead of a filesystem path.
                            System.Windows.Clipboard.SetImage(bmp);

                            var imagePackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                            var imageFile = await StorageFile.GetFileFromPathAsync(files[0]);
                            imagePackage.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(imageFile));
                            Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(imagePackage,
                                new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = true });
                            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                            copied = true;
                            App.Log($"Copied image pixels to clipboard: {files[0]}");
                        }
                        catch (Exception imageEx)
                        {
                            App.Log($"Image clipboard copy error: {imageEx.Message}");
                        }
                    }
                    else
                    {
                        // Files retain their native file-drop representation.
                        var fileCollection = new System.Collections.Specialized.StringCollection();
                        fileCollection.AddRange(files);
                        var dataObj = new System.Windows.DataObject();
                        dataObj.SetFileDropList(fileCollection);
                        dataObj.SetText(string.Join(Environment.NewLine, files));
                        System.Windows.Clipboard.SetDataObject(dataObj, true);

                        try
                        {
                            var filePackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                            filePackage.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                            var items = new System.Collections.Generic.List<IStorageItem>();
                            foreach (var f in files)
                            {
                                if (File.Exists(f)) items.Add(await StorageFile.GetFileFromPathAsync(f));
                                else if (Directory.Exists(f)) items.Add(await StorageFolder.GetFolderFromPathAsync(f));
                            }
                            if (items.Count > 0)
                            {
                                filePackage.SetStorageItems(items);
                            }
                            filePackage.SetText(string.Join(Environment.NewLine, files));
                            Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(filePackage,
                                new ClipboardContentOptions { IsAllowedInHistory = true, IsRoamable = true });
                            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                        }
                        catch (Exception fileEx)
                        {
                            App.Log($"File clipboard history copy error: {fileEx.Message}");
                        }
                        copied = true;
                    }

                    App.Log($"Copied {files.Length} files to clipboard: {string.Join(", ", files)}");
                }
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                string? text = data.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    System.Windows.Clipboard.SetText(text);
                    try
                    {
                        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dp.SetText(text);
                        var options = new ClipboardContentOptions
                        {
                            IsAllowedInHistory = true,
                            IsRoamable = true
                        };
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(dp, options);
                        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                    }
                    catch { }

                    copied = true;
                    App.Log($"Copied UnicodeText to clipboard ({text.Length} chars)");
                }
            }
            else if (data.GetDataPresent(DataFormats.Text))
            {
                string? text = data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    System.Windows.Clipboard.SetText(text);
                    try
                    {
                        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dp.SetText(text);
                        var options = new ClipboardContentOptions
                        {
                            IsAllowedInHistory = true,
                            IsRoamable = true
                        };
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(dp, options);
                        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                    }
                    catch { }

                    copied = true;
                    App.Log($"Copied Text to clipboard ({text.Length} chars)");
                }
            }
            else if (data.GetDataPresent(DataFormats.Bitmap))
            {
                var bmp = data.GetData(DataFormats.Bitmap) as BitmapSource;
                if (bmp != null)
                {
                    System.Windows.Clipboard.SetImage(bmp);
                    copied = true;
                    App.Log("Copied image to clipboard");
                }
            }

            // Pop out confirmed "Copied" on its own surface, never over media controls.
            DragCopiedSuccessIndicator.Visibility = Visibility.Visible;
            DragShareSuccessIndicator.Visibility = Visibility.Collapsed;
            CompleteDropFeedback();
        }
        catch (Exception ex)
        {
            App.Log($"Error executing copy drop: {ex.Message}");
            ResetDragDropState();
        }
    }

    private void ExecuteShareDrop(IDataObject data)
    {
        try
        {
            DragShareSuccessIndicator.Visibility = Visibility.Visible;
            DragCopiedSuccessIndicator.Visibility = Visibility.Collapsed;

            // Extract file or text to share
            string? firstFile = null;
            string? textContent = null;

            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    firstFile = files[0];
                    App.Log($"Share dropped file: {firstFile}");
                }
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText))
            {
                textContent = data.GetData(DataFormats.UnicodeText) as string;
                App.Log("Share dropped text");
            }
            else if (data.GetDataPresent(DataFormats.Text))
            {
                textContent = data.GetData(DataFormats.Text) as string;
                App.Log("Share dropped text");
            }

            // Launch Windows native Share Menu
            OpenWindowsShareDialog(firstFile, textContent);
            CompleteDropFeedback();
        }
        catch (Exception ex)
        {
            App.Log($"Error executing share drop: {ex.Message}");
            ResetDragDropState();
        }
    }

    private void OpenWindowsShareDialog(string? filePath, string? textContent)
    {
        try
        {
            App.Log($"OpenWindowsShareDialog: filePath='{filePath}', textContent='{textContent}'");
            IntPtr hostHwnd = new WindowInteropHelper(this).Handle;
            if (hostHwnd == IntPtr.Zero)
            {
                App.Log("Share sheet could not open because the notch window has no handle.");
                return;
            }

            Guid dtmIid = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            IntPtr pDtm = interop.GetForWindow(hostHwnd, ref dtmIid);
            var dtm = WinRT.MarshalInterface<DataTransferManager>.FromAbi(pDtm);

            dtm.DataRequested += async (sender, args) =>
            {
                var deferral = args.Request.GetDeferral();
                try
                {
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        args.Request.Data.Properties.Title = Path.GetFileName(filePath);
                        var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                        args.Request.Data.SetStorageItems(new[] { storageFile });
                        App.Log($"Share Sheet: attached storage file '{filePath}'");
                    }
                    else if (!string.IsNullOrEmpty(textContent))
                    {
                        args.Request.Data.Properties.Title = "Shared Text";
                        args.Request.Data.SetText(textContent);
                    }
                }
                catch (Exception dataEx)
                {
                    App.Log($"Share data error: {dataEx.Message}");
                }
                finally
                {
                    deferral.Complete();
                }
            };

            WindowHelper.SetActivationEnabled(this, true);
            Activate();
            interop.ShowShareUIForWindow(hostHwnd);
            App.Log("Requested native Windows Share UI from the notch window.");
            _ = CenterNativeShareSheetAsync(hostHwnd);
        }
        catch (Exception ex)
        {
            App.Log($"Share invocation error: {ex.Message}");
        }
    }

    private static async Task CenterNativeShareSheetAsync(IntPtr shareHostHwnd)
    {
        // The Share system dialog is created asynchronously by the shell. Retry briefly
        // until its owned window exists, then center that actual window in the host's
        // monitor work area. Windows Share ignores the host's y coordinate when placing
        // the sheet, so move the created system window itself.
        for (int attempt = 0; attempt < 16; attempt++)
        {
            await Task.Delay(125);
            IntPtr shareSheet = IntPtr.Zero;
            EnumWindows((candidate, _) =>
            {
                if (!IsWindowVisible(candidate) || candidate == shareHostHwnd)
                {
                    return true;
                }

                bool isOwnedByHost = GetWindow(candidate, GwOwner) == shareHostHwnd;
                var title = new StringBuilder(64);
                GetWindowText(candidate, title, title.Capacity);
                if (isOwnedByHost || string.Equals(title.ToString(), "Share", StringComparison.OrdinalIgnoreCase))
                {
                    shareSheet = candidate;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (shareSheet == IntPtr.Zero || !GetWindowRect(shareSheet, out var shareRect))
            {
                continue;
            }

            int width = shareRect.Right - shareRect.Left;
            int height = shareRect.Bottom - shareRect.Top;
            IntPtr monitor = MonitorFromWindow(shareHostHwnd, MonitorDefaultToNearest);
            var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo)) continue;
            int hostCenterX = (monitorInfo.Work.Left + monitorInfo.Work.Right) / 2;
            int hostCenterY = (monitorInfo.Work.Top + monitorInfo.Work.Bottom) / 2;
            SetWindowPos(shareSheet, IntPtr.Zero,
                hostCenterX - (width / 2),
                hostCenterY - (height / 2),
                0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
            App.Log($"Centered native Windows Share UI on pass {attempt + 1}.");
        }

        App.Log("Finished native Windows Share UI centering passes.");
    }

    private static bool IsImageExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;
        ext = ext.ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".webp" || ext == ".gif";
    }

    private void ResetDragDropState()
    {
        _isDropFeedbackActive = false;
        _isDragActive = false;
        DragDropFeedbackOverlay.Visibility = Visibility.Collapsed;
        DragCopyIndicator.Visibility = Visibility.Collapsed;
        DragShareIndicator.Visibility = Visibility.Collapsed;
        DragCopiedSuccessIndicator.Visibility = Visibility.Collapsed;
        DragShareSuccessIndicator.Visibility = Visibility.Collapsed;

        CompactView.Visibility = Visibility.Visible;
        UpdateCompactViewVisibility();
        AnimateIsland(CurrentCompactWidth, CurrentCompactHeight);
    }

    private void BeginDropFeedback()
    {
        _isDropFeedbackActive = true;
        _isDragActive = false;
        _dragDropFeedbackTimer.Stop();
        _isExpanded = false;
        CompactView.BeginAnimation(OpacityProperty, null);
        ExpandedContainer.BeginAnimation(OpacityProperty, null);
        IslandContainer.BeginAnimation(WidthProperty, null);
        IslandContainer.BeginAnimation(HeightProperty, null);
        CompactView.Opacity = 0.0;
        ExpandedContainer.Opacity = 0.0;
        CompactView.Visibility = Visibility.Collapsed;
        ExpandedContainer.Visibility = Visibility.Collapsed;
        DetachedBubbleContainer.Visibility = Visibility.Collapsed;
        DetachedLeftContainer.Visibility = Visibility.Collapsed;
        DragDropFeedbackOverlay.Visibility = Visibility.Visible;
        DragDropFeedbackOverlay.Opacity = 1.0;
        IslandContainer.Width = DragHoverWidth;
        IslandContainer.Height = DragHoverHeight;
        DragCopyIndicator.Visibility = Visibility.Collapsed;
        DragShareIndicator.Visibility = Visibility.Collapsed;
        DragCopiedSuccessIndicator.Visibility = Visibility.Collapsed;
        DragShareSuccessIndicator.Visibility = Visibility.Collapsed;
    }

    private void CompleteDropFeedback()
    {
        _isDropFeedbackActive = true;
        _isDragActive = false;
        _isExpanded = false;
        CompactView.BeginAnimation(OpacityProperty, null);
        ExpandedContainer.BeginAnimation(OpacityProperty, null);
        CompactView.Opacity = 0.0;
        ExpandedContainer.Opacity = 0.0;
        CompactView.Visibility = Visibility.Collapsed;
        ExpandedContainer.Visibility = Visibility.Collapsed;
        DragDropFeedbackOverlay.Visibility = Visibility.Visible;
        DragDropFeedbackOverlay.Opacity = 1.0;
        _dragDropFeedbackTimer.Stop();
        _dragDropFeedbackTimer.Start();
    }

    #endregion
}
