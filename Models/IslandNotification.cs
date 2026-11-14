using System;
using System.Windows.Media.Imaging;

namespace WindowsLive.Models;

public class IslandNotification
{
    public uint Id { get; set; }
    public string AppName { get; set; } = "Notification";
    public string AppId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public BitmapImage? AppLogo { get; set; }
    public DateTime ReceivedTime { get; set; } = DateTime.Now;

    public bool IsWhatsApp =>
        AppName.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
        Title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase) ||
        AppId.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);

    public bool IsTelegram =>
        AppName.Contains("Telegram", StringComparison.OrdinalIgnoreCase) ||
        AppId.Contains("Telegram", StringComparison.OrdinalIgnoreCase);

    public bool IsDiscord =>
        AppName.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
        AppId.Contains("Discord", StringComparison.OrdinalIgnoreCase);

    public bool IsSlack =>
        AppName.Contains("Slack", StringComparison.OrdinalIgnoreCase) ||
        AppId.Contains("Slack", StringComparison.OrdinalIgnoreCase);
}
