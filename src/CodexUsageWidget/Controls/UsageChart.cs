using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CodexUsageWidget.Core;

namespace CodexUsageWidget.Controls;

public sealed class UsageChart : FrameworkElement
{
    private IReadOnlyList<DailyUsage>? _daily;
    private int _days = 7;
    public void SetData(IReadOnlyList<DailyUsage>? daily, int days) { _daily = daily; _days = days; InvalidateVisual(); }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var muted = (Brush)Application.Current.FindResource("Muted");
        var accent = (Brush)Application.Current.FindResource("Pink");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var start = today.AddDays(1 - _days);
        var data = _daily?.Where(row => row.Date >= start && row.Date <= today).ToDictionary(row => row.Date, row => row.Tokens);
        var max = Math.Max(1, data?.Values.DefaultIfEmpty().Max() ?? 1);
        var baseline = Math.Max(0, ActualHeight - 16);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (data is null || data.Count == 0)
        {
            var label = new FormattedText(Localization.Current["ActivityUnavailable"], Localization.Current.Culture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(label, new Point(0, 24));
            return;
        }
        var slot = ActualWidth / _days;
        var scale = new FormattedText(max.ToString("N0", Localization.Current.Culture), Localization.Current.Culture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(scale, new Point(0, 0));
        for (var index = 0; index < _days; index++)
        {
            var x = index * slot + slot * .18;
            var width = Math.Max(1, slot * .64);
            if (data.TryGetValue(start.AddDays(index), out var tokens))
            {
                var height = Math.Max(2, tokens / (double)max * Math.Max(1, baseline - 18));
                context.DrawRoundedRectangle(accent, null, new Rect(x, baseline - height, width, height), 2, 2);
            }
            else context.DrawLine(new Pen(muted, 1) { DashStyle = DashStyles.Dot }, new Point(x, baseline), new Point(x + width, baseline));
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ActualWidth <= 0) return;
        var index = Math.Clamp((int)(e.GetPosition(this).X / ActualWidth * _days), 0, _days - 1);
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(1 - _days + index);
        var row = _daily?.FirstOrDefault(item => item.Date == date);
        ToolTip = date.ToString("d", Localization.Current.Culture) + " · " + (row is null
            ? Localization.Current["NotReported"] : row.Tokens.ToString("N0", Localization.Current.Culture));
    }
}
