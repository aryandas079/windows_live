using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsLive.Controls;

public partial class EqualizerControl : UserControl
{
    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(EqualizerControl),
            new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8EB00"))));

    public static readonly DependencyProperty IsPlayingProperty =
        DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(EqualizerControl),
            new PropertyMetadata(false, OnIsPlayingChanged));

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    private readonly DispatcherTimer _animTimer;
    private readonly Random _rand = new();
    private readonly double[] _currentHeights = new double[5] { 4, 8, 13, 16, 11 };
    private readonly double[] _targetHeights = new double[5] { 4, 8, 13, 16, 11 };
    private int _stepCounter = 0;

    public EqualizerControl()
    {
        InitializeComponent();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _animTimer.Tick += OnAnimTick;
    }

    private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EqualizerControl eq)
        {
            if ((bool)e.NewValue)
            {
                eq._animTimer.Start();
            }
            else
            {
                eq.EaseToIdle();
            }
        }
    }

    private void EaseToIdle()
    {
        _targetHeights[0] = 3;
        _targetHeights[1] = 6;
        _targetHeights[2] = 4;
        _targetHeights[3] = 7;
        _targetHeights[4] = 3;
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        _stepCounter++;

        if (IsPlaying)
        {
            // Pick new target heights every few ticks
            if (_stepCounter % 3 == 0)
            {
                _targetHeights[0] = _rand.Next(3, 11);
                _targetHeights[1] = _rand.Next(5, 15);
                _targetHeights[2] = _rand.Next(4, 18);
                _targetHeights[3] = _rand.Next(6, 17);
                _targetHeights[4] = _rand.Next(3, 13);
            }
        }
        else
        {
            EaseToIdle();
        }

        // Smoothly interpolate current heights towards targets
        for (int i = 0; i < 5; i++)
        {
            _currentHeights[i] += (_targetHeights[i] - _currentHeights[i]) * 0.4;
            _currentHeights[i] = Math.Clamp(_currentHeights[i], 2.0, 18.0);
        }

        Bar1.Height = _currentHeights[0];
        Bar2.Height = _currentHeights[1];
        Bar3.Height = _currentHeights[2];
        Bar4.Height = _currentHeights[3];
        Bar5.Height = _currentHeights[4];

        if (!IsPlaying && Math.Abs(_currentHeights[0] - _targetHeights[0]) < 0.2)
        {
            _animTimer.Stop();
        }
    }
}
