using System.Windows;
using System.Windows.Controls;
using WindowsLive.Models;

namespace WindowsLive.Controls;

public partial class AppIconControl : UserControl
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(AppIconControl),
            new PropertyMetadata(new CornerRadius(6)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty IconPaddingProperty =
        DependencyProperty.Register(
            nameof(IconPadding),
            typeof(Thickness),
            typeof(AppIconControl),
            new PropertyMetadata(new Thickness(5)));

    public Thickness IconPadding
    {
        get => (Thickness)GetValue(IconPaddingProperty);
        set => SetValue(IconPaddingProperty, value);
    }

    public AppIconControl()
    {
        InitializeComponent();
    }

    public void SetNotification(IslandNotification? notif)
    {
        IconInstagram.Visibility = Visibility.Collapsed;
        IconTwitterX.Visibility = Visibility.Collapsed;
        IconFacebook.Visibility = Visibility.Collapsed;
        IconWhatsApp.Visibility = Visibility.Collapsed;
        IconTelegram.Visibility = Visibility.Collapsed;
        IconDiscord.Visibility = Visibility.Collapsed;
        IconYouTube.Visibility = Visibility.Collapsed;
        IconSystem.Visibility = Visibility.Collapsed;
        IconMail.Visibility = Visibility.Collapsed;
        IconCustomImage.Visibility = Visibility.Collapsed;
        IconGeneric.Visibility = Visibility.Collapsed;

        if (notif == null)
        {
            IconGeneric.Visibility = Visibility.Visible;
            return;
        }

        switch (notif.AppType)
        {
            case KnownNotificationApp.Instagram:
                IconInstagram.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.TwitterX:
                IconTwitterX.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.Facebook:
                IconFacebook.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.WhatsApp:
                IconWhatsApp.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.Telegram:
                IconTelegram.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.Discord:
                IconDiscord.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.YouTube:
                IconYouTube.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.System:
                IconSystem.Visibility = Visibility.Visible;
                break;
            case KnownNotificationApp.Mail:
                IconMail.Visibility = Visibility.Visible;
                break;
            default:
                if (notif.AppLogo != null)
                {
                    RealAppImage.Source = notif.AppLogo;
                    IconCustomImage.Visibility = Visibility.Visible;
                }
                else
                {
                    IconGeneric.Visibility = Visibility.Visible;
                }
                break;
        }
    }
}