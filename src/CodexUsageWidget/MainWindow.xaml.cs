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
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    private WidgetSettings _settings;
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
        var languageIndex = Array.IndexOf(arguments, "--language");
        Localization.Current.SetLanguage(languageIndex >= 0 && languageIndex + 1 < arguments.Length
            ? arguments[languageIndex + 1] : _settings.Language);
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.IsCompact = arguments.Contains("--compact") || _settings.Compact;
        ApplyLayout();
        Topmost = _settings.Topmost;
        UpdatePin();
        var area = SystemParameters.WorkArea;
        Left = _settings.Left ?? area.Right - Width - 24;
        Top = _settings.Top ?? area.Bottom - Height - 24;
        EnsureVisible();
        _clock.Tick += (_, _) => _viewModel.Tick();
        _viewModel.PropertyChanged += (_, _) => UpdateTray();
        Localization.Current.PropertyChanged += LanguageChanged;
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

    private void EnsureVisible()
    {
        var window = new Rect(Left, Top, Width, Height);
        var visible = Forms.Screen.AllScreens.Any(screen =>
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var area = screen.WorkingArea;
            var rect = new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
            var intersection = Rect.Intersect(window, rect);
            return !intersection.IsEmpty && intersection.Width >= Math.Min(Width, 120) && intersection.Height >= Math.Min(Height, 80);
        });
        if (!visible)
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - Width - 24);
            Top = Math.Max(area.Top, area.Bottom - Height - 24);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
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
                    if (_arguments.Contains("--all-languages"))
                        foreach (var language in Localization.Languages)
                        {
                            Localization.Current.SetLanguage(language.Code);
                            await CapturePreviewsAsync(Path.Combine(output, "locales", language.Code));
                        }
                    Localization.Current.SetLanguage(_settings.Language);
                    await CapturePreviewsAsync(output);
                    Application.Current.Shutdown(0);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception.Message);
                    Application.Current.Shutdown(1);
                }
            }
            return;
        }
        CreateTray();
        _clock.Start();
        StartMonitor();
    }

    private void StartMonitor()
    {
        var monitor = new UsageMonitor(new CodexClient(_settings.CodexPath), TimeSpan.FromSeconds(_settings.RefreshSeconds));
        _monitor = monitor;
        monitor.SnapshotReceived += snapshot => Dispatch(monitor, () => _viewModel.SetSnapshot(snapshot));
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
        Width = _viewModel.IsCompact ? 104 : 386;
        Height = _viewModel.IsCompact ? 104 : 598;
        Surface.CornerRadius = new CornerRadius(_viewModel.IsCompact ? 22 : 26);
    }

    private void Compact_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsCompact = !_viewModel.IsCompact;
        _settings.Compact = _viewModel.IsCompact;
        ApplyLayout();
        EnsureVisible();
        SaveSettings();
    }

    private void Compact_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Compact_Click(sender, new RoutedEventArgs()); e.Handled = true; }
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase) return;
            current = VisualTreeHelper.GetParent(current);
        }
        if (e.LeftButton == MouseButtonState.Pressed && e.ClickCount == 1)
        {
            DragMove();
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
        PinButton.Foreground = (Brush)FindResource(Topmost ? "Lime" : "Muted");
        PinButton.ToolTip = Localization.Current[Topmost ? "PinOn" : "PinOff"];
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_resetAction || !_viewModel.CanReset) return;
        _resetAction = true;
        var sent = false;
        try
        {
            var dialog = new ResetConfirmationWindow(_viewModel) { Owner = this, Topmost = Topmost };
            if (dialog.ShowDialog() != true) return;
            if (!_viewModel.CanReset) { _viewModel.SetNotice("RefreshToReset"); return; }
            if (_preview) { _viewModel.SetNotice("ResetSuccess"); return; }
            if (_monitor is null || _exiting) return;
            _viewModel.SetResetBusy(true);
            var request = _pendingResets.Load() ?? _pendingResets.Begin(_viewModel.NextResetCredit?.Id
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

    private void ShowWidget() { EnsureVisible(); Show(); Activate(); }

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
        Add("RefreshShort", () => Refresh_Click(this, new RoutedEventArgs()));
        Add("Settings", () => { ShowWidget(); Settings_Click(this, new RoutedEventArgs()); });
        Add("ResetAction", () => { ShowWidget(); Reset_Click(this, new RoutedEventArgs()); });
        menu.Items.Add(new Separator());
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
        _viewModel.IsCompact = true;
        ApplyLayout();
        await Task.Delay(150);
        if (FindVisualChildren<TextBlock>(CompactPanel).Any(text => text.IsVisible) ||
            FindVisualChildren<RingGauge>(CompactPanel).Count() != 2)
            throw new InvalidOperationException("Compact mode must show only two rings.");
        Capture(Surface, Path.Combine(directory, "compact.png"));
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
        var originalLanguage = Localization.Current.Language;
        var languageBox = (System.Windows.Controls.ComboBox)settings.FindName("LanguageBox");
        languageBox.SelectedValue = originalLanguage == "de" ? "fr" : "de";
        if (Localization.Current.Language == originalLanguage)
            throw new InvalidOperationException("Language selection did not update the interface.");
        settings.Close();
        if (Localization.Current.Language != originalLanguage)
            throw new InvalidOperationException("Cancel did not restore the language.");
        var notice = new NoticeWindow { Owner = this };
        notice.Show();
        await Task.Delay(100);
        VerifyDialogCenter(notice);
        Capture((FrameworkElement)notice.Content, Path.Combine(directory, "already-running.png"));
        notice.Close();
        _trayMenu = CreateTrayMenu();
        UpdateTray();
        _trayMenu.Placement = PlacementMode.Center;
        _trayMenu.IsOpen = true;
        await Task.Delay(100);
        Capture(_trayMenu, Path.Combine(directory, "tray-menu.png"));
        _viewModel.SetBusy(true);
        if (_trayMenu.Items.OfType<MenuItem>().Where(item => item.Tag is "ResetAction" or "RefreshShort").Any(item => item.IsEnabled))
            throw new InvalidOperationException("Busy tray actions should be disabled.");
        _trayMenu.IsOpen = false;
        _viewModel.SetBusy(false);
        _trayMenu = null;
        var now = DateTimeOffset.UtcNow;
        _viewModel.SetSnapshot(new UsageSnapshot(new QuotaWindow(0, 300, now.AddHours(3)), new QuotaWindow(100, 10080, now.AddDays(6)), now));
        await Task.Delay(600);
        var rings = FindVisualChildren<RingGauge>(ExpandedPanel).ToArray();
        if (rings.Length != 2 || rings[0].Value != 100 || rings[0].AnimatedValue != 100 || rings[1].Value != 0 || rings[1].AnimatedValue != 0)
            throw new InvalidOperationException("Full and empty rings must represent remaining quota.");
        Capture(Surface, Path.Combine(directory, "ring-boundaries.png"));
    }

    private void VerifyDialogCenter(Window dialog)
    {
        if (Math.Abs(dialog.Left + dialog.ActualWidth / 2 - Left - ActualWidth / 2) > 1 ||
            Math.Abs(dialog.Top + dialog.ActualHeight / 2 - Top - ActualHeight / 2) > 1)
            throw new InvalidOperationException("Dialog is not centered on its owner.");
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
