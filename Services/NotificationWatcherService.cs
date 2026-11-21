using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WindowsLive.Models;

namespace WindowsLive.Services;

public class NotificationWatcherService
{
    private UserNotificationListener? _listener;
    private readonly DispatcherTimer _pollTimer;
    private readonly Dictionary<uint, string> _activeNotificationContent = new();
    private readonly List<IslandNotification> _activeNotifications = new();
    private bool _isInitialized = false;
    private bool _isPolling = false;

    public event Action<IslandNotification>? NotificationReceived;
    public event Action? NotificationsChanged;

    public IReadOnlyList<IslandNotification> ActiveNotifications => _activeNotifications;
    public IslandNotification? LatestNotification => _activeNotifications.LastOrDefault();
    public int Count => _activeNotifications.Count;
    public bool HasNotifications => _activeNotifications.Count > 0;

    public NotificationWatcherService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _pollTimer.Tick += async (s, e) => await PollNotificationsAsync();
    }

    public async Task StartAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access == UserNotificationListenerAccessStatus.Allowed)
            {
                // Baseline: load existing notifications into active list so compact pill reflects current state
                var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                foreach (var n in existing)
                {
                    var (title, msg) = ExtractTexts(n);
                    var logo = await LoadAppLogoAsync(n);
                    string appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "Notification";
                    if (string.IsNullOrWhiteSpace(appName)) appName = "Notification";

                    var item = new IslandNotification
                    {
                        Id = n.Id,
                        AppName = appName,
                        AppId = n.AppInfo?.Id ?? "",
                        Title = string.IsNullOrWhiteSpace(title) ? appName : title,
                        Message = msg,
                        AppLogo = logo,
                        ReceivedTime = DateTime.Now
                    };
                    _activeNotifications.Add(item);
                    _activeNotificationContent[n.Id] = $"{title}|{msg}";
                }

                _isInitialized = true;
                _pollTimer.Start();
                App.Log($"NotificationWatcherService started successfully. Loaded {existing.Count} notifications.");
                if (_activeNotifications.Count > 0)
                {
                    NotificationsChanged?.Invoke();
                }
            }
            else
            {
                App.Log($"NotificationWatcherService access status: {access}");
            }
        }
        catch (Exception ex)
        {
            App.Log($"NotificationWatcherService StartAsync error: {ex.Message}");
        }
    }

    private async Task PollNotificationsAsync()
    {
        if (_listener == null || !_isInitialized || _isPolling) return;

        _isPolling = true;
        try
        {
            var notifs = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var currentIds = new HashSet<uint>();
            bool changed = false;

            foreach (var n in notifs)
            {
                currentIds.Add(n.Id);
                var (title, msg) = ExtractTexts(n);
                string contentSignature = $"{title}|{msg}";

                var existingItem = _activeNotifications.FirstOrDefault(x => x.Id == n.Id);

                if (existingItem == null)
                {
                    // Brand new notification
                    changed = true;
                    var logo = await LoadAppLogoAsync(n);
                    string appName = UwpResourceResolver.Clean(n.AppInfo?.DisplayInfo?.DisplayName, "Notification");
                    if (string.IsNullOrWhiteSpace(appName)) appName = "Notification";

                    string cleanTitle = UwpResourceResolver.Clean(title, appName);
                    string cleanMsg = UwpResourceResolver.Clean(msg, "");

                    var item = new IslandNotification
                    {
                        Id = n.Id,
                        AppName = appName,
                        AppId = n.AppInfo?.Id ?? "",
                        Title = string.IsNullOrWhiteSpace(cleanTitle) ? appName : cleanTitle,
                        Message = cleanMsg,
                        AppLogo = logo,
                        ReceivedTime = DateTime.Now
                    };

                    _activeNotifications.Add(item);
                    _activeNotificationContent[n.Id] = contentSignature;
                    App.Log($"New notification detected: App={item.AppName}, Title={item.Title}, Message={item.Message}");
                    NotificationReceived?.Invoke(item);
                }
                else if (_activeNotificationContent.TryGetValue(n.Id, out var prevContent) && prevContent != contentSignature)
                {
                    // Notification content updated (e.g. WhatsApp new message in existing conversation thread)
                    changed = true;
                    _activeNotificationContent[n.Id] = contentSignature;
                    string cleanTitle = UwpResourceResolver.Clean(title, existingItem.AppName);
                    existingItem.Title = string.IsNullOrWhiteSpace(cleanTitle) ? existingItem.AppName : cleanTitle;
                    existingItem.Message = UwpResourceResolver.Clean(msg, "");
                    existingItem.ReceivedTime = DateTime.Now;

                    App.Log($"Notification updated: App={existingItem.AppName}, Title={existingItem.Title}, Message={existingItem.Message}");
                    NotificationReceived?.Invoke(existingItem);
                }
            }

            // Clean up dismissed notifications
            for (int i = _activeNotifications.Count - 1; i >= 0; i--)
            {
                uint id = _activeNotifications[i].Id;
                if (!currentIds.Contains(id))
                {
                    _activeNotifications.RemoveAt(i);
                    _activeNotificationContent.Remove(id);
                    changed = true;
                }
            }

            if (changed)
            {
                NotificationsChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            App.Log($"PollNotificationsAsync error: {ex.Message}");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private (string title, string message) ExtractTexts(UserNotification n)
    {
        string fallbackApp = UwpResourceResolver.Clean(n.AppInfo?.DisplayInfo?.DisplayName, "Notification");
        try
        {
            var toast = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (toast != null)
            {
                var elements = toast.GetTextElements()
                    .Select(t => UwpResourceResolver.Clean(t.Text?.Trim(), fallbackApp))
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                if (elements.Count >= 2)
                {
                    string msg = string.Join(" • ", elements.Skip(1));
                    msg = System.Text.RegularExpressions.Regex.Replace(msg, @"[ \t]*\r?\n[ \t]*", "\n");
                    msg = System.Text.RegularExpressions.Regex.Replace(msg, @"\n{2,}", "\n");
                    return (elements[0]!, msg.Trim());
                }
                else if (elements.Count == 1)
                {
                    string msg = elements[0]!;
                    msg = System.Text.RegularExpressions.Regex.Replace(msg, @"[ \t]*\r?\n[ \t]*", "\n");
                    msg = System.Text.RegularExpressions.Regex.Replace(msg, @"\n{2,}", "\n");
                    return (fallbackApp, msg.Trim());
                }
            }
        }
        catch { }

        return (fallbackApp, "New notification received");
    }

    private async Task<BitmapImage?> LoadAppLogoAsync(UserNotification n)
    {
        try
        {
            var logoRef = n.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(48, 48));
            if (logoRef != null)
            {
                using var ras = await logoRef.OpenReadAsync();
                using var stream = ras.AsStreamForRead();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch { }

        return null;
    }

    public void Dismiss(uint id)
    {
        try
        {
            _listener?.RemoveNotification(id);
        }
        catch { }
    }
}