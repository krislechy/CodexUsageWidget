using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CodexUsageWidget.Core;
using Microsoft.Win32;

namespace CodexUsageWidget;

public partial class SettingsWindow : Window
{
    public WidgetSettings Result { get; }
    private readonly bool _originalStartup;
    private readonly string _originalLanguage;
    private bool _initialized;

    public SettingsWindow(WidgetSettings settings)
    {
        _originalLanguage = Localization.Current.Language;
        InitializeComponent();
        Result = new WidgetSettings
        {
            CodexPath = settings.CodexPath, RefreshSeconds = settings.RefreshSeconds, Topmost = settings.Topmost,
            Compact = settings.Compact, Left = settings.Left, Top = settings.Top, Language = settings.Language
        };
        CodexPathBox.Text = settings.CodexPath ?? "";
        TopmostBox.IsChecked = settings.Topmost;
        _originalStartup = WidgetSettings.StartsWithWindows();
        StartupBox.IsChecked = _originalStartup;
        foreach (ComboBoxItem item in IntervalBox.Items)
            if (int.Parse((string)item.Tag) == settings.RefreshSeconds) IntervalBox.SelectedItem = item;
        LanguageBox.ItemsSource = Localization.Languages;
        LanguageBox.SelectedValue = _originalLanguage;
        _initialized = true;
        Closed += (_, _) => { if (DialogResult != true) Localization.Current.SetLanguage(_originalLanguage); };
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && LanguageBox.SelectedItem is LanguageOption language)
            Localization.Current.SetLanguage(language.Code);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Localization.Current["BrowseCli"], Filter = "Codex CLI|codex.exe;codex.cmd" };
        if (dialog.ShowDialog(this) == true) CodexPathBox.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = CodexPathBox.Text.Trim();
            if (path.Length > 0) _ = CodexLocator.Find(path);
            Result.CodexPath = path.Length == 0 ? null : path;
            Result.Topmost = TopmostBox.IsChecked == true;
            Result.RefreshSeconds = int.Parse((string)((ComboBoxItem)IntervalBox.SelectedItem).Tag);
            Result.Language = Localization.Current.Language;
            if ((StartupBox.IsChecked == true) != _originalStartup)
                WidgetSettings.SetStartWithWindows(StartupBox.IsChecked == true);
            Result.Save();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UsageUnavailableException or ArgumentException)
        {
            ErrorText.Text = Localization.Current["SettingsSaveError"];
        }
    }
}
