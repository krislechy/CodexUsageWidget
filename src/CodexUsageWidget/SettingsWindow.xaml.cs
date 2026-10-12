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

    public SettingsWindow(WidgetSettings settings)
    {
        InitializeComponent();
        Result = new WidgetSettings
        {
            CodexPath = settings.CodexPath, RefreshSeconds = settings.RefreshSeconds, Topmost = settings.Topmost,
            Compact = settings.Compact, Left = settings.Left, Top = settings.Top
        };
        CodexPathBox.Text = settings.CodexPath ?? "";
        TopmostBox.IsChecked = settings.Topmost;
        _originalStartup = WidgetSettings.StartsWithWindows();
        StartupBox.IsChecked = _originalStartup;
        foreach (ComboBoxItem item in IntervalBox.Items)
            if (int.Parse((string)item.Tag) == settings.RefreshSeconds) IntervalBox.SelectedItem = item;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Codex CLI", Filter = "Codex CLI|codex.exe;codex.cmd|Executable files|*.exe;*.cmd" };
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
            if ((StartupBox.IsChecked == true) != _originalStartup)
                WidgetSettings.SetStartWithWindows(StartupBox.IsChecked == true);
            Result.Save();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UsageUnavailableException or ArgumentException)
        {
            ErrorText.Text = "Не удалось сохранить настройки. Проверьте путь к Codex CLI и права доступа.";
        }
    }
}
