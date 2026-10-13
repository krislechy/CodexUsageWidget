using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexUsageWidget.Core;
using CodexUsageWidget.Controls;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CodexUsageWidget;

public partial class MainWindow : Window
{
    private readonly WidgetViewModel _viewModel = new();
    private readonly bool _preview;
    private readonly string[] _arguments;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private WidgetSettings _settings;
    private ExtraInfoWindow? _extraInfo;
    private readonly QuotaAlerts _alerts = new();

    private void ExtraInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_extraInfo is not null) { _extraInfo.Show(); _extraInfo.Activate(); return; }
        if (!_preview && _monitor is null) return;
        var window = new ExtraInfoWindow(_settings, _preview ? null : _monitor!.Client, _preview ? null : () => _settings.Save()) { Owner = this, Topmost = Topmost };
        _extraInfo = window;
        window.Closed += (_, _) => _extraInfo = null;
        window.Show();
    }

    private void ShowQuotaAlerts(UsageSnapshot snapshot)
    {
        if (_tray is null || _preview) return;
        var alerts = _alerts.Evaluate(snapshot, _settings.LowQuotaAlerts, _settings.LowQuotaThreshold,
            _settings.ResetExpiryAlerts, _settings.ResetExpiryHours, DateTimeOffset.UtcNow);
        if (alerts.Count == 0) return;
        var text = Localization.Current;
        var messages = alerts.Where(alert => alert.Kind == "low").Select(alert => text.Format("LowQuotaAlertBody",
            text[alert.DurationMinutes == 300 ? "FiveHours" : "Weekly"], alert.RemainingPercent?.ToString("0.#", text.Culture))).ToList();
        var resets = alerts.Where(alert => alert.Kind == "expiry").ToArray();
        if (resets.Length > 0) messages.Add(text.Format("ResetExpiryAlertBody", resets.Length,
            resets.Min(alert => alert.Expiry)!.Value.ToLocalTime().ToString("g", text.Culture)));
        _tray.ShowBalloonTip(5000, "Codex Usage Widget", string.Join("\n", messages), Forms.ToolTipIcon.Info);
    }
    private UsageMonitor? _monitor;
    private Forms.NotifyIcon? _tray;
    private ContextMenu? _trayMenu;
    private bool _exiting;
    private bool _changingSettings;
    private bool _resetAction;
    private readonly PendingResetStore _pendingResets = new(WidgetSettings.PendingResetPath);

    public MainWindow(bool preview, string[] arguments)
    {
        _preview = preview;
        _arguments = arguments;
        _settings = preview ? new WidgetSettings() : WidgetSettings.Load();
        WidgetTheme.Apply(_settings);
        var languageIndex = Array.IndexOf(arguments, "--language");
        Localization.Current.SetLanguage(languageIndex >= 0 && languageIndex + 1 < arguments.Length
            ? arguments[languageIndex + 1] : _settings.Language);
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.IsCompact = arguments.Contains("--compact") || _settings.Compact;
        ApplyLayout();
        Topmost = _settings.Topmost;
        UpdatePin();
        if (_settings.Left is { } left && _settings.Top is { } top)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
            EnsureVisible();
        }
        _clock.Tick += (_, _) => _viewModel.Tick();
        IsVisibleChanged += (_, _) => UpdateClockState();
        StateChanged += (_, _) => UpdateClockState();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, EnsureVisible);
        _viewModel.PropertyChanged += (_, e) => { if (e.PropertyName is not (nameof(WidgetViewModel.FiveHourCountdown) or nameof(WidgetViewModel.SoonestExpiryText))) UpdateTray(); };
        Localization.Current.PropertyChanged += LanguageChanged;
        WidgetTheme.Changed += UpdatePin;
        if (!_preview) LoadPendingReset();
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdatePin();
        UpdateTray();
    }

    private void LoadPendingReset()
    {
        try { _viewModel.SetPendingReset(_pendingResets.Load() is not null); }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _viewModel.SetPendingReset(true, blocked: true);
            _viewModel.SetNotice("PendingStoreError");
        }
    }

    private void EnsureVisible() => WindowPlacement.KeepInside(this);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureVisible();
        UpdateClockState();
        if (_preview)
        {
            SetDemoSnapshot();
            var captureIndex = Array.IndexOf(_arguments, "--capture");
            if (captureIndex >= 0)
            {
                try
                {
                    if (captureIndex + 1 >= _arguments.Length) throw new ArgumentException("--capture requires an output directory.");
                    var output = Path.GetFullPath(_arguments[captureIndex + 1]);
                    Localization.Current.SetLanguage(_settings.Language);
                    await CapturePreviewsAsync(output);
                    Application.Current.Shutdown(0);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception.Message);
                    if (captureIndex + 1 < _arguments.Length)
                    {
                        var errorDirectory = Path.GetFullPath(_arguments[captureIndex + 1]);
                        Directory.CreateDirectory(errorDirectory);
                        File.WriteAllText(Path.Combine(errorDirectory, "capture-error.txt"), exception.ToString());
                    }
                    Application.Current.Shutdown(1);
                }
            }
            return;
        }
        CreateTray();
        StartMonitor();
    }

    private void StartMonitor()
    {
        var monitor = new UsageMonitor(new CodexClient(_settings.CodexPath), TimeSpan.FromSeconds(_settings.RefreshSeconds));
        _monitor = monitor;
        monitor.SnapshotReceived += snapshot => Dispatch(monitor, () => { _viewModel.SetSnapshot(snapshot); ShowQuotaAlerts(snapshot); });
        monitor.StatusChanged += status => Dispatch(monitor, () => _viewModel.SetError(status));
        monitor.BusyChanged += busy => Dispatch(monitor, () => _viewModel.SetBusy(busy));
        monitor.Start();
    }

    private void Dispatch(UsageMonitor source, Action action)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => { if (!_exiting && ReferenceEquals(source, _monitor)) action(); });
    }

    private void ApplyLayout()
    {
        Width = _viewModel.IsCompact ? 128 : 386;
        var showResets = ResetDisclosure.IsChecked == true;
        ResetSectionRow.Height = new GridLength(showResets ? 292 : 44);
        Height = _viewModel.IsCompact ? 128 : showResets ? 796 : 548;
        Surface.CornerRadius = new CornerRadius(_viewModel.IsCompact ? 22 : 26);
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        var screen = WindowPlacement.ScreenFor(this);
        _viewModel.IsCompact = !_viewModel.IsCompact;
        _settings.Compact = _viewModel.IsCompact;
        ApplyLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            WindowPlacement.KeepInside(this, screen);
            SaveSettings();
            ReplayVisibleRings();
        });
    }

    private void ResetDisclosure_Click(object sender, RoutedEventArgs e)
    {
        var screen = WindowPlacement.ScreenFor(this);
        ApplyLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            WindowPlacement.KeepInside(this, screen);
            SaveSettings();
        });
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && (_viewModel.IsCompact || IsInsideRingArea(e.OriginalSource as DependencyObject)))
        {
            Compact_Click(sender, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase or Thumb or ScrollBar) return;
            current = VisualTreeHelper.GetParent(current);
        }
        if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1)
        {
            DragMove();
            EnsureVisible();
            SaveSettings();
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        _settings.Topmost = Topmost;
        UpdatePin();
        SaveSettings();
    }

    private void UpdatePin()
    {
        PinButton.Foreground = (Brush)FindResource(Topmost ? "Lime" : "WidgetMuted");
        PinButton.ToolTip = Localization.Current[Topmost ? "PinOn" : "PinOff"];
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_resetAction || !_viewModel.CanReset) return;
        var selected = sender is Button { Tag: ResetEntry entry } ? entry : null;
        if (selected is not null && _viewModel.FindSelectableCredit(selected.Credit.Id) is null) return;
        _resetAction = true;
        var sent = false;
        try
        {
            var previous = _preview ? null : _pendingResets.Load();
            if (!_preview && _viewModel.PendingReset && previous is null)
                throw new IOException("Pending reset identity is missing.");
            if (previous is not null && selected is not null)
            {
                _viewModel.SetPendingReset(true);
                _viewModel.SetNotice("ResetPending");
                return;
            }
            var confirmationSelection = selected ?? (previous is null ? null :
                _viewModel.ResetEntries.FirstOrDefault(row => row.Credit.Id == previous.CreditId));
            var dialog = new ResetConfirmationWindow(_viewModel, confirmationSelection) { Owner = this, Topmost = Topmost };
            if (dialog.ShowDialog() != true) return;
            if (!_viewModel.CanReset) { _viewModel.SetNotice("RefreshToReset"); return; }
            var credit = selected is null ? _viewModel.NextResetCredit : _viewModel.FindSelectableCredit(selected.Credit.Id);
            if (selected is not null && credit is null) { _viewModel.SetNotice("RefreshToReset"); return; }
            if (_preview) { _viewModel.SetNotice("ResetSuccess"); return; }
            if (_monitor is null || _exiting) return;
            _viewModel.SetResetBusy(true);
            var request = previous ?? _pendingResets.Begin(credit?.Id
                ?? throw new IOException("Reset details are not available."));
            _viewModel.SetPendingReset(true);
            sent = true;
            var outcome = await _monitor.ConsumeResetAsync(request);
            _pendingResets.Complete();
            _viewModel.SetPendingReset(false);
            _viewModel.SetNotice(outcome switch
            {
                ResetOutcome.Reset => "ResetSuccess", ResetOutcome.AlreadyRedeemed => "ResetAlreadyApplied",
                ResetOutcome.NothingToReset => "NothingToReset", _ => "NoCredit"
            });
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or
            UsageUnavailableException or TimeoutException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            if (!_exiting)
            {
                LoadPendingReset();
                _viewModel.SetError(sent ? "ResetUncertain" : "PendingStoreError");
            }
        }
        finally { _resetAction = false; _viewModel.SetResetBusy(false); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) SetDemoSnapshot();
        else if (_monitor is not null) await _monitor.RefreshAsync();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_changingSettings || _resetAction) return;
        _changingSettings = true;
        try
        {
            _extraInfo?.Close();
            var settings = new SettingsWindow(_settings) { Owner = this, Topmost = Topmost };
            if (settings.ShowDialog() != true) return;
            var oldMonitor = _monitor;
            _monitor = null;
            if (oldMonitor is not null) await oldMonitor.DisposeAsync();
            _settings = settings.Result;
            Topmost = _settings.Topmost;
            UpdatePin();
            if (!_preview && !_exiting) StartMonitor();
        }
        finally { _changingSettings = false; }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (_preview) Application.Current.Shutdown();
        else { SaveSettings(); Hide(); }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.R)
        { Refresh_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.M)
        { Compact_Click(sender, new RoutedEventArgs()); e.Handled = true; }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting || _preview) return;
        e.Cancel = true;
        Hide_Click(this, new RoutedEventArgs());
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon { Icon = CreateIcon(), Visible = true, Text = "Codex Usage Widget" };
        _trayMenu = CreateTrayMenu();
        _tray.MouseClick += (_, e) => Dispatcher.Invoke(() =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowWidget();
            if (e.Button == Forms.MouseButtons.Right)
            {
                UpdateTray();
                _trayMenu.IsOpen = true;
            }
        });
        UpdateTray();
    }

    private void ShowWidget()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); EnsureVisible(); UpdateClockState(); Activate();
    }

    private void UpdateClockState()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            var resume = !_clock.IsEnabled;
            _viewModel.Tick();
            _clock.Start();
            if (resume) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ReplayVisibleRings);
        }
        else _clock.Stop();
    }

    private void ReplayVisibleRings()
    {
        foreach (var ring in FindVisualChildren<RingGauge>(Surface).Where(ring => ring.IsVisible)) ring.ReplayFill();
    }

    private bool IsInsideRingArea(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, FullRingArea)) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = this };
        void Add(string key, Action action)
        {
            var item = new MenuItem { Tag = key, Header = Localization.Current[key] };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("MenuShow", ShowWidget);
        Add("MenuMode", () => Compact_Click(this, new RoutedEventArgs()));
        Add("Settings", () => { ShowWidget(); Settings_Click(this, new RoutedEventArgs()); });
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        Add("MenuExit", ExitAsync);
        return menu;
    }

    private void UpdateTray()
    {
        if (_exiting) return;
        var text = $"Codex · {Localization.Current["FiveHours"]} {_viewModel.FiveHourRemaining} · {Localization.Current["Weekly"]} {_viewModel.WeeklyRemaining}";
        if (_viewModel.HasError) text += " · !";
        if (_tray is not null) _tray.Text = text.Length > 63 ? text[..63] : text;
        if (_trayMenu is { } menu)
            foreach (var item in menu.Items.OfType<MenuItem>())
                if (item.Tag is string key)
                {
                    item.Header = key == "ResetAction" ? $"{_viewModel.ResetButtonText} · {_viewModel.ResetAvailability}" :
                        key == "MenuMode" ? Localization.Current[_viewModel.IsCompact ? "ExpandView" : "CompactView"] : Localization.Current[key];
                    if (key == "ResetAction") item.IsEnabled = _viewModel.CanReset;
                    if (key == "RefreshShort") item.IsEnabled = _viewModel.CanRefresh;
                }
    }

    private void SaveSettings()
    {
        if (_preview) return;
        _settings.Left = Left;
        _settings.Top = Top;
        try { _settings.Save(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { _viewModel.SetError("Could not save local settings. Check folder permissions."); }
    }

    private async void ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        SaveSettings();
        _clock.Stop();
        _extraInfo?.Close();
        Hide();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }
        if (_trayMenu is not null) _trayMenu.IsOpen = false;
        if (_monitor is not null) await _monitor.DisposeAsync();
        Localization.Current.PropertyChanged -= LanguageChanged;
        WidgetTheme.Changed -= UpdatePin;
        _viewModel.Dispose();
        Application.Current.Shutdown();
    }

    private static Drawing.Icon CreateIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        using var icon = new Drawing.Icon(stream);
        return (Drawing.Icon)icon.Clone();
    }

    private void SetDemoSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        _viewModel.SetSnapshot(new UsageSnapshot(new QuotaWindow(73, 300, now.AddHours(3)), new QuotaWindow(11, 10080, now.AddDays(6)), now,
            new ResetCredits(3, [new("demo-first", now.AddDays(2)), new("demo-second", now.AddDays(8)), new("demo-third", now.AddDays(14))]), true));
    }

    private async Task CapturePreviewsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        SetDemoSnapshot();
        _viewModel.SetPendingReset(false);
        _viewModel.IsCompact = false;
        ApplyLayout();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        await Task.Delay(650);
        VerifyRingAccessibility();
        if (!_viewModel.CanReset || !_viewModel.ResetAvailability.Contains("3", StringComparison.Ordinal))
            throw new InvalidOperationException("Available reset state verification failed.");
        Capture(Surface, Path.Combine(directory, "main.png"));
        if (ResetDetailsPanel.IsVisible || ResetButton.IsVisible || ResetCountBadge.Text != "3")
            throw new InvalidOperationException("Collapsed resets must show only their heading and live count badge.");
        var collapsedHeight = Height;
        ResetDisclosure.IsChecked = true;
        ResetDisclosure_Click(this, new RoutedEventArgs());
        await Task.Delay(150);
        if (!ResetDetailsPanel.IsVisible || ResetEntriesList.ActualHeight < 1)
            throw new InvalidOperationException("Reset disclosure must reveal details and grow the window.");
        Capture(Surface, Path.Combine(directory, "resets-expanded.png"));
        var rowButtons = FindVisualChildren<Button>(ResetEntriesList).Where(button => button.Tag is ResetEntry).ToArray();
        if (rowButtons.Length != 3 || rowButtons.Any(button => !button.IsEnabled))
            throw new InvalidOperationException("Each available credit must have its own enabled action.");
        var chosen = (ResetEntry)rowButtons[1].Tag;
        if (_viewModel.FindSelectableCredit(chosen.Credit.Id)?.Id != "demo-second")
            throw new InvalidOperationException("Selecting a reset row must preserve its credit identity.");
        var confirmationSeen = false;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            var selectedDialog = Application.Current.Windows.OfType<ResetConfirmationWindow>().Single(window => window.Owner == this);
            var expiry = (TextBlock)selectedDialog.FindName("ExpiryText");
            if (expiry.Text != chosen.Expiry) throw new InvalidOperationException("Confirmation must identify the selected credit expiry.");
            if (!FindVisualChildren<Button>(selectedDialog).Any(button => button.IsDefault && button.IsCancel))
                throw new InvalidOperationException("Cancel must remain the confirmation default.");
            confirmationSeen = true;
            Capture((FrameworkElement)selectedDialog.Content, Path.Combine(directory, "selected-reset-confirmation.png"));
            selectedDialog.Close();
        });
        rowButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!confirmationSeen || _viewModel.ResetCountText != "3" || _resetAction)
            throw new InvalidOperationException("Cancel must leave every reset unspent.");
        _viewModel.SetPendingReset(true);
        await Task.Delay(100);
        if (FindVisualChildren<Button>(ResetEntriesList).Where(button => button.Tag is ResetEntry).Any(button => button.IsEnabled) || !ResetButton.IsVisible)
            throw new InvalidOperationException("Pending attempts must disable fresh row actions and expose the same-attempt retry.");
        var retryPosition = ResetButton.TransformToAncestor(ResetDetailsPanel).Transform(new Point());
        if (retryPosition.Y + ResetButton.ActualHeight > ResetDetailsPanel.ActualHeight + 1)
            throw new InvalidOperationException("Retry must remain inside the reset section.");
        Capture(Surface, Path.Combine(directory, "reset-pending-expanded.png"));
        _viewModel.SetPendingReset(false);
        ResetDisclosure.IsChecked = false;
        ResetDisclosure_Click(this, new RoutedEventArgs());
        await Task.Delay(100);
        if (ResetDetailsPanel.IsVisible || Height != collapsedHeight)
            throw new InvalidOperationException("Closing reset details must recover the space.");
        if (_viewModel.ResetEntries.Count != 3 || ResetEntriesList.Items.Count != 3)
            throw new InvalidOperationException("Every returned reset must be listed.");
        var validationSnapshot = _viewModel.Snapshot!;
        var nowForSelection = DateTimeOffset.UtcNow;
        _viewModel.SetSnapshot(validationSnapshot with { ObservedAt = nowForSelection, ResetCredits = new ResetCredits(3,
            [new("expired-selection", nowForSelection.AddSeconds(-1)), new("unknown-expiry", null), new("valid-selection", nowForSelection.AddDays(1))]), ResetCreditsIncluded = true });
        if (_viewModel.FindSelectableCredit("expired-selection") is not null || _viewModel.FindSelectableCredit("valid-selection") is null)
            throw new InvalidOperationException("Only current selectable rows may start a new reset.");
        SetDemoSnapshot();
        Compact_Click(this, new RoutedEventArgs());
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(100);
        VerifyRefillInProgress();
        Capture(Surface, Path.Combine(directory, "compact-refill.png"));
        await Task.Delay(550);
        if (FindVisualChildren<TextBlock>(CompactPanel).Any(text => text.IsVisible && !ReferenceEquals(text, CompactCountdown)) ||
            FindVisualChildren<RingGauge>(CompactPanel).Count() != 2)
            throw new InvalidOperationException("Compact mode must show only two rings and the countdown.");
        Capture(Surface, Path.Combine(directory, "compact.png"));
        var countdownBefore = CompactCountdown.Text;
        await Task.Delay(2100);
        if (CompactCountdown.Text == countdownBefore)
            throw new InvalidOperationException($"Countdown must update each second. Before={countdownBefore}, after={CompactCountdown.Text}, model={_viewModel.FiveHourCountdown}, timer={_clock.IsEnabled}.");
        Compact_Click(this, new RoutedEventArgs());
        if (!_viewModel.IsExpanded) throw new InvalidOperationException("Compact expansion failed.");
        _viewModel.IsCompact = false;
        ApplyLayout();
        _viewModel.SetError("ConnectionError");
        await Task.Delay(150);
        if (_viewModel.CanReset) throw new InvalidOperationException("Stale reset action should be disabled.");
        Capture(Surface, Path.Combine(directory, "stale.png"));
        _viewModel.SetPendingReset(true);
        if (!_viewModel.CanReset) throw new InvalidOperationException("A pending reset should permit an idempotent retry.");
        Capture(Surface, Path.Combine(directory, "reset-pending.png"));
        _viewModel.SetPendingReset(false);
        SetDemoSnapshot();
        var confirmation = new ResetConfirmationWindow(_viewModel) { Owner = this };
        confirmation.Show();
        await Task.Delay(100);
        VerifyDialogCenter(confirmation);
        if (!FindVisualChildren<Button>(confirmation).Any(button => button.IsDefault && button.IsCancel))
            throw new InvalidOperationException("Reset confirmation must default to cancel.");
        Capture((FrameworkElement)confirmation.Content, Path.Combine(directory, "reset-confirmation.png"));
        confirmation.Close();
        _viewModel.SetSnapshot(_viewModel.Snapshot! with { ObservedAt = DateTimeOffset.UtcNow, ResetCredits = new ResetCredits(0, []), ResetCreditsIncluded = true });
        if (_viewModel.CanReset) throw new InvalidOperationException("Zero available resets should disable the action.");
        await Task.Delay(100);
        if (ResetCountBadge.Text != "0") throw new InvalidOperationException("The badge must display the authoritative zero count.");
        Capture(Surface, Path.Combine(directory, "reset-empty.png"));
        SetDemoSnapshot();
        var settings = new SettingsWindow(new WidgetSettings { Language = Localization.Current.Language }) { Owner = this };
        settings.Show();
        await Task.Delay(150);
        VerifyDialogCenter(settings);
        Capture((FrameworkElement)settings.Content, Path.Combine(directory, "settings.png"));
        var selector = (System.Windows.Controls.ComboBox)settings.FindName("IntervalBox");
        selector.Focus();
        Keyboard.Focus(selector);
        await Task.Delay(100);
        Capture((FrameworkElement)settings.Content, Path.Combine(directory, "settings-focused.png"));
        var fiveColor = (System.Windows.Controls.TextBox)settings.FindName("FiveHourColorBox");
        var backgroundColor = (System.Windows.Controls.TextBox)settings.FindName("BackgroundColorBox");
        fiveColor.Text = "#38BDF8";
        backgroundColor.Text = "#F5F5F7";
        await Task.Delay(100);
        if (((SolidColorBrush)Surface.Background).Color != Color.FromRgb(245, 245, 247) ||
            ((SolidColorBrush)FindResource("Pink")).Color != Color.FromRgb(56, 189, 248))
            throw new InvalidOperationException("Custom colors must preview immediately.");
        Capture(Surface, Path.Combine(directory, "custom-colors.png"));
        fiveColor.Text = "#bad";
        if (((System.Windows.Controls.Button)settings.FindName("SaveButton")).IsEnabled)
            throw new InvalidOperationException("Malformed colors must block saving.");
        fiveColor.Text = "#38BDF8";
        var serializedColors = System.Text.Json.JsonSerializer.Deserialize<WidgetSettings>(System.Text.Json.JsonSerializer.Serialize(settings.Result))!;
        if (serializedColors.FiveHourColor != "#38BDF8" || serializedColors.BackgroundColor != "#F5F5F7")
            throw new InvalidOperationException("Custom colors must round-trip through settings JSON.");
        settings.Close();
        if (((SolidColorBrush)Surface.Background).Color != Color.FromRgb(20, 22, 25) ||
            ((SolidColorBrush)FindResource("Pink")).Color != Color.FromRgb(255, 57, 126))
            throw new InvalidOperationException("Cancel must restore the original colors.");
        var expiryNow = DateTimeOffset.UtcNow;
        if (WidgetViewModel.FormatExpiryLeadTime(expiryNow.AddDays(1.5), expiryNow) != "1 d" ||
            WidgetViewModel.FormatExpiryLeadTime(expiryNow.AddHours(23), expiryNow) != "23 h" ||
            WidgetViewModel.FormatExpiryLeadTime(expiryNow.AddHours(-1), expiryNow) != "0 h")
            throw new InvalidOperationException("Expiry summary must switch from days to hours and never be negative.");
        _viewModel.SetSnapshot(_viewModel.Snapshot! with { ObservedAt = DateTimeOffset.UtcNow,
            ResetCredits = new ResetCredits(2, [new ResetCredit("later", expiryNow.AddDays(5)), new ResetCredit("first", expiryNow.AddHours(22.5))]), ResetCreditsIncluded = true });
        await Task.Delay(100);
        if (_viewModel.ResetEntries[0].Credit.Id != "first" || SoonestExpiry.Text != "First expires in 23 h")
            throw new InvalidOperationException("The header must summarize the first expiring credit regardless of server order.");
        Capture(Surface, Path.Combine(directory, "reset-hours.png"));
        SetDemoSnapshot();
        var notice = new NoticeWindow { Owner = this };
        notice.Show();
        await Task.Delay(100);
        Capture((FrameworkElement)notice.Content, Path.Combine(directory, "already-running.png"));
        notice.Close();
        var extraInfo = new ExtraInfoWindow(new WidgetSettings(), null) { Owner = this };
        extraInfo.Show();
        await Task.Delay(150);
        VerifyDialogCenter(extraInfo);
        if (extraInfo.IsLoading || !((System.Windows.Controls.StackPanel)extraInfo.FindName("AccountRows")).Children.OfType<System.Windows.Controls.Grid>().Any())
            throw new InvalidOperationException("Extra info must show account data after opening.");
        Capture((FrameworkElement)extraInfo.Content, Path.Combine(directory, "extra-info.png"));
        foreach (var section in new[] { "Account", "TokenActivity" })
            ((System.Windows.Controls.Expander)extraInfo.FindName(section + "Section")).IsExpanded = false;
        ((System.Windows.Controls.Expander)extraInfo.FindName("ExtraQuotasSection")).IsExpanded = true;
        await Task.Delay(100);
        Capture((FrameworkElement)extraInfo.Content, Path.Combine(directory, "extra-quotas.png"));
        ((System.Windows.Controls.Expander)extraInfo.FindName("ExtraQuotasSection")).IsExpanded = false;
        ((System.Windows.Controls.Expander)extraInfo.FindName("ThreadDetailsSection")).IsExpanded = true;
        await extraInfo.SelectThreadAsync(new ThreadChoice("demo-chat", "Example conversation"));
        await Task.Delay(100);
        ((System.Windows.Controls.ScrollViewer)extraInfo.FindName("ExtraScroll")).ScrollToBottom();
        await Task.Delay(100);
        Capture((FrameworkElement)extraInfo.Content, Path.Combine(directory, "extra-thread.png"));
        extraInfo.Close();
        _trayMenu = CreateTrayMenu();
        UpdateTray();
        _trayMenu.Placement = PlacementMode.Center;
        _trayMenu.IsOpen = true;
        await Task.Delay(100);
        Capture(_trayMenu, Path.Combine(directory, "tray-menu.png"));
        if (_trayMenu.Items.OfType<MenuItem>().Any(item => item.Tag is "ResetAction" or "RefreshShort"))
            throw new InvalidOperationException("Tray menu must not expose reset or refresh actions.");
        _trayMenu.IsOpen = false;
        _viewModel.SetBusy(false);
        _trayMenu = null;
        var tooltip = new ToolTip { Content = _viewModel.CompactTooltip, PlacementTarget = this, Placement = PlacementMode.Center, IsOpen = true };
        await Task.Delay(100);
        Capture(tooltip, Path.Combine(directory, "tooltip.png"));
        tooltip.IsOpen = false;
        var now = DateTimeOffset.UtcNow;
        if (WidgetViewModel.FormatCountdown(now.AddSeconds(3661), now) != "01:01:01" ||
            WidgetViewModel.FormatCountdown(now.AddSeconds(-1), now) != "00:00:00")
            throw new InvalidOperationException("Countdown format or expiry boundary failed.");
        _viewModel.SetSnapshot(new UsageSnapshot(new QuotaWindow(0, 300, now.AddHours(3)), new QuotaWindow(100, 10080, now.AddDays(6)), now));
        await Task.Delay(600);
        var rings = FindVisualChildren<RingGauge>(ExpandedPanel).ToArray();
        if (rings.Length != 2 || rings[0].Value != 100 || rings[0].AnimatedValue != 100 || rings[1].Value != 0 || rings[1].AnimatedValue != 0)
            throw new InvalidOperationException("Full and empty rings must represent remaining quota.");
        Capture(Surface, Path.Combine(directory, "ring-boundaries.png"));
        var tab = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        RaiseEvent(tab);
        if (!tab.Handled) throw new InvalidOperationException("Tab navigation must be disabled.");
        Hide();
        if (_clock.IsEnabled) throw new InvalidOperationException("The countdown timer must stop while hidden.");
        ShowWidget();
        if (!_clock.IsEnabled) throw new InvalidOperationException("The countdown timer must resume when shown.");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(100);
        VerifyRefillInProgress();
        Capture(Surface, Path.Combine(directory, "tray-restore-refill.png"));
        await Task.Delay(550);
        foreach (var ring in FindVisualChildren<RingGauge>(Surface).Where(ring => ring.IsVisible))
            if (Math.Abs(ring.AnimatedValue - ring.Value) > 0.01)
                throw new InvalidOperationException("Refill must end at the authoritative percentage.");
        foreach (var screen in Forms.Screen.AllScreens)
        {
            _viewModel.IsCompact = true;
            ApplyLayout();
            await Task.Delay(100);
            WindowPlacement.MoveToCornerForPreview(this, screen);
            await Task.Delay(200);
            Compact_Click(this, new RoutedEventArgs());
            await Task.Delay(200);
            if (!WindowPlacement.IsInside(this) || WindowPlacement.ScreenFor(this)?.DeviceName != screen.DeviceName)
                throw new InvalidOperationException("Expanded widget must fit on the selected monitor.");
        }
    }

    private void VerifyDialogCenter(Window dialog)
    {
        if (Math.Abs(dialog.Left + dialog.ActualWidth / 2 - Left - ActualWidth / 2) > 1 ||
            Math.Abs(dialog.Top + dialog.ActualHeight / 2 - Top - ActualHeight / 2) > 1)
            throw new InvalidOperationException("Dialog is not centered on its owner.");
    }

    private void VerifyRefillInProgress()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        foreach (var ring in FindVisualChildren<RingGauge>(Surface).Where(ring => ring.IsVisible && ring.Value > 0))
            if (ring.AnimatedValue < 0 || ring.AnimatedValue >= ring.Value)
                throw new InvalidOperationException("Visible rings must replay their fill from zero.");
        VerifyRingAccessibility();
    }

    private void VerifyRingAccessibility()
    {
        foreach (var ring in FindVisualChildren<RingGauge>(Surface))
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(ring) ?? throw new InvalidOperationException("Ring automation peer is missing.");
            var provider = peer.GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
            if (provider is null || !provider.IsReadOnly || provider.Value != ring.Value ||
                !peer.GetName().Contains(Localization.Current.Format("AvailableValue", ring.Value.ToString("0.#", Localization.Current.Culture)), StringComparison.Ordinal))
                throw new InvalidOperationException("Ring accessibility verification failed.");
        }
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = element.ActualWidth;
        var height = element.ActualHeight;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("Surface"), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(VisualTreeHelper.GetOffset(element).X, VisualTreeHelper.GetOffset(element).Y, width, height)
            }, null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
