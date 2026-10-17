using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CodexUsageWidget;

public sealed class WidgetSettings
{
    public string? CodexPath { get; set; }
    public int RefreshSeconds { get; set; } = 60;
    public bool Topmost { get; set; } = true;
    public bool Compact { get; set; }
    public string Language { get; set; } = "en";
    public string FiveHourColor { get; set; } = "#FF397E";
    public string WeeklyColor { get; set; } = "#9BEF29";
    public string BackgroundColor { get; set; } = "#141619";
    public bool LowQuotaAlerts { get; set; }
    public int LowQuotaThreshold { get; set; } = 10;
    public bool ResetExpiryAlerts { get; set; }
    public int ResetExpiryHours { get; set; } = 24;
    public int NotificationSeconds { get; set; } = 5;
    public bool AutoUpdates { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget", "settings.json");
    public static string PendingResetPath => Path.Combine(Path.GetDirectoryName(SettingsPath)!, "pending-reset.json");

    public static WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var settings = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(SettingsPath)) ?? new();
            if (settings.RefreshSeconds is not (30 or 60 or 120 or 300)) settings.RefreshSeconds = 60;
            if (!System.Linq.Enumerable.Any(CodexUsageWidget.Core.Localization.Languages, item => item.Code == settings.Language)) settings.Language = "en";
            if (settings.Left is { } left && !double.IsFinite(left)) settings.Left = null;
            if (settings.Top is { } top && !double.IsFinite(top)) settings.Top = null;
            if (!WidgetTheme.TryColor(settings.FiveHourColor, out _)) settings.FiveHourColor = "#FF397E";
            if (!WidgetTheme.TryColor(settings.WeeklyColor, out _)) settings.WeeklyColor = "#9BEF29";
            if (!WidgetTheme.TryColor(settings.BackgroundColor, out _)) settings.BackgroundColor = "#141619";
            if (settings.LowQuotaThreshold is < 1 or > 99) settings.LowQuotaThreshold = 10;
            if (settings.ResetExpiryHours is < 1 or > 168) settings.ResetExpiryHours = 24;
            if (settings.NotificationSeconds is < 1 or > 60) settings.NotificationSeconds = 5;
            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        var path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    public static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("CodexUsageWidget") is string;
    }

    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            key.SetValue("CodexUsageWidget", $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue("CodexUsageWidget", throwOnMissingValue: false);
    }
}
