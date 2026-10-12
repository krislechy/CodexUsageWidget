using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexUsageWidget.Controls;

/// <summary>Resolution-independent clockwise usage arc, including exact zero/full states.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0d, OnValueChanged));
    public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(nameof(AnimatedValue), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.HotPink, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(17d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double AnimatedValue { get => (double)GetValue(AnimatedValueProperty); set => SetValue(AnimatedValueProperty, value); }
    public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    protected override AutomationPeer OnCreateAutomationPeer() => new RingGaugeAutomationPeer(this);

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var ring = (RingGauge)sender;
        var target = Math.Clamp((double)e.NewValue, 0, 100);
        if (UIElementAutomationPeer.FromElement(ring) is { } peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)e.OldValue, target);
        if (!SystemParameters.ClientAreaAnimation || !ring.IsLoaded)
        {
            ring.BeginAnimation(AnimatedValueProperty, null);
            ring.AnimatedValue = target;
            return;
        }
        var from = ring.AnimatedValue;
        ring.BeginAnimation(AnimatedValueProperty, null);
        ring.AnimatedValue = target;
        ring.BeginAnimation(AnimatedValueProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(550))
        {
            EasingFunction = new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, (Math.Min(ActualWidth, ActualHeight) - StrokeWidth) / 2);
        var track = new Pen(TrackBrush, StrokeWidth);
        context.DrawEllipse(null, track, center, radius, radius);
        var value = Math.Clamp(AnimatedValue, 0, 100);
        if (value <= 0 || radius <= 0) return;
        var pen = new Pen(RingBrush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (value >= 100)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var radians = value / 100 * 2 * Math.PI - Math.PI / 2;
        var end = new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            drawing.ArcTo(end, new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }
}

public sealed class RingGaugeAutomationPeer(RingGauge owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
{
    protected override string GetClassNameCore() => nameof(RingGauge);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
    protected override string GetNameCore() => $"{base.GetNameCore()} Израсходовано {owner.Value:0.#}%.";
    public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
    public bool IsReadOnly => true;
    public double LargeChange => 0;
    public double SmallChange => 0;
    public double Maximum => 100;
    public double Minimum => 0;
    public double Value => owner.Value;
    public void SetValue(double value) => throw new InvalidOperationException("Quota values are read-only.");
}
