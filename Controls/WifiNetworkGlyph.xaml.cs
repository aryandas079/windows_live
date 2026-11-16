using System.Windows;
using System.Windows.Controls;

namespace WindowsLive.Controls;

public partial class WifiNetworkGlyph : UserControl
{
    public static readonly DependencyProperty IsHotspotProperty = DependencyProperty.Register(
        nameof(IsHotspot),
        typeof(bool),
        typeof(WifiNetworkGlyph),
        new PropertyMetadata(false, OnIsHotspotChanged));

    public WifiNetworkGlyph()
    {
        InitializeComponent();
        UpdateIcon();
    }

    public bool IsHotspot
    {
        get => (bool)GetValue(IsHotspotProperty);
        set => SetValue(IsHotspotProperty, value);
    }

    private static void OnIsHotspotChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((WifiNetworkGlyph)dependencyObject).UpdateIcon();
    }

    private void UpdateIcon()
    {
        if (WifiIcon == null || HotspotIcon == null) return;
        WifiIcon.Visibility = IsHotspot ? Visibility.Collapsed : Visibility.Visible;
        HotspotIcon.Visibility = IsHotspot ? Visibility.Visible : Visibility.Collapsed;
    }
}
