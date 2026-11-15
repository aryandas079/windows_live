using System;
using System.Text.RegularExpressions;

namespace WindowsLive.Services;

/// <summary>
/// Resolves and cleans Windows UWP resource strings (e.g. "ms-resource:AlarmButton", "ms-resource:TimerDoneText")
/// into clean, authentic human-readable labels like "Alarm" and "Timer".
/// </summary>
public static class UwpResourceResolver
{
    public static string Clean(string? text, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        string trimmed = text.Trim();
        if (!trimmed.Contains("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        string lower = trimmed.ToLowerInvariant();

        // 1. Windows Alarm strings (e.g. "ms-resource:AlarmButton", "ms-resource:AlarmTitle", "ms-resource:Alarm...")
        if (lower.Contains("alarm"))
        {
            return "Alarm";
        }

        // 2. Windows Timer strings (e.g. "ms-resource:TimerDoneText", "ms-resource:TimerDone", "ms-resource:Timer...")
        if (lower.Contains("timer"))
        {
            return "Timer";
        }

        // 3. Windows Stopwatch strings
        if (lower.Contains("stopwatch"))
        {
            return "Stopwatch";
        }

        // 4. Windows World Clock strings
        if (lower.Contains("worldclock"))
        {
            return "World Clock";
        }

        // 5. Windows Focus Session strings
        if (lower.Contains("focus"))
        {
            return "Focus Session";
        }

        // 6. Generic AppDisplayName
        if (lower.Contains("appdisplayname"))
        {
            return !string.IsNullOrWhiteSpace(fallback) ? fallback : "Clock";
        }

        // 7. Generic UWP ms-resource extractor for any other apps
        try
        {
            int idx = trimmed.IndexOf("ms-resource:", StringComparison.OrdinalIgnoreCase);
            string key = trimmed.Substring(idx + "ms-resource:".Length).TrimStart('/', '\\');
            if (key.Contains('/')) key = key.Substring(key.LastIndexOf('/') + 1);

            // Strip common suffixes like "Text", "Button", "Label"
            if (key.EndsWith("Text", StringComparison.OrdinalIgnoreCase) && key.Length > 4)
            {
                key = key.Substring(0, key.Length - 4);
            }
            else if (key.EndsWith("Button", StringComparison.OrdinalIgnoreCase) && key.Length > 6)
            {
                key = key.Substring(0, key.Length - 6);
            }

            string resolved = Regex.Replace(key, "([a-z])([A-Z])", "$1 $2").Trim();
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }
        catch { }

        return !string.IsNullOrWhiteSpace(fallback) ? fallback : "Notification";
    }
}
