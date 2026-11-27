using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WindowsLive.Services;

public class WeatherInfo
{
    public string CityName { get; set; } = "Current Location";
    public string Country { get; set; } = "";
    public double Latitude { get; set; } = 0.0;
    public double Longitude { get; set; } = 0.0;
    public double Temperature { get; set; } = 0.0;
    public string Unit { get; set; } = "°C";
    public double ApparentTemperature { get; set; } = 0.0;
    public int Humidity { get; set; } = 0;
    public double WindSpeed { get; set; } = 0.0;
    public int WeatherCode { get; set; } = 0;
    public string ConditionText { get; set; } = "Clear";
    public string WeatherIconKey { get; set; } = "Sun";
    public double HighTemp { get; set; } = 0.0;
    public double LowTemp { get; set; } = 0.0;
    public bool IsDay { get; set; } = true;
    public DateTime LastUpdated { get; set; } = DateTime.MinValue;
}

public class WeatherSettings
{
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public double Latitude { get; set; } = 0.0;
    public double Longitude { get; set; } = 0.0;
    public bool UseCelsius { get; set; } = true;
    public bool IsAutoDetected { get; set; } = true;
}

public class WeatherService
{
    public event Action<WeatherInfo>? WeatherUpdated;

    public WeatherInfo? CurrentWeather { get; private set; }
    public WeatherSettings Settings { get; private set; } = new();

    private readonly DispatcherTimer _refreshTimer;
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    private readonly string _settingsFilePath;
    private bool _isFetching = false;

    static WeatherService()
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsLiveDynamicIsland/1.0 (Windows NT 10.0; Win64; x64)");
        }
        catch { }
    }

    public WeatherService()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDir = Path.Combine(localAppData, "WindowsLive");
        Directory.CreateDirectory(appDir);
        _settingsFilePath = Path.Combine(appDir, "weather_settings.json");

        LoadSettings();

        // Refresh weather data every 15 minutes
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _refreshTimer.Tick += async (s, e) => await RefreshWeatherAsync();
    }

    public void Start()
    {
        App.Log("WeatherService started.");
        _refreshTimer.Start();

        Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(Settings.City) || (Settings.Latitude == 0 && Settings.Longitude == 0))
            {
                await AutoDetectLocationAsync();
            }
            else
            {
                await RefreshWeatherAsync();
            }
        });
    }

    public void Stop()
    {
        _refreshTimer.Stop();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<WeatherSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"WeatherService LoadSettings error: {ex.Message}");
        }

        // Defaults
        Settings = new WeatherSettings
        {
            City = "Silchar",
            Country = "India",
            Latitude = 24.8273,
            Longitude = 92.7975,
            UseCelsius = true,
            IsAutoDetected = true
        };
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            App.Log($"WeatherService SaveSettings error: {ex.Message}");
        }
    }

    public async Task<bool> SetLocationAsync(string placeName)
    {
        if (string.IsNullOrWhiteSpace(placeName)) return false;

        try
        {
            string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(placeName.Trim())}&count=1&language=en&format=json";
            using var resp = await _httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return false;

            string content = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return false;
            }

            var first = results[0];
            string cityName = first.GetProperty("name").GetString() ?? placeName.Trim();
            string country = first.TryGetProperty("country", out var cProp) ? cProp.GetString() ?? "" : "";
            double lat = first.GetProperty("latitude").GetDouble();
            double lon = first.GetProperty("longitude").GetDouble();

            Settings.City = cityName;
            Settings.Country = country;
            Settings.Latitude = lat;
            Settings.Longitude = lon;
            Settings.IsAutoDetected = false;
            SaveSettings();

            await RefreshWeatherAsync();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"WeatherService SetLocation error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> AutoDetectLocationAsync()
    {
        try
        {
            // Primary geolocation: ip-api.com
            string url = "http://ip-api.com/json/?fields=status,city,country,lat,lon";
            using var resp = await _httpClient.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var sProp) && sProp.GetString() == "success")
                {
                    Settings.City = root.GetProperty("city").GetString() ?? "Current Location";
                    Settings.Country = root.TryGetProperty("country", out var cProp) ? cProp.GetString() ?? "" : "";
                    Settings.Latitude = root.GetProperty("lat").GetDouble();
                    Settings.Longitude = root.GetProperty("lon").GetDouble();
                    Settings.IsAutoDetected = true;
                    SaveSettings();

                    await RefreshWeatherAsync();
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"WeatherService AutoDetect error: {ex.Message}");
        }

        // Fallback: if not set, keep existing or default Silchar
        if (Settings.Latitude != 0 && Settings.Longitude != 0)
        {
            await RefreshWeatherAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> ToggleUnitAsync()
    {
        Settings.UseCelsius = !Settings.UseCelsius;
        SaveSettings();
        return await RefreshWeatherAsync();
    }

    public async Task<bool> RefreshWeatherAsync()
    {
        if (_isFetching) return false;
        if (Settings.Latitude == 0 && Settings.Longitude == 0) return false;

        _isFetching = true;
        try
        {
            string unitParam = Settings.UseCelsius ? "" : "&temperature_unit=fahrenheit";
            string url = $"https://api.open-meteo.com/v1/forecast?latitude={Settings.Latitude}&longitude={Settings.Longitude}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto{unitParam}";

            using var resp = await _httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return false;

            string content = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("current", out var cur)) return false;

            double temp = cur.GetProperty("temperature_2m").GetDouble();
            double appTemp = cur.TryGetProperty("apparent_temperature", out var atProp) ? atProp.GetDouble() : temp;
            int hum = cur.TryGetProperty("relative_humidity_2m", out var hProp) ? hProp.GetInt32() : 0;
            double wind = cur.TryGetProperty("wind_speed_10m", out var wProp) ? wProp.GetDouble() : 0.0;
            int code = cur.TryGetProperty("weather_code", out var cProp) ? cProp.GetInt32() : 0;
            int isDayInt = cur.TryGetProperty("is_day", out var dProp) ? dProp.GetInt32() : 1;
            bool isDay = isDayInt == 1;

            double high = temp;
            double low = temp;
            if (root.TryGetProperty("daily", out var daily))
            {
                if (daily.TryGetProperty("temperature_2m_max", out var maxArr) && maxArr.GetArrayLength() > 0)
                {
                    high = maxArr[0].GetDouble();
                }
                if (daily.TryGetProperty("temperature_2m_min", out var minArr) && minArr.GetArrayLength() > 0)
                {
                    low = minArr[0].GetDouble();
                }
            }

            var (condText, iconKey) = MapWeatherCode(code, isDay);

            var info = new WeatherInfo
            {
                CityName = Settings.City,
                Country = Settings.Country,
                Latitude = Settings.Latitude,
                Longitude = Settings.Longitude,
                Temperature = Math.Round(temp, 0),
                Unit = Settings.UseCelsius ? "°C" : "°F",
                ApparentTemperature = Math.Round(appTemp, 0),
                Humidity = hum,
                WindSpeed = wind,
                WeatherCode = code,
                ConditionText = condText,
                WeatherIconKey = iconKey,
                HighTemp = Math.Round(high, 0),
                LowTemp = Math.Round(low, 0),
                IsDay = isDay,
                LastUpdated = DateTime.Now
            };

            CurrentWeather = info;
            App.Log($"WeatherService updated: {info.CityName} {info.Temperature}{info.Unit} {info.ConditionText}");

            WeatherUpdated?.Invoke(info);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"WeatherService RefreshWeather error: {ex.Message}");
            return false;
        }
        finally
        {
            _isFetching = false;
        }
    }

    public static (string condition, string iconKey) MapWeatherCode(int code, bool isDay)
    {
        return code switch
        {
            0 => isDay ? ("Sunny", "Sun") : ("Clear Night", "Moon"),
            1 => isDay ? ("Mainly Sunny", "PartlyCloudyDay") : ("Mainly Clear", "PartlyCloudyNight"),
            2 => isDay ? ("Partly Cloudy", "PartlyCloudyDay") : ("Partly Cloudy", "PartlyCloudyNight"),
            3 => ("Overcast", "Cloudy"),
            45 or 48 => ("Foggy", "Fog"),
            51 or 53 or 55 => ("Drizzle", "Drizzle"),
            56 or 57 => ("Freezing Drizzle", "Drizzle"),
            61 or 63 or 65 => ("Rain", "Rain"),
            66 or 67 => ("Freezing Rain", "Rain"),
            71 or 73 or 75 => ("Snow", "Snow"),
            77 => ("Snow Grains", "Snow"),
            80 or 81 or 82 => ("Showers", "Rain"),
            85 or 86 => ("Snow Showers", "Snow"),
            95 or 96 or 99 => ("Thunderstorm", "Thunderstorm"),
            _ => ("Clear", isDay ? "Sun" : "Moon")
        };
    }
}
