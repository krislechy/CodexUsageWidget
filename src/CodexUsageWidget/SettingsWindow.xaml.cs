using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexUsageWidget.Core;
using Microsoft.Win32;

namespace CodexUsageWidget;

public partial class SettingsWindow : WidgetDialogWindow
{
    public WidgetSettings Result { get; }
    private readonly bool _originalStartup;
    private readonly string _originalLanguage;
    private bool _initialized;
    private readonly WidgetSettings _originalColors;
    private readonly UpdateController? _updates;
    public bool UpdateRequested { get; private set; }

    public SettingsWindow(WidgetSettings settings, UpdateController? updates = null)
    {
        _updates = updates;
        _originalLanguage = Localization.Current.Language;
        _originalColors = new WidgetSettings { FiveHourColor = settings.FiveHourColor, WeeklyColor = settings.WeeklyColor, BackgroundColor = settings.BackgroundColor };
        InitializeComponent();
        Result = new WidgetSettings
        {
            CodexPath = settings.CodexPath, RefreshSeconds = settings.RefreshSeconds, Topmost = settings.Topmost,
            Compact = settings.Compact, Left = settings.Left, Top = settings.Top, Language = settings.Language,
            FiveHourColor = settings.FiveHourColor, WeeklyColor = settings.WeeklyColor, BackgroundColor = settings.BackgroundColor,
            LowQuotaAlerts = settings.LowQuotaAlerts, LowQuotaThreshold = settings.LowQuotaThreshold,
            ResetExpiryAlerts = settings.ResetExpiryAlerts, ResetExpiryHours = settings.ResetExpiryHours,
            NotificationSeconds = settings.NotificationSeconds,
            AutoUpdates = settings.AutoUpdates
        };
        CodexPathBox.Text = settings.CodexPath ?? "";
        TopmostBox.IsChecked = settings.Topmost;
        _originalStartup = WidgetSettings.StartsWithWindows();
        StartupBox.IsChecked = _originalStartup;
        foreach (ComboBoxItem item in IntervalBox.Items)
            if (int.Parse((string)item.Tag) == settings.RefreshSeconds) IntervalBox.SelectedItem = item;
        LanguageBox.ItemsSource = Localization.Languages;
        LanguageBox.SelectedValue = _originalLanguage;
        FiveHourColorBox.Text = settings.FiveHourColor;
        WeeklyColorBox.Text = settings.WeeklyColor;
        BackgroundColorBox.Text = settings.BackgroundColor;
        AutoUpdatesBox.IsChecked = settings.AutoUpdates;
        NotificationDuration.ItemsSource = System.Linq.Enumerable.Range(1, 60);
        NotificationDuration.SelectedItem = settings.NotificationSeconds;
        if (updates is not null) UpdatesPanel.DataContext = updates;
        else
        {
            CheckUpdatesButton.IsEnabled = false; InstallUpdateButton.IsEnabled = false;
            UpdateStatus.Text = Localization.Current.Format("UpdateInstalledVersion", UpdateController.CurrentVersion.ToString(3), "Preview");
        }
        _initialized = true;
        Colors_Changed(this, null!);
        Loaded += (_, _) => WindowPlacement.KeepInside(this, Owner is null ? null : WindowPlacement.ScreenFor(Owner));
        Closed += (_, _) => { if (DialogResult != true) { Localization.Current.SetLanguage(_originalLanguage); WidgetTheme.Apply(_originalColors); } };
    }

    private void Colors_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        var valid = WidgetTheme.TryColor(FiveHourColorBox.Text.Trim(), out var five) &
            WidgetTheme.TryColor(WeeklyColorBox.Text.Trim(), out var weekly) &
            WidgetTheme.TryColor(BackgroundColorBox.Text.Trim(), out var background);
        SaveButton.IsEnabled = valid;
        ColorError.Text = valid ? "" : Localization.Current["ColorInvalid"];
        if (!valid) return;
        FiveHourSwatch.Background = new SolidColorBrush(five);
        WeeklySwatch.Background = new SolidColorBrush(weekly);
        BackgroundSwatch.Background = new SolidColorBrush(background);
        Result.FiveHourColor = FiveHourColorBox.Text.Trim().ToUpperInvariant();
        Result.WeeklyColor = WeeklyColorBox.Text.Trim().ToUpperInvariant();
        Result.BackgroundColor = BackgroundColorBox.Text.Trim().ToUpperInvariant();
        WidgetTheme.Apply(Result);
    }

    private void ColorPalette_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var box = (TextBox)FindName((string)button.Tag);
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var hex in new[] { "#FF397E", "#9BEF29", "#38BDF8", "#A78BFA", "#FF9F0A", "#141619", "#30343B", "#F5F5F7" })
        {
            WidgetTheme.TryColor(hex, out var color);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new System.Windows.Shapes.Rectangle { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = hex });
            var item = new MenuItem { Header = row };
            item.Click += (_, _) => box.Text = hex;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void DefaultColors_Click(object sender, RoutedEventArgs e)
    {
        FiveHourColorBox.Text = "#FF397E";
        WeeklyColorBox.Text = "#9BEF29";
        BackgroundColorBox.Text = "#141619";
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
        if (!SaveButton.IsEnabled) return;
        try
        {
            var path = CodexPathBox.Text.Trim();
            if (path.Length > 0) _ = CodexLocator.Find(path);
            Result.CodexPath = path.Length == 0 ? null : path;
            Result.Topmost = TopmostBox.IsChecked == true;
            Result.NotificationSeconds = (int)NotificationDuration.SelectedItem;
            Result.RefreshSeconds = int.Parse((string)((ComboBoxItem)IntervalBox.SelectedItem).Tag);
            Result.Language = Localization.Current.Language;
            Result.AutoUpdates = AutoUpdatesBox.IsChecked == true;
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

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updates is not null) await _updates.CheckNowAsync();
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled || _updates is null) return;
        if (await _updates.PrepareManualAsync())
        {
            if (!IsLoaded || !IsVisible) return;
            UpdateRequested = true;
            Save_Click(sender, e);
        }
    }
}
