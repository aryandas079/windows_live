using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WindowsLive.Services;

public record LyricLine(TimeSpan Time, string Text);

public class ParsedLyrics
{
    public string Artist { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<LyricLine> Lines { get; set; } = new();
    public bool IsSynced { get; set; } = false;

    public (string Prev, string Current, string Next, int Index) GetLinesAt(TimeSpan position)
    {
        if (Lines.Count == 0)
        {
            return (string.Empty, !string.IsNullOrWhiteSpace(Title) ? $"♪ {Title} ♪" : "♪ Music ♪", string.Empty, -1);
        }

        // Before first line
        if (position < Lines[0].Time)
        {
            string firstLine = string.IsNullOrWhiteSpace(Lines[0].Text) ? (Lines.Count > 1 ? Lines[1].Text : string.Empty) : Lines[0].Text;
            return (string.Empty, !string.IsNullOrWhiteSpace(Title) ? $"♪ {Title} ♪" : "♪ Music ♪", firstLine, -1);
        }

        // Find active line
        int activeIdx = 0;
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Time <= position)
            {
                activeIdx = i;
            }
            else
            {
                break;
            }
        }

        string curr = Lines[activeIdx].Text;
        if (string.IsNullOrWhiteSpace(curr))
        {
            curr = "♪ Instrumental ♪";
        }

        string prev = string.Empty;
        for (int i = activeIdx - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(Lines[i].Text))
            {
                prev = Lines[i].Text;
                break;
            }
        }

        string next = string.Empty;
        for (int i = activeIdx + 1; i < Lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(Lines[i].Text))
            {
                next = Lines[i].Text;
                break;
            }
        }

        return (prev, curr, next, activeIdx);
    }
}

public class LyricsService
{
    public event Action<ParsedLyrics>? LyricsLoaded;
    public event Action<string, string, string>? ActiveLineChanged;

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private readonly ConcurrentDictionary<string, ParsedLyrics> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _syncTimer;
    private readonly Dispatcher _dispatcher;

    private ParsedLyrics? _currentLyrics;
    private string _currentTrackKey = string.Empty;
    private int _lastActiveIndex = -2;
    private string _lastCurrentLine = string.Empty;
    private CancellationTokenSource? _cts;

    private Func<TimeSpan>? _positionProvider;
    private Func<bool>? _isPlayingProvider;

    static LyricsService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsLiveDynamicIsland/1.0 (Windows NT 10.0; Win64; x64)");
    }

    public LyricsService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        _syncTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _syncTimer.Tick += OnSyncTimerTick;
    }

    public ParsedLyrics? CurrentLyrics => _currentLyrics;
    public bool HasLyrics => _currentLyrics != null && _currentLyrics.Lines.Count > 0;

    public void SetPlaybackProviders(Func<TimeSpan> positionProvider, Func<bool> isPlayingProvider)
    {
        _positionProvider = positionProvider;
        _isPlayingProvider = isPlayingProvider;
    }

    public void StartTracking()
    {
        if (!_syncTimer.IsEnabled)
        {
            _syncTimer.Start();
        }
    }

    public void StopTracking()
    {
        _syncTimer.Stop();
    }

    public void LoadTrack(string artist, string title, TimeSpan duration, bool isPlaying)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            _currentLyrics = null;
            _currentTrackKey = string.Empty;
            _lastActiveIndex = -2;
            _lastCurrentLine = string.Empty;
            ActiveLineChanged?.Invoke(string.Empty, "No track playing", string.Empty);
            return;
        }

        string cleanTitle = CleanTrackTitle(title);
        string cleanArtist = CleanArtistName(artist);
        string key = $"{cleanArtist}|{cleanTitle}";

        if (string.Equals(_currentTrackKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentTrackKey = key;
        _lastActiveIndex = -2;
        _lastCurrentLine = string.Empty;

        if (_cache.TryGetValue(key, out var cached))
        {
            _currentLyrics = cached;
            LyricsLoaded?.Invoke(cached);
            UpdateCurrentLines();
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                var lyrics = await FetchLyricsFromLrclibAsync(cleanArtist, cleanTitle, duration, token);
                if (token.IsCancellationRequested) return;

                if (lyrics != null && lyrics.Lines.Count > 0)
                {
                    _cache[key] = lyrics;
                }
                else
                {
                    lyrics = CreateFallbackLyrics(artist, title, duration);
                    _cache[key] = lyrics;
                }

                _dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _currentLyrics = lyrics;
                    LyricsLoaded?.Invoke(lyrics);
                    UpdateCurrentLines();
                });
            }
            catch (Exception ex)
            {
                App.Log($"LyricsService fetch error: {ex.Message}");
                if (!token.IsCancellationRequested)
                {
                    var fallback = CreateFallbackLyrics(artist, title, duration);
                    _dispatcher.Invoke(() =>
                    {
                        _currentLyrics = fallback;
                        LyricsLoaded?.Invoke(fallback);
                        UpdateCurrentLines();
                    });
                }
            }
        }, token);
    }

    public void SetCustomLyrics(string artist, string title, string lrcText)
    {
        var lyrics = ParseLrc(lrcText, artist, title);
        string key = $"{CleanArtistName(artist)}|{CleanTrackTitle(title)}";
        _cache[key] = lyrics;
        _currentLyrics = lyrics;
        _currentTrackKey = key;
        _lastActiveIndex = -2;
        _lastCurrentLine = string.Empty;
        LyricsLoaded?.Invoke(lyrics);
        UpdateCurrentLines();
    }

    private void OnSyncTimerTick(object? sender, EventArgs e)
    {
        UpdateCurrentLines();
    }

    public void UpdateCurrentLines()
    {
        if (_currentLyrics == null) return;

        TimeSpan pos = _positionProvider?.Invoke() ?? TimeSpan.Zero;
        var (prev, curr, next, idx) = _currentLyrics.GetLinesAt(pos);

        if (idx != _lastActiveIndex || !string.Equals(curr, _lastCurrentLine, StringComparison.Ordinal))
        {
            _lastActiveIndex = idx;
            _lastCurrentLine = curr;
            ActiveLineChanged?.Invoke(prev, curr, next);
        }
    }

    private static async Task<ParsedLyrics?> FetchLyricsFromLrclibAsync(string artist, string title, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            // 1. Direct GET
            string directUrl = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
            if (duration > TimeSpan.Zero)
            {
                directUrl += $"&duration={(int)duration.TotalSeconds}";
            }

            using var directResp = await _httpClient.GetAsync(directUrl, ct);
            if (directResp.IsSuccessStatusCode)
            {
                string json = await directResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string synced = root.TryGetProperty("syncedLyrics", out var sp) && sp.ValueKind == JsonValueKind.String ? sp.GetString() ?? string.Empty : string.Empty;
                string plain = root.TryGetProperty("plainLyrics", out var pp) && pp.ValueKind == JsonValueKind.String ? pp.GetString() ?? string.Empty : string.Empty;

                if (!string.IsNullOrWhiteSpace(synced))
                {
                    return ParseLrc(synced, artist, title);
                }
                if (!string.IsNullOrWhiteSpace(plain))
                {
                    return ParsePlainLyrics(plain, artist, title, duration);
                }
            }

            // 2. Search Fallback
            string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(artist + " " + title)}";
            using var searchResp = await _httpClient.GetAsync(searchUrl, ct);
            if (searchResp.IsSuccessStatusCode)
            {
                string json = await searchResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    JsonElement? bestSynced = null;
                    JsonElement? bestPlain = null;

                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("syncedLyrics", out var sp) && sp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sp.GetString()))
                        {
                            bestSynced = item;
                            break;
                        }
                        if (bestPlain == null && item.TryGetProperty("plainLyrics", out var pp) && pp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pp.GetString()))
                        {
                            bestPlain = item;
                        }
                    }

                    if (bestSynced.HasValue)
                    {
                        string s = bestSynced.Value.GetProperty("syncedLyrics").GetString()!;
                        return ParseLrc(s, artist, title);
                    }
                    if (bestPlain.HasValue)
                    {
                        string p = bestPlain.Value.GetProperty("plainLyrics").GetString()!;
                        return ParsePlainLyrics(p, artist, title, duration);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"LRCLIB query failed: {ex.Message}");
        }

        return null;
    }

    private static ParsedLyrics ParseLrc(string lrcText, string artist, string title)
    {
        var result = new ParsedLyrics
        {
            Artist = artist,
            Title = title,
            IsSynced = true
        };

        var regex = new Regex(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$", RegexOptions.Compiled);

        using var reader = new StringReader(lrcText);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var match = regex.Match(line);
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, out double min) &&
                    double.TryParse(match.Groups[2].Value, out double sec))
                {
                    var time = TimeSpan.FromSeconds(min * 60.0 + sec);
                    string text = match.Groups[3].Value.Trim();
                    result.Lines.Add(new LyricLine(time, text));
                }
            }
        }

        result.Lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    private static ParsedLyrics ParsePlainLyrics(string plainText, string artist, string title, TimeSpan duration)
    {
        var result = new ParsedLyrics
        {
            Artist = artist,
            Title = title,
            IsSynced = false
        };

        var rawLines = plainText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToList();

        if (rawLines.Count == 0) return result;

        double totalSec = duration.TotalSeconds > 10 ? duration.TotalSeconds : 180.0;
        double startSec = Math.Min(10.0, totalSec * 0.05);
        double endSec = Math.Max(startSec + 10.0, totalSec * 0.95);
        double step = (endSec - startSec) / Math.Max(1, rawLines.Count);

        for (int i = 0; i < rawLines.Count; i++)
        {
            var t = TimeSpan.FromSeconds(startSec + i * step);
            result.Lines.Add(new LyricLine(t, rawLines[i]));
        }

        return result;
    }

    private static ParsedLyrics CreateFallbackLyrics(string artist, string title, TimeSpan duration)
    {
        var result = new ParsedLyrics
        {
            Artist = artist,
            Title = title,
            IsSynced = false
        };

        result.Lines.Add(new LyricLine(TimeSpan.Zero, $"♪ {title} ♪"));
        if (!string.IsNullOrWhiteSpace(artist))
        {
            result.Lines.Add(new LyricLine(TimeSpan.FromSeconds(3), artist));
        }
        result.Lines.Add(new LyricLine(TimeSpan.FromSeconds(6), "(Live Captions / Lyrics)"));
        return result;
    }

    private static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        string t = title;
        t = Regex.Replace(t, @"\s*[\(\[](feat\.|ft\.|featuring).*?[\)\]]", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*[\(\[](remastered|remaster|deluxe|bonus|expanded|anniversary|live).*?[\)\]]", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*[\(\[](official\s*(music\s*)?video|official\s*audio|lyric\s*video)[\)\]]", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s*-\s*(single|ep|deluxe|remastered).*$", "", RegexOptions.IgnoreCase);
        return t.Trim();
    }

    private static string CleanArtistName(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;

        string a = artist;
        int commaIdx = a.IndexOfAny(new[] { ',', ';', '&' });
        if (commaIdx > 0)
        {
            a = a.Substring(0, commaIdx);
        }

        int featIdx = a.IndexOf("feat", StringComparison.OrdinalIgnoreCase);
        if (featIdx > 0)
        {
            a = a.Substring(0, featIdx);
        }

        return a.Trim();
    }
}
