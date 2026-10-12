using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace CodexUsageWidget.Core;

public sealed record LanguageOption(string Code, string NativeName, string CultureName);

/// <summary>Small embedded catalogs shared by WPF bindings and formatting. English is the explicit default.</summary>
public sealed class Localization : INotifyPropertyChanged
{
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("en", "English", "en-US"), new("ru", "Русский", "ru-RU"), new("uk", "Українська", "uk-UA"),
        new("sr", "Српски", "sr-Cyrl-RS"), new("fr", "Français", "fr-FR"), new("de", "Deutsch", "de-DE"),
        new("zh-CN", "简体中文", "zh-CN"), new("ja", "日本語", "ja-JP")
    ];
    public static Localization Current { get; } = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _catalogs = [];
    private string _language = "en";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language => _language;
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Languages.First(item => item.Code == _language).CultureName);
    public string CultureName => Culture.Name;
    public string this[string key] => Catalog(_language).TryGetValue(key, out var value) ? value : Catalog("en")[key];
    public string Format(string key, params object?[] values) => string.Format(Culture, this[key], values);

    public void SetLanguage(string? code)
    {
        var selected = Languages.FirstOrDefault(item => item.Code == code)?.Code ?? "en";
        if (selected == _language) return;
        _language = selected;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public IReadOnlyDictionary<string, string> Catalog(string code)
    {
        if (_catalogs.TryGetValue(code, out var existing)) return existing;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CodexUsageWidget.Core.Locales.{code}.json")
            ?? throw new InvalidOperationException($"Missing locale catalog: {code}");
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Invalid locale catalog: {code}");
        _catalogs[code] = catalog;
        return catalog;
    }
}
