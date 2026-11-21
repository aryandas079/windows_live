using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WindowsLive.Models;

namespace WindowsLive.Services;

public class MediaManager
{
    public event Action<MediaTrackInfo>? TrackChanged;

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly DispatcherTimer _timelineTimer;
    private readonly Dispatcher _dispatcher;

    private MediaTrackInfo _currentTrack = new();
    private bool _isDemoMode = false;
    private readonly System.Diagnostics.Stopwatch _positionStopwatch = new();
    private TimeSpan _lastReportedPosition = TimeSpan.Zero;
    private DateTimeOffset _lastUpdatedTime = DateTimeOffset.MinValue;

    public MediaTrackInfo CurrentTrack => _currentTrack;

    public MediaManager(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        _timelineTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timelineTimer.Tick += OnTimelineTick;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                await RefreshSessionAsync();
            }
        }
        catch
        {
            // If session manager is not available, leave HasMedia = false
            _currentTrack = new MediaTrackInfo { HasMedia = false };
            _dispatcher.Invoke(() => TrackChanged?.Invoke(_currentTrack));
        }

        _timelineTimer.Start();
    }

    public void EnableDemoMode()
    {
        SimulateTrack("Windows Live Studio", "Starry Horizon", TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(24));
    }

    public void SimulateTrack(string artist, string title, TimeSpan? position = null, TimeSpan? duration = null, string? sourceApp = null)
    {
        _isDemoMode = true;
        string app = sourceApp ?? "Media Player";
        bool isBrowser = app.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                         app.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                         app.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
                         app.Contains("Brave", StringComparison.OrdinalIgnoreCase) ||
                         app.Contains("Browser", StringComparison.OrdinalIgnoreCase);

        _currentTrack = new MediaTrackInfo
        {
            Title = title,
            Artist = artist,
            AlbumTitle = "Album",
            SourceApp = app,
            IsBrowser = isBrowser,
            IsPlaying = true,
            Position = position ?? TimeSpan.FromSeconds(25),
            Duration = duration ?? TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(20),
            HasMedia = true
        };
        _dispatcher.Invoke(() => TrackChanged?.Invoke(_currentTrack));
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        await _dispatcher.InvokeAsync(async () =>
        {
            await RefreshSessionAsync();
        });
    }

    private async Task RefreshSessionAsync()
    {
        if (_sessionManager == null) return;

        try
        {
            var session = _sessionManager.GetCurrentSession();
            if (session != null)
            {
                _isDemoMode = false;
                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }

                _currentSession = session;
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

                await UpdateTrackFromSessionAsync();
            }
            else
            {
                if (!_isDemoMode)
                {
                    if (_currentSession != null)
                    {
                        _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                        _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                        _currentSession = null;
                    }
                    _currentTrack = new MediaTrackInfo { HasMedia = false, IsPlaying = false };
                    _dispatcher.Invoke(() => TrackChanged?.Invoke(_currentTrack));
                }
            }
        }
        catch
        {
            if (!_isDemoMode)
            {
                _currentTrack = new MediaTrackInfo { HasMedia = false, IsPlaying = false };
                _dispatcher.Invoke(() => TrackChanged?.Invoke(_currentTrack));
            }
        }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        await _dispatcher.InvokeAsync(async () =>
        {
            await UpdateTrackFromSessionAsync();
        });
    }

    private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        await _dispatcher.InvokeAsync(async () =>
        {
            await UpdateTrackFromSessionAsync();
        });
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _dispatcher.Invoke(() =>
        {
            UpdateTimeline();
        });
    }

    private async Task UpdateTrackFromSessionAsync()
    {
        if (_currentSession == null) return;

        try
        {
            var props = await _currentSession.TryGetMediaPropertiesAsync();
            var playbackInfo = _currentSession.GetPlaybackInfo();
            var timeline = _currentSession.GetTimelineProperties();

            BitmapImage? thumbnailImage = null;
            if (props?.Thumbnail != null)
            {
                thumbnailImage = await LoadThumbnailAsync(props.Thumbnail);
            }

            // If thumbnail was not ready yet, retry once after a brief delay
            if (thumbnailImage == null && props != null)
            {
                await Task.Delay(200);
                var retryProps = await _currentSession.TryGetMediaPropertiesAsync();
                if (retryProps?.Thumbnail != null)
                {
                    thumbnailImage = await LoadThumbnailAsync(retryProps.Thumbnail);
                }
            }

            string rawSource = _currentSession.SourceAppUserModelId ?? "Media Player";
            string source = rawSource;
            bool isBrowser = false;

            if (rawSource.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) source = "Spotify";
            else if (rawSource.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) source = "Apple Music";
            else if (rawSource.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) { source = "Chrome"; isBrowser = true; }
            else if (rawSource.Contains("Edge", StringComparison.OrdinalIgnoreCase) || rawSource.Contains("msedge", StringComparison.OrdinalIgnoreCase)) { source = "Edge"; isBrowser = true; }
            else if (rawSource.Contains("Firefox", StringComparison.OrdinalIgnoreCase) || rawSource.Contains("Mozilla", StringComparison.OrdinalIgnoreCase)) { source = "Firefox"; isBrowser = true; }
            else if (rawSource.Contains("Brave", StringComparison.OrdinalIgnoreCase)) { source = "Brave"; isBrowser = true; }
            else if (rawSource.Contains("Opera", StringComparison.OrdinalIgnoreCase)) { source = "Opera"; isBrowser = true; }
            else if (rawSource.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)) { source = "Vivaldi"; isBrowser = true; }
            else if (rawSource.Contains("Arc", StringComparison.OrdinalIgnoreCase)) { source = "Arc"; isBrowser = true; }
            else if (rawSource.Contains("Browser", StringComparison.OrdinalIgnoreCase) || rawSource.Contains("Chromium", StringComparison.OrdinalIgnoreCase)) { source = "Browser"; isBrowser = true; }
            else if (rawSource.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase) || rawSource.Contains("Media", StringComparison.OrdinalIgnoreCase)) source = "Media Player";

            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool hasTitle = !string.IsNullOrWhiteSpace(props?.Title);

            SynchronizeTimeline(
                timeline?.Position ?? TimeSpan.Zero,
                timeline?.LastUpdatedTime ?? DateTimeOffset.UtcNow,
                (timeline?.EndTime ?? TimeSpan.Zero) == TimeSpan.Zero ? TimeSpan.FromMinutes(3) : timeline!.EndTime,
                isPlaying
            );

            _currentTrack = new MediaTrackInfo
            {
                Title = hasTitle ? props!.Title : (isPlaying ? "Now Playing" : string.Empty),
                Artist = props?.Artist ?? string.Empty,
                AlbumTitle = props?.AlbumTitle ?? string.Empty,
                SourceApp = source,
                IsBrowser = isBrowser,
                Thumbnail = thumbnailImage,
                IsPlaying = isPlaying,
                Position = GetLivePosition(),
                Duration = (timeline?.EndTime ?? TimeSpan.Zero) == TimeSpan.Zero ? TimeSpan.FromMinutes(3) : timeline!.EndTime,
                HasMedia = hasTitle || isPlaying
            };

            TrackChanged?.Invoke(_currentTrack);
        }
        catch
        {
            // Keep current
        }
    }

    private void SynchronizeTimeline(TimeSpan reportedPosition, DateTimeOffset lastUpdatedTime, TimeSpan duration, bool isPlaying)
    {
        _lastReportedPosition = reportedPosition;
        _lastUpdatedTime = lastUpdatedTime;
        _positionStopwatch.Restart();

        TimeSpan livePos = reportedPosition;
        if (isPlaying && lastUpdatedTime > DateTimeOffset.MinValue)
        {
            var elapsed = DateTimeOffset.UtcNow - lastUpdatedTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1))
            {
                livePos += elapsed;
            }
        }

        _currentTrack.Position = livePos;
        if (duration > TimeSpan.Zero)
        {
            _currentTrack.Duration = duration;
        }
    }

    public TimeSpan GetLivePosition()
    {
        if (_isDemoMode)
        {
            return _currentTrack.Position;
        }

        if (!_currentTrack.IsPlaying)
        {
            return _currentTrack.Position;
        }

        TimeSpan live = _currentTrack.Position;
        if (_lastUpdatedTime > DateTimeOffset.MinValue)
        {
            var elapsed = DateTimeOffset.UtcNow - _lastUpdatedTime;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(1))
            {
                live = _lastReportedPosition + elapsed;
            }
            else
            {
                live = _lastReportedPosition + _positionStopwatch.Elapsed;
            }
        }
        else
        {
            live = _lastReportedPosition + _positionStopwatch.Elapsed;
        }

        if (live > _currentTrack.Duration && _currentTrack.Duration > TimeSpan.Zero)
        {
            live = _currentTrack.Duration;
        }
        return live;
    }

    private void UpdateTimeline()
    {
        if (_currentSession == null) return;

        try
        {
            var timeline = _currentSession.GetTimelineProperties();
            var playbackInfo = _currentSession.GetPlaybackInfo();
            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (timeline != null)
            {
                SynchronizeTimeline(timeline.Position, timeline.LastUpdatedTime, timeline.EndTime, isPlaying);
                TrackChanged?.Invoke(_currentTrack);
            }
        }
        catch { }
    }

    private async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference thumbnailRef)
    {
        try
        {
            using var ras = await thumbnailRef.OpenReadAsync();
            if (ras == null || ras.Size == 0) return null;

            using var netStream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            ms.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            App.Log($"LoadThumbnailAsync error: {ex.Message}");
            return null;
        }
    }

    private void OnTimelineTick(object? sender, EventArgs e)
    {
        if (_isDemoMode && _currentTrack.IsPlaying)
        {
            var newPos = _currentTrack.Position + TimeSpan.FromMilliseconds(50);
            if (newPos > _currentTrack.Duration)
            {
                newPos = TimeSpan.Zero;
            }
            _currentTrack.Position = newPos;
            TrackChanged?.Invoke(_currentTrack);
        }
        else if (_currentSession != null && _currentTrack.IsPlaying)
        {
            _currentTrack.Position = GetLivePosition();
            TrackChanged?.Invoke(_currentTrack);
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_isDemoMode)
        {
            _currentTrack.IsPlaying = !_currentTrack.IsPlaying;
            TrackChanged?.Invoke(_currentTrack);
            return;
        }

        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TryTogglePlayPauseAsync();
            }
            catch { }
        }
    }

    public async Task NextAsync()
    {
        if (_isDemoMode)
        {
            _currentTrack.Position = TimeSpan.Zero;
            _currentTrack.Title = _currentTrack.Title switch
            {
                "Starry Horizon" => "Midnight Neon Drive",
                "Midnight Neon Drive" => "Electric Sunlight",
                _ => "Starry Horizon"
            };
            TrackChanged?.Invoke(_currentTrack);
            return;
        }

        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TrySkipNextAsync();
            }
            catch { }
        }
    }

    public async Task PreviousAsync()
    {
        if (_isDemoMode)
        {
            _currentTrack.Position = TimeSpan.Zero;
            _currentTrack.Title = _currentTrack.Title switch
            {
                "Midnight Neon Drive" => "Starry Horizon",
                "Electric Sunlight" => "Midnight Neon Drive",
                _ => "Electric Sunlight"
            };
            TrackChanged?.Invoke(_currentTrack);
            return;
        }

        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TrySkipPreviousAsync();
            }
            catch { }
        }
    }

    public async Task SeekAsync(TimeSpan position)
    {
        if (_isDemoMode)
        {
            _currentTrack.Position = position;
            TrackChanged?.Invoke(_currentTrack);
            return;
        }

        if (_currentSession != null)
        {
            try
            {
                await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
            }
            catch { }
        }
    }
}
