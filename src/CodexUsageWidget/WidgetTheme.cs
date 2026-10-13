using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CodexUsageWidget;

internal static class WidgetTheme
{
    internal static event Action? Changed;
    internal static bool TryColor(string? value, out Color color)
    {
        color = default;
        if (value is not { Length: 7 } || value[0] != '#' ||
            !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    internal static void Apply(WidgetSettings settings)
    {
        if (!TryColor(settings.FiveHourColor, out var pink) || !TryColor(settings.WeeklyColor, out var lime) ||
            !TryColor(settings.BackgroundColor, out var background)) return;
        var resources = Application.Current.Resources;
        void Set(string key, Color color) => resources[key] = new SolidColorBrush(color);
        Set("Pink", pink); Set("Lime", lime); Set("WidgetSurface", background);
        double Luminance(Color color)
        {
            double Channel(byte channel) { var value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var luminance = Luminance(background);
        var light = luminance > .179;
        var foreground = light ? Colors.Black : Colors.White;
        var muted = light ? Color.FromRgb(70, 75, 83) : Color.FromRgb(171, 176, 186);
        var mutedLuminance = Luminance(muted);
        var contrast = (Math.Max(luminance, mutedLuminance) + .05) / (Math.Min(luminance, mutedLuminance) + .05);
        Set("WidgetText", foreground);
        Set("WidgetMuted", contrast >= 4.5 ? muted : foreground);
        Color Track(Color accent) => Color.FromRgb((byte)(background.R * .8 + accent.R * .2),
            (byte)(background.G * .8 + accent.G * .2), (byte)(background.B * .8 + accent.B * .2));
        Set("PinkTrack", Track(pink)); Set("LimeTrack", Track(lime));
        Changed?.Invoke();
    }
}
