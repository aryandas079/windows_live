using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WindowsLive.Controls;

public partial class CircularProgressArt : UserControl
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(CircularProgressArt),
            new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8EB00"))));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(CircularProgressArt),
            new PropertyMetadata(0.35, OnProgressChanged));

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(CircularProgressArt),
            new PropertyMetadata(null, OnImageSourceChanged));

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public CircularProgressArt()
    {
        InitializeComponent();
        UpdateArc(Progress);
    }

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircularProgressArt cpa)
        {
            cpa.UpdateArc((double)e.NewValue);
        }
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircularProgressArt cpa)
        {
            if (e.NewValue is ImageSource img)
            {
                cpa.AlbumArtImage.Source = img;
                cpa.AlbumArtImage.Visibility = Visibility.Visible;
                cpa.DefaultOrb.Visibility = Visibility.Collapsed;
                cpa.MusicSymbol.Visibility = Visibility.Collapsed;
            }
            else
            {
                cpa.AlbumArtImage.Visibility = Visibility.Collapsed;
                cpa.DefaultOrb.Visibility = Visibility.Visible;
                cpa.MusicSymbol.Visibility = Visibility.Visible;
            }
        }
    }

    private void UpdateArc(double progress)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        double radius = 13.0;
        Point center = new Point(15.0, 15.0);

        if (progress <= 0.005)
        {
            ProgressArcPath.Data = Geometry.Empty;
            return;
        }

        if (progress >= 0.999)
        {
            ProgressArcPath.Data = new EllipseGeometry(center, radius, radius);
            return;
        }

        double angle = progress * 360.0;
        double rad = (angle - 90.0) * Math.PI / 180.0;

        Point startPoint = new Point(center.X, center.Y - radius);
        Point endPoint = new Point(
            center.X + radius * Math.Cos(rad),
            center.Y + radius * Math.Sin(rad)
        );

        bool isLargeArc = angle > 180.0;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(startPoint, false, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true, true);
        }
        geom.Freeze();
        ProgressArcPath.Data = geom;
    }
}
