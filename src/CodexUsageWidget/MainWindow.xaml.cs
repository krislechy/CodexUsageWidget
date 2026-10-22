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
    private readonly ContextMonitor _contextMonitor = new();
    private readonly DispatcherTimer _contextClock = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _readingContext;
    private async Task RefreshContextAsync()
    {
        if (_readingContext || _preview || _exiting || !IsVisible) return;
        _readingContext = true;
        var id = _settings.ContextThreadId;
        try { var value = await _contextMonitor.ReadAsync(id); if (id == _settings.ContextThreadId && !_exiting) _viewModel.SetContext(value); }
        finally { _readingContext = false; }
    }
    private WidgetSettings _settings;
    private ExtraInfoWindow? _extraInfo;
    private readonly QuotaAlerts _alerts = new();
    private QuotaNotificationWindow? _notification;
    private readonly UpdateController _updates;
    private Task<System.Collections.Generic.IReadOnlyList<ThreadChoice>> LoadContextSessionsAsync() =>
        _preview ? Task.FromResult<System.Collections.Generic.IReadOnlyList<ThreadChoice>>(
            [new("demo-chat", "Example conversation")]) :
        _monitor is null ? Task.FromException<System.Collections.Generic.IReadOnlyList<ThreadChoice>>(new IOException()) :
        SessionPicker.ReadAllAsync(_monitor.Client);

    private async void Context_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await SessionPicker.OpenAsync((FrameworkElement)sender, LoadContextSessionsAsync, _settings.ContextThreadId, choice =>
        {
            _settings.ContextThreadId = choice.Id;
            _viewModel.SetContext(null);
            if (!_preview) _settings.Save();
            _ = RefreshContextAsync();
        });
    }

    private void ExtraInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.IsInstalling || _exiting) return;
        if (_extraInfo is not null) { _extraInfo.Show(); _extraInfo.Activate(); return; }
        if (!_preview && _monitor is null) return;
        var window = new ExtraInfoWindow(_settings, _preview ? null : _monitor!.Client, _preview ? null : () => _settings.Save()) { Owner = this, Topmost = Topmost };
        window.ContextThreadSelected += id => { _settings.ContextThreadId = id; _viewModel.SetContext(null); if (!_preview) _settings.Save(); _ = RefreshContextAsync(); };
        _extraInfo = window;
        window.Closed += (_, _) => _extraInfo = null;
        window.Show();
    }

    private void ShowQuotaAlerts(UsageSnapshot snapshot)
    {
        if (_preview || _exiting) return;
        var alerts = _alerts.Evaluate(snapshot, _settings.LowQuotaAlerts, _settings.LowQuotaThreshold,
            _settings.ResetExpiryAlerts, _settings.ResetExpiryHours, DateTimeOffset.UtcNow);
        if (alerts.Count == 0) return;
        var text = Localization.Current;
        var messages = alerts.Where(alert => alert.Kind == "low").Select(alert => text.Format("LowQuotaAlertBody",
            text[alert.DurationMinutes == 300 ? "FiveHours" : "Weekly"], alert.RemainingPercent?.ToString("0.#", text.Culture))).ToList();
        var resets = alerts.Where(alert => alert.Kind == "expiry").ToArray();
        if (resets.Length > 0) messages.Add(text.Format("ResetExpiryAlertBody", resets.Length,
            resets.Min(alert => alert.Expiry)!.Value.ToLocalTime().ToString("g", text.Culture)));
        if (_notification is null)
        {
            var popup = new QuotaNotificationWindow(ShowWidget, TimeSpan.FromSeconds(_settings.NotificationSeconds));
            _notification = popup;
            popup.Closed += (_, _) => { if (ReferenceEquals(_notification, popup)) _notification = null; };
        }
        var heading = resets.Length == alerts.Count ? "ResetExpiryNotificationTitle" : resets.Length == 0 ? "LowQuotaNotificationTitle" : "QuotaNotificationTitle";
        _notification.ShowMessages(heading, messages);
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
        _updates = new UpdateController(() => _settings.AutoUpdates,
            () => !_exiting && !_changingSettings && !_resetAction && !_viewModel.PendingReset && _viewModel.CanRefresh && !OwnedWindows.Cast<Window>().Any(window => window.IsVisible),
            ExitAsync, preview);
        DataContext = _viewModel;
        _contextClock.Tick += async (_, _) => await RefreshContextAsync();
        if (!preview) _contextClock.Start();
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
                    if (_arguments.Contains("--context-only")) await CaptureContextAsync(output);
                    else await CapturePreviewsAsync(output);
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
        var updatedIndex = Array.IndexOf(_arguments, "--updated");
        if (updatedIndex >= 0 && updatedIndex + 1 < _arguments.Length)
        {
            try
            {
                var plan = UpdateInstallation.ReadPlan(Path.Combine(_arguments[updatedIndex + 1], "plan.json"));
                if (string.Equals(plan.InstallDirectory.TrimEnd(Path.DirectorySeparatorChar), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    UpdatePackages.ParseVersion(plan.Version) == UpdateController.CurrentVersion)
                    File.WriteAllText(Path.Combine(plan.JobDirectory, "app-ready"), "ready");
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        }
        _updates.Start();
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
        Height = _viewModel.IsCompact ? 148 : showResets ? 830 : 582;
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
        if (_resetAction || !_viewModel.CanReset || _updates.IsInstalling) return;
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
        if (_changingSettings || _resetAction || _exiting || _updates.IsInstalling) return;
        _changingSettings = true;
        var requestedUpdate = false;
        try
        {
            _extraInfo?.Close();
            var settings = new SettingsWindow(_settings, _updates, LoadContextSessionsAsync) { Owner = this, Topmost = Topmost };
            if (settings.ShowDialog() != true) return;
            var oldMonitor = _monitor;
            _monitor = null;
            if (oldMonitor is not null) await oldMonitor.DisposeAsync();
            _viewModel.SetBusy(false);
            if (_settings.ContextThreadId != settings.Result.ContextThreadId) _viewModel.SetContext(null);
            _settings = settings.Result;
            requestedUpdate = settings.UpdateRequested;
            Topmost = _settings.Topmost;
            UpdatePin();
            if (!_preview && !_exiting && !requestedUpdate) StartMonitor();
        }
        finally { _changingSettings = false; }
        if (requestedUpdate) _updates.RequestManualInstall();
        await _updates.TryInstallAsync();
        if (requestedUpdate && !_exiting && !_preview && _monitor is null) StartMonitor();
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
        _contextClock.Stop();
        _contextMonitor.Dispose();
        if (_exiting) return;
        _exiting = true;
        SaveSettings();
        _clock.Stop();
        _updates.Dispose();
        _extraInfo?.Close();
        Hide();
        _notification?.Close();
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

    private async Task CaptureContextAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        using (var titles = JsonDocument.Parse("{\"data\":[{\"id\":\"a\",\"name\":\"Русский чат\"},{\"id\":\"b\",\"name\":null,\"preview\":\"Кириллица без названия\"},{\"id\":\"c\",\"name\":\" \",\"preview\":\"Ещё один чат\"}]}"))
        {
            var page = ThreadPage.Parse(titles.RootElement);
            if (page.Threads[0].Name != "Русский чат" || page.Threads[1].Name != "Кириллица без названия" ||
                page.Threads[2].Name != "Ещё один чат")
                throw new InvalidOperationException("Cyrillic names and preview fallbacks must remain readable in the English UI.");
            var picker = new ExtraInfoWindow(new WidgetSettings(), null) { Owner = this };
            await picker.SelectThreadAsync(page.Threads[1]);
            if (((TextBlock)picker.ChooseThread.Content).Text != "Кириллица без названия")
                throw new InvalidOperationException("The conversation picker must display Cyrillic without transliteration.");
            picker.Close();
        }
        _viewModel.IsCompact = false;
        ApplyLayout();
        _viewModel.SetContext(new ContextUsage(72000, 100000));
        await Task.Delay(100);
        if (_viewModel.ContextPercent != 72) throw new InvalidOperationException("Context percentage mismatch.");
        Capture(Surface, Path.Combine(directory, "context-expanded.png"));
        _viewModel.IsCompact = true;
        ApplyLayout();
        await Task.Delay(100);
        Capture(Surface, Path.Combine(directory, "context-compact.png"));
        if (FindVisualChildren<TextBlock>(CompactContextArea).Any() ||
            FindVisualChildren<ProgressBar>(CompactContextArea).Single().Value != 72)
            throw new InvalidOperationException("Compact context must show only the mouse icon and scale, with no text or percentage.");
        _viewModel.SetContext(null);
        if (_viewModel.ContextPercent != 0 || !_viewModel.ContextLabel.Contains('—'))
            throw new InvalidOperationException("Switching to unknown context must clear the previous value.");
        var settings = new SettingsWindow(new WidgetSettings()) { Owner = this };
        settings.Show();
        settings.ContextColorBox.Text = "#38BDF8";
        if (!settings.SaveButton.IsEnabled || settings.Result.ContextColor != "#38BDF8" ||
            ((SolidColorBrush)FindResource("ContextAccent")).Color != Color.FromRgb(56, 189, 248))
            throw new InvalidOperationException("Context color preview failed.");
        settings.ContextColorBox.Text = "invalid";
        if (settings.SaveButton.IsEnabled) throw new InvalidOperationException("Invalid context color must block saving.");
        settings.ContextColorBox.Text = "#38BDF8";
        var copy = JsonSerializer.Deserialize<WidgetSettings>(JsonSerializer.Serialize(settings.Result))!;
        if (copy.ContextColor != "#38BDF8") throw new InvalidOperationException("Context color must persist.");
        settings.Close();
        if (((SolidColorBrush)FindResource("ContextAccent")).Color != Color.FromRgb(255, 159, 10))
            throw new InvalidOperationException("Cancelling color changes must restore orange.");
        var home = Path.Combine(directory, "fixture");
        var sessionId = Guid.NewGuid().ToString();
        Directory.CreateDirectory(Path.Combine(home, "sessions"));
        var fixture = Path.Combine(home, "sessions", $"rollout-{sessionId}.jsonl");
        var log = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"model_context_window\":100000,\"last_token_usage\":{\"total_tokens\":72000},\"total_token_usage\":{\"total_tokens\":999999}}}}";
        File.WriteAllText(fixture, log + "\n");
        var previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", home);
            using var monitor = new ContextMonitor();
            if ((await monitor.ReadAsync(sessionId))?.Percent != 72)
                throw new InvalidOperationException("Context must use the latest turn, not cumulative tokens.");
            if (await monitor.ReadAsync(Guid.NewGuid().ToString()) is not null)
                throw new InvalidOperationException("Session changes must clear context.");
            File.AppendAllText(fixture, log.Replace("72000", "12000") + "\n{partial");
            if ((await monitor.ReadAsync(sessionId))?.Percent != 12)
                throw new InvalidOperationException("Context must decrease after compaction and tolerate partial journal writes.");
            File.WriteAllText(fixture, log.Replace("72000", "84000") + "\n");
            if ((await monitor.ReadAsync(sessionId))?.Percent != 84)
                throw new InvalidOperationException("Old journal fixture must reproduce the stale 84% value.");
            var resumedDirectory = Path.Combine(home, "sessions", "next-day");
            Directory.CreateDirectory(resumedDirectory);
            var resumed = Path.Combine(resumedDirectory, $"rollout-{sessionId}_{Guid.NewGuid()}.jsonl");
            File.WriteAllText(resumed, log.Replace("72000", "12000") + "\n");
            ContextUsage? resumedUsage = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                resumedUsage = await monitor.ReadAsync(sessionId);
                if (resumedUsage?.Percent == 12) break;
                await Task.Delay(50);
            }
            if (resumedUsage?.Percent != 12)
                throw new InvalidOperationException("A running monitor must discover continuation journals and discard the old 84% cache even when lengths match.");
            using (var restarted = new ContextMonitor())
                if ((await restarted.ReadAsync(sessionId))?.Percent != 12)
                    throw new InvalidOperationException("A fresh monitor must prefer the latest continuation rather than the original journal.");
            File.AppendAllText(resumed, "{\"type\":\"compacted\",\"payload\":{}}\n");
            if (await monitor.ReadAsync(sessionId) is not null)
                throw new InvalidOperationException("A compaction boundary must clear stale usage until fresh counts arrive.");
            File.AppendAllText(resumed, log.Replace("72000", "6000") + "\n");
            if ((await monitor.ReadAsync(sessionId))?.Percent != 6)
                throw new InvalidOperationException("Fresh post-compaction context must replace the cleared reading.");
            File.WriteAllText(resumed, log.Replace("72000", "7000") + "\n");
            File.SetLastWriteTimeUtc(resumed, DateTime.UtcNow.AddSeconds(1));
            if ((await monitor.ReadAsync(sessionId))?.Percent != 7)
                throw new InvalidOperationException("Equal-length journal rewrites must invalidate the cached reading.");
            if (ContextUsage.ParseLogLine(log.Replace("100000", "0")) is not null ||
                ContextUsage.ParseLogLine("{}") is not null || ContextUsage.ParseLogLine("null") is not null)
                throw new InvalidOperationException("Invalid context metadata must remain unavailable.");
            var projectJson = "{\"local-projects\":{\"p\":{\"name\":\"Example project\"}},\"thread-project-assignments\":{\"a\":{\"projectId\":\"p\"}}}";
            File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), projectJson);
            System.Collections.Generic.IReadOnlyList<ThreadChoice> choices = [new("a", "Русский сеанс"), new("b", "Unassigned chat")];
            var groups = SessionPicker.GroupThreads(choices, projectJson);
            if (groups.Count != 2 || groups[0].Name != "Example project" || groups[1].Name != "No project")
                throw new InvalidOperationException("Project grouping must use membership and keep a separate No project group.");
            var loads = 0;
            Task<System.Collections.Generic.IReadOnlyList<ThreadChoice>> LoadChoices()
            {
                loads++;
                return Task.FromResult(choices);
            }
            string? selected = null;
            _viewModel.IsCompact = false; ApplyLayout();
            var first = (await SessionPicker.OpenAsync(FullContextArea, LoadChoices, "a", choice => selected = choice.Id))!;
            await Task.Delay(100);
            Capture(first, Path.Combine(directory, "context-session-projects.png"));
            var project = (MenuItem)first.Items[0];
            project.IsSubmenuOpen = true;
            await Task.Delay(100);
            var submenu = (Popup)project.Template.FindName("PART_Popup", project);
            if (!submenu.IsOpen || !project.IsChecked || !((MenuItem)project.Items[0]).IsChecked ||
                ((MenuItem)first.Items[1]).IsChecked)
                throw new InvalidOperationException("Project submenu and selected-session marker must be visible.");
            Capture((FrameworkElement)submenu.Child, Path.Combine(directory, "context-session-threads.png"));
            ((MenuItem)project.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (selected != "a") throw new InvalidOperationException("Session selection must invoke the matching conversation.");
            choices = [new("b", "Freshly updated session")];
            File.WriteAllText(Path.Combine(home, ".codex-global-state.json"), projectJson.Replace("Example project", "Renamed project").Replace("\"a\"", "\"b\""));
            var second = (await SessionPicker.OpenAsync(FullContextArea, LoadChoices, null, _ => { }))!;
            var updated = (TextBlock)((MenuItem)((MenuItem)second.Items[0]).Items[0]).Header;
            if (loads != 2 || updated.Text != "Freshly updated session" ||
                ((TextBlock)((MenuItem)second.Items[0]).Header).Text != "Renamed project")
                throw new InvalidOperationException("Each menu opening must load fresh sessions without caching.");
            second.IsOpen = false;
            var sessionSettings = new SettingsWindow(new WidgetSettings { ContextThreadId = "a" }, loadSessions: LoadChoices) { Owner = this };
            sessionSettings.Show();
            var settingsMenu = (await SessionPicker.OpenAsync(sessionSettings.ContextSessionButton, LoadChoices,
                sessionSettings.Result.ContextThreadId, choice => sessionSettings.Result.ContextThreadId = choice.Id))!;
            ((MenuItem)((MenuItem)settingsMenu.Items[0]).Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (sessionSettings.Result.ContextThreadId != "b")
                throw new InvalidOperationException("Settings must use the same grouped picker.");
            sessionSettings.Close();
            var datedProjects = "{\"local-projects\":{\"old\":{\"name\":\"A older project\",\"createdAt\":10},\"new\":{\"name\":\"Z newer project\",\"createdAt\":20}},\"thread-project-assignments\":{\"old-chat\":{\"projectId\":\"old\"},\"recent-chat\":{\"projectId\":\"new\"},\"earlier-chat\":{\"projectId\":\"new\"}}}";
            using var datedThreads = JsonDocument.Parse("{\"data\":[{\"id\":\"earlier-chat\",\"name\":\"Earlier chat\",\"updatedAt\":100},{\"id\":\"old-chat\",\"name\":\"Older project chat\",\"updatedAt\":9999},{\"id\":\"recent-chat\",\"name\":\"Recent chat\",\"recencyAt\":300,\"updatedAt\":150},{\"id\":\"unassigned\",\"name\":\"No project chat\",\"updatedAt\":99999}]}");
            var ordered = SessionPicker.GroupThreads(ThreadPage.Parse(datedThreads.RootElement).Threads, datedProjects);
            if (ordered[0].Key != "new" || ordered[1].Key != "old" || ordered[2].Key != "" ||
                ordered[0].Threads[0].Id != "recent-chat" || ordered[0].Threads[1].Id != "earlier-chat")
                throw new InvalidOperationException("Projects must sort by creation and chats by last use, newest first.");
            var selectedMenu = SessionPicker.Build(ordered, "recent-chat", _ => { });
            if (!((MenuItem)selectedMenu.Items[0]).IsChecked || ((MenuItem)selectedMenu.Items[1]).IsChecked)
                throw new InvalidOperationException("Only the selected session's project may have a checkmark.");
            var unassignedMenu = SessionPicker.Build(ordered, "unassigned", _ => { });
            if (!((MenuItem)unassignedMenu.Items[2]).IsChecked || ((MenuItem)unassignedMenu.Items[0]).IsChecked)
                throw new InvalidOperationException("No project must receive the checkmark for an unassigned selection.");
        }
        finally { Environment.SetEnvironmentVariable("CODEX_HOME", previousHome); }
        File.WriteAllText(Path.Combine(directory, "context-verification.txt"), "PASS: current context, missing metadata, session switching, compaction, partial writes, expanded/compact UI, color preview, persistence, validation and cancellation.");
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
        var duration = (ComboBox)settings.FindName("NotificationDuration");
        if ((string)((ComboBoxItem)duration.SelectedItem).Tag != "2" || duration.Items.Count != 3 || settings.Result.NotificationSeconds != 2)
            throw new InvalidOperationException("Notification duration must default to two seconds with only 2, 3 and 5 second choices.");
        var savedDuration = System.Text.Json.JsonSerializer.Deserialize<WidgetSettings>("{\"NotificationSeconds\":3}")!;
        var durationSettings = new SettingsWindow(savedDuration);
        if ((string)((ComboBoxItem)durationSettings.NotificationDuration.SelectedItem).Tag != "3" || durationSettings.Result.NotificationSeconds != 3)
            throw new InvalidOperationException("Notification duration must survive settings serialization and cloning.");
        durationSettings.Close();
        var constrainedSettings = new SettingsWindow(new WidgetSettings())
        {
            Owner = this,
            Height = WindowPlacement.ScreenFor(this)!.WorkingArea.Height / VisualTreeHelper.GetDpi(this).DpiScaleY + 100
        };
        constrainedSettings.Show();
        await Task.Delay(150);
        VerifyDialogCenter(constrainedSettings);
        if (!WindowPlacement.IsInside(constrainedSettings))
            throw new InvalidOperationException("An oversized settings dialog must fit the monitor's working area.");
        constrainedSettings.Close();
        Capture((FrameworkElement)settings.Content, Path.Combine(directory, "settings.png"));
        duration.IsDropDownOpen = true;
        await Task.Delay(100);
        var durationPopup = (System.Windows.Controls.Primitives.Popup)duration.Template.FindName("PART_Popup", duration);
        if (((FrameworkElement)durationPopup.Child).ActualHeight > 224)
            throw new InvalidOperationException("Notification duration choices must stay within a scrollable dropdown.");
        duration.IsDropDownOpen = false;
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
        var openedFromNotification = false;
        uint inputTimestamp = 1;
        var notification = new QuotaNotificationWindow(() => openedFromNotification = true) { InputTimestamp = () => inputTimestamp };
        notification.ShowMessages("LowQuotaNotificationTitle", ["5 hours: 8% remaining."]);
        await Task.Delay(100);
        if (notification.IsActive || !notification.PreventsActivation || notification.ShowInTaskbar || notification.Owner is not null)
            throw new InvalidOperationException("Custom notification must be independent and non-activating.");
        Hide();
        if (!notification.IsVisible) throw new InvalidOperationException("Notifications must remain visible while the widget is in the tray.");
        Capture(notification.NotificationSurface, Path.Combine(directory, "notification.png"));
        notification.ShowMessages("ResetExpiryNotificationTitle", ["1 reset expires soon. First expiry: Oct 18, 09:00."]);
        if (!notification.NotificationBody.Text.Contains("8%", StringComparison.Ordinal) || !notification.NotificationBody.Text.Contains("expires soon", StringComparison.Ordinal))
            throw new InvalidOperationException("New notifications must coalesce without losing visible messages.");
        notification.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        if (notification.DismissalScheduled) throw new InvalidOperationException("Hover must pause automatic dismissal.");
        inputTimestamp++;
        await Task.Delay(350);
        notification.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
        if (!notification.IsMouseOver && !notification.DismissalScheduled) throw new InvalidOperationException("Dismissal must resume after hover.");
        foreach (var screen in Forms.Screen.AllScreens)
        {
            notification.PlaceForPreview(screen);
            await Task.Delay(100);
            if (!WindowPlacement.IsInside(notification) || WindowPlacement.ScreenFor(notification)?.DeviceName != screen.DeviceName)
                throw new InvalidOperationException("Notification must fit the selected monitor's working area.");
        }
        Capture(notification.NotificationSurface, Path.Combine(directory, "notification-combined.png"));
        notification.NotificationBody.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
        if (!openedFromNotification || notification.IsVisible) throw new InvalidOperationException("Open widget must dismiss the notification and invoke its action.");
        var autoDismissed = false;
        var briefNotification = new QuotaNotificationWindow(() => { }, TimeSpan.FromMilliseconds(150)) { InputTimestamp = () => inputTimestamp };
        briefNotification.Closed += (_, _) => autoDismissed = true;
        briefNotification.ShowMessages("LowQuotaNotificationTitle", ["5 hours: 8% remaining."]);
        await Task.Delay(350);
        if (!briefNotification.IsVisible || briefNotification.DismissalScheduled)
            throw new InvalidOperationException("An unattended notification must wait for user activity.");
        inputTimestamp++;
        for (var activity = 0; activity < 6; activity++) { inputTimestamp++; await Task.Delay(100); }
        if (!briefNotification.IsMouseOver && !autoDismissed) throw new InvalidOperationException("A notification must dismiss after activity and its configured duration.");
        if (briefNotification.IsVisible) briefNotification.Close();
        var openedOnDismiss = false;
        var dismissible = new QuotaNotificationWindow(() => openedOnDismiss = true);
        dismissible.ShowMessages("ResetExpiryNotificationTitle", ["One reset expires soon."]);
        dismissible.DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (dismissible.IsVisible || dismissible.DismissalScheduled || openedOnDismiss) throw new InvalidOperationException("Manual dismissal must stop the timer without opening the widget.");
    }

    private void VerifyDialogCenter(Window dialog)
    {
        if (dialog.WindowStartupLocation != WindowStartupLocation.CenterOwner)
            throw new InvalidOperationException("Dialogs must declare CenterOwner startup placement.");
        var area = WindowPlacement.ScreenFor(this)!.WorkingArea;
        var ownerOrigin = PointToScreen(new Point());
        var origin = dialog.PointToScreen(new Point());
        var ownerDpi = VisualTreeHelper.GetDpi(this);
        var dialogDpi = VisualTreeHelper.GetDpi(dialog);
        var width = dialog.ActualWidth * dialogDpi.DpiScaleX;
        var height = dialog.ActualHeight * dialogDpi.DpiScaleY;
        var left = Math.Clamp(ownerOrigin.X + (ActualWidth * ownerDpi.DpiScaleX - width) / 2,
            area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(ownerOrigin.Y + (ActualHeight * ownerDpi.DpiScaleY - height) / 2,
            area.Top, Math.Max(area.Top, area.Bottom - height));
        if (Math.Abs(origin.X - left) > 2 || Math.Abs(origin.Y - top) > 2)
            throw new InvalidOperationException($"Dialog must be centered or clamped to its owner's working area. Expected ({left:0.##}, {top:0.##}), got ({origin.X:0.##}, {origin.Y:0.##}); size {width:0.##} × {height:0.##}, work area {area}.");
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
