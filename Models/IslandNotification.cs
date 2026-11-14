using System;
using System.Windows.Media.Imaging;

namespace WindowsLive.Models;

public enum KnownNotificationApp
{
    Generic,
    Instagram,
    TwitterX,
    Facebook,
    WhatsApp,
    Telegram,
    Discord,
    Slack,
    YouTube,
    System,
    Mail,
    Spotify,
    AppleMusic
}

public class IslandNotification
{
    public uint Id { get; set; }
    public string AppName { get; set; } = "Notification";
    public string AppId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public BitmapImage? AppLogo { get; set; }
    public DateTime ReceivedTime { get; set; } = DateTime.Now;

    public KnownNotificationApp AppType => ResolveAppType();

    public bool IsMessagingApp =>
        AppType == KnownNotificationApp.WhatsApp ||
        AppType == KnownNotificationApp.Telegram ||
        AppType == KnownNotificationApp.Discord ||
        AppType == KnownNotificationApp.Slack ||
        AppType == KnownNotificationApp.Instagram ||
        AppType == KnownNotificationApp.TwitterX ||
        AppType == KnownNotificationApp.Facebook ||
        AppName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Signal", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Slack", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Messenger", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Teams", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Messages", StringComparison.OrdinalIgnoreCase) ||
        AppName.Contains("Chat", StringComparison.OrdinalIgnoreCase);

    public string DisplayTitle
    {
        get
        {
            switch (AppType)
            {
                case KnownNotificationApp.Instagram:
                    if (IsBrowserOrGeneric(AppName)) return "Instagram";
                    break;
                case KnownNotificationApp.TwitterX:
                    if (IsBrowserOrGeneric(AppName)) return "Twitter";
                    break;
                case KnownNotificationApp.Facebook:
                    if (IsBrowserOrGeneric(AppName)) return "Facebook";
                    break;
                case KnownNotificationApp.WhatsApp:
                    if (IsBrowserOrGeneric(AppName)) return "WhatsApp";
                    break;
                case KnownNotificationApp.Telegram:
                    if (IsBrowserOrGeneric(AppName)) return "Telegram";
                    break;
                case KnownNotificationApp.Discord:
                    if (IsBrowserOrGeneric(AppName)) return "Discord";
                    break;
                case KnownNotificationApp.YouTube:
                    if (IsBrowserOrGeneric(AppName)) return "YouTube";
                    break;
                case KnownNotificationApp.System:
                    return "System";
            }

            if (!string.IsNullOrWhiteSpace(Title) && !IsBrowserOrGeneric(Title))
            {
                return Title;
            }

            return !string.IsNullOrWhiteSpace(AppName) ? AppName : "Notification";
        }
    }

    private static bool IsBrowserOrGeneric(string str)
    {
        return str.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
               str.Equals("Notification", StringComparison.OrdinalIgnoreCase);
    }

    private KnownNotificationApp ResolveAppType()
    {
        string combined = $"{AppName} {AppId} {Title} {Message}".ToLowerInvariant();

        if (combined.Contains("instagram"))
            return KnownNotificationApp.Instagram;

        if (combined.Contains("twitter") || combined.Contains("x.com") || combined.Contains("tweet") ||
            AppName.Equals("X", StringComparison.OrdinalIgnoreCase) || Title.Equals("X", StringComparison.OrdinalIgnoreCase))
            return KnownNotificationApp.TwitterX;

        if (combined.Contains("whatsapp") || combined.Contains("wa.me"))
            return KnownNotificationApp.WhatsApp;

        if (combined.Contains("telegram") || combined.Contains("t.me"))
            return KnownNotificationApp.Telegram;

        if (combined.Contains("facebook") || combined.Contains("messenger") || combined.Contains("fb.com"))
            return KnownNotificationApp.Facebook;

        if (combined.Contains("discord"))
            return KnownNotificationApp.Discord;

        if (combined.Contains("slack"))
            return KnownNotificationApp.Slack;

        if (combined.Contains("youtube"))
            return KnownNotificationApp.YouTube;

        if (combined.Contains("outlook") || combined.Contains("mail") || combined.Contains("gmail"))
            return KnownNotificationApp.Mail;

        if (combined.Contains("spotify"))
            return KnownNotificationApp.Spotify;

        if (combined.Contains("apple music") || AppId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase))
            return KnownNotificationApp.AppleMusic;

        if (combined.Contains("windows") || combined.Contains("system") || combined.Contains("settings") ||
            combined.Contains("security") || combined.Contains("battery") || combined.Contains("bluetooth") ||
            AppId.Contains("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            AppId.Contains("immersivecontrolcenter", StringComparison.OrdinalIgnoreCase) ||
            AppId.Contains("Microsoft.Windows", StringComparison.OrdinalIgnoreCase))
            return KnownNotificationApp.System;

        return KnownNotificationApp.Generic;
    }
}