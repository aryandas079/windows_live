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

    public MediaTrackInfo CurrentTrack => _currentTrack;

    public MediaManager(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        _timelineTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
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

    public void SimulateTrack(string artist, string title, TimeSpan? position = null, TimeSpan? duration = null)
    {
        _isDemoMode = true;
        _currentTrack = new MediaTrackInfo
        {
            Title = title,
            Artist = artist,
            AlbumTitle = "Album",
            SourceApp = "Media Player",
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

            string source = _currentSession.SourceAppUserModelId ?? "Media Player";
            if (source.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) source = "Spotify";
            else if (source.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) source = "Chrome";
            else if (source.Contains("Edge", StringComparison.OrdinalIgnoreCase)) source = "Edge";
            else if (source.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) source = "Apple Music";
            else if (source.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase) || source.Contains("Media", StringComparison.OrdinalIgnoreCase)) source = "Media Player";

            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool hasTitle = !string.IsNullOrWhiteSpace(props?.Title);

            _currentTrack = new MediaTrackInfo
            {
                Title = hasTitle ? props!.Title : (isPlaying ? "Now Playing" : string.Empty),
                Artist = props?.Artist ?? string.Empty,
                AlbumTitle = props?.AlbumTitle ?? string.Empty,
                SourceApp = source,
                Thumbnail = thumbnailImage,
                IsPlaying = isPlaying,
                Position = timeline?.Position ?? TimeSpan.Zero,
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

    private void UpdateTimeline()
    {
        if (_currentSession == null) return;

        try
        {
            var timeline = _currentSession.GetTimelineProperties();
            if (timeline != null)
            {
                _currentTrack.Position = timeline.Position;
                if (timeline.EndTime > TimeSpan.Zero)
                {
                    _currentTrack.Duration = timeline.EndTime;
                }
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
            var newPos = _currentTrack.Position + TimeSpan.FromMilliseconds(500);
            if (newPos > _currentTrack.Duration)
            {
                newPos = TimeSpan.Zero;
            }
            _currentTrack.Position = newPos;
            TrackChanged?.Invoke(_currentTrack);
        }
        else if (_currentSession != null && _currentTrack.IsPlaying)
        {
            _currentTrack.Position += TimeSpan.FromMilliseconds(500);
            if (_currentTrack.Position > _currentTrack.Duration)
            {
                _currentTrack.Position = _currentTrack.Duration;
            }
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
