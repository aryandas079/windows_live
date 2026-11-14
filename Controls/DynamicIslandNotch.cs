using System;
using System.Windows;
using System.Windows.Media;

namespace WindowsLive.Controls;

public class DynamicIslandNotch : FrameworkElement
{
    public static readonly DependencyProperty EarFilletProperty =
        DependencyProperty.Register(nameof(EarFillet), typeof(double), typeof(DynamicIslandNotch),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(DynamicIslandNotch),
            new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsNotchModeProperty =
        DependencyProperty.Register(nameof(IsNotchMode), typeof(bool), typeof(DynamicIslandNotch),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(DynamicIslandNotch),
            new FrameworkPropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#080809")), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(DynamicIslandNotch),
            new FrameworkPropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2BFFFFFF")), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(DynamicIslandNotch),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double EarFillet
    {
        get => (double)GetValue(EarFilletProperty);
        set => SetValue(EarFilletProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public bool IsNotchMode
    {
        get => (bool)GetValue(IsNotchModeProperty);
        set => SetValue(IsNotchModeProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        Pen? strokePen = (Stroke != null && StrokeThickness > 0)
            ? new Pen(Stroke, StrokeThickness)
            : null;

        if (strokePen != null)
        {
            strokePen.Freeze();
        }

        if (!IsNotchMode)
        {
            // Floating pill capsule
            double r = Math.Min(CornerRadius, Math.Min(w / 2.0, h / 2.0));
            dc.DrawRoundedRectangle(Fill, strokePen, new Rect(0, 0, w, h), r, r);
            return;
        }

        double f = Math.Max(0, EarFillet);
        double rBottom = Math.Max(0, CornerRadius);

        // Clamp parameters so geometry is always valid
        f = Math.Min(f, w / 4.0);
        rBottom = Math.Min(rBottom, Math.Min(h, (w - 2.0 * f) / 2.0));

        var stream = new StreamGeometry();
        using (var ctx = stream.Open())
        {
            ctx.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);

            if (f > 0.5)
            {
                // Left concave ear down-right into vertical side
                ctx.ArcTo(new Point(f, f), new Size(f, f), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
            }
            else
            {
                ctx.LineTo(new Point(0, 0), isStroked: true, isSmoothJoin: true);
            }

            // Left vertical edge down to bottom corner
            ctx.LineTo(new Point(f, h - rBottom), isStroked: true, isSmoothJoin: true);

            if (rBottom > 0.5)
            {
                // Bottom-left convex corner
                ctx.ArcTo(new Point(f + rBottom, h), new Size(rBottom, rBottom), 0, false, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: true);
            }

            // Bottom flat edge
            ctx.LineTo(new Point(w - f - rBottom, h), isStroked: true, isSmoothJoin: true);

            if (rBottom > 0.5)
            {
                // Bottom-right convex corner
                ctx.ArcTo(new Point(w - f, h - rBottom), new Size(rBottom, rBottom), 0, false, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: true);
            }

            // Right vertical edge up to ear
            ctx.LineTo(new Point(w - f, f), isStroked: true, isSmoothJoin: true);

            if (f > 0.5)
            {
                // Right concave ear up-right to top edge
                ctx.ArcTo(new Point(w, 0), new Size(f, f), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
            }

            // Flush with top edge (unstroked so it blends seamlessly into monitor bezel)
            ctx.LineTo(new Point(0, 0), isStroked: false, isSmoothJoin: true);
        }

        stream.Freeze();
        dc.DrawGeometry(Fill, strokePen, stream);
    }
}
