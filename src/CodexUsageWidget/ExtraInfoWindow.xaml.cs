using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public partial class ExtraInfoWindow : WidgetDialogWindow
{
    private readonly Localization _text = Localization.Current;
    private readonly CodexClient? _client;
    private readonly WidgetSettings _settings;
    private readonly Action? _save;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(5) };
    private ExtraInfoSnapshot? _snapshot;
    private ThreadDetails? _thread;
    private ThreadChoice? _selectedThread;
    private readonly List<ThreadChoice> _threads = [];
    private string? _cursor;
    private string? _threadSearch;
    private bool _busy;
    private bool _threadBusy;
    private int _accountRevision;
    private bool _reloadAgain;
    private int _days = 7;
    internal event Action<string>? ContextThreadSelected;
    internal bool IsLoading => _busy;

    public ExtraInfoWindow(WidgetSettings settings, CodexClient? client, Action? save = null)
    {
        _settings = settings; _client = client; _save = save;
        InitializeComponent();
        LowAlerts.IsChecked = settings.LowQuotaAlerts;
        ExpiryAlerts.IsChecked = settings.ResetExpiryAlerts;
        LowThreshold.Text = settings.LowQuotaThreshold.ToString(_text.Culture);
        ExpiryThreshold.Text = settings.ResetExpiryHours.ToString(_text.Culture);
        _text.PropertyChanged += LanguageChanged;
        ExtraScroll.SizeChanged += (_, _) => { Render(); RenderThread(); };
        if (client is not null) client.ExtraInfoInvalidated += AccountChanged;
        if (client is not null) client.ThreadTokensUpdated += ThreadTokensChanged;
        _refresh.Tick += async (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) await ReloadAsync(); };
        Loaded += async (_, _) =>
        {
            WindowPlacement.KeepInside(this, Owner is null ? null : WindowPlacement.ScreenFor(Owner));
            await ReloadAsync();
            if (_client is not null) _refresh.Start();
        };
        Closed += (_, _) =>
        {
            _lifetime.Cancel(); _refresh.Stop(); _text.PropertyChanged -= LanguageChanged;
            if (_client is not null) _client.ExtraInfoInvalidated -= AccountChanged;
            if (_client is not null) _client.ThreadTokensUpdated -= ThreadTokensChanged;
        };
    }

    private void LanguageChanged(object? sender, PropertyChangedEventArgs e) { Render(); RenderThread(); }
    private void AccountChanged() => Dispatcher.BeginInvoke(new Action(async () =>
    {
        if (_lifetime.IsCancellationRequested) return;
        _accountRevision++; _snapshot = null; _reloadAgain = _busy;
        foreach (var panel in new[] { AccountRows, UsageRows, QuotaRows, ResetRows, NoticeRows }) panel.Children.Clear();
        _selectedThread = null; _thread = null; _threads.Clear(); ThreadRows.Children.Clear();
        _cursor = null; MoreThreads.Visibility = Visibility.Collapsed; Chart.SetData(null, _days);
        ChooseThread.Content = _text["SelectThread"];
        if (IsVisible) await ReloadAsync();
    }));
    private void ThreadTokensChanged(string id, JsonElement tokens) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_lifetime.IsCancellationRequested || _selectedThread?.Id != id) return;
        _thread = new(_thread?.Usage, tokens); RenderThread();
    }));
    private async void Reload_Click(object sender, RoutedEventArgs e) => await ReloadAsync();
    internal async Task ReloadAsync()
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true; ReloadButton.IsEnabled = false; ExtraStatus.Text = _text["Updating"];
        var revision = _accountRevision;
        try
        {
            var snapshot = _client is null ? DemoSnapshot() : await _client.ReadExtraInfoAsync(_lifetime.Token);
            if (revision != _accountRevision || _lifetime.IsCancellationRequested) return;
            _snapshot = snapshot;
            Render();
            if (_selectedThread is { } selected) await SelectThreadAsync(selected);
            ExtraStatus.Text = _text.Format("UpdatedAt", _snapshot.FetchedAt.ToLocalTime().ToString("t", _text.Culture));
            if (_snapshot.UnavailableSections.Count > 0)
                ExtraStatus.Text += " · " + _text.Format("UnavailableSections", string.Join(", ", _snapshot.UnavailableSections.Select(key => _text[key])));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UsageUnavailableException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { ExtraStatus.Text = _text["ConnectionError"]; }
        finally
        {
            _busy = false; ReloadButton.IsEnabled = true;
            if (_reloadAgain && !_lifetime.IsCancellationRequested)
            { _reloadAgain = false; _ = Dispatcher.BeginInvoke(new Action(async () => await ReloadAsync())); }
        }
    }

    private string Number(long? value) => value?.ToString("N0", _text.Culture) ?? _text["NotReported"];
    private string Estimate(decimal? value) => value?.ToString("N4", _text.Culture) ?? _text["NotReported"];
    private string Date(DateTimeOffset? value) => value?.ToLocalTime().ToString("g", _text.Culture) ?? _text["NotReported"];
    private string State(bool? value) => _text[value is null ? "NotReported" : value.Value ? "Yes" : "No"];
    private void Row(StackPanel parent, string key, string? value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var columnWidth = Math.Max(100, (ExtraScroll.ActualWidth - 24) / 2);
        grid.Children.Add(new TextBlock { Text = _text[key], MaxWidth = columnWidth, Foreground = (Brush)FindResource("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 14, 0) });
        var text = new TextBlock { Text = value ?? _text["NotReported"], MaxWidth = columnWidth, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(text, 1); grid.Children.Add(text); parent.Children.Add(grid);
    }
    private void Note(StackPanel parent, string value) => parent.Children.Add(new TextBlock { Text = value, Foreground = (Brush)FindResource("Muted"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
    private void Heading(StackPanel parent, string value) => parent.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });

    private void Render()
    {
        if (_snapshot is not { } snapshot) return;
        foreach (var panel in new[] { AccountRows, UsageRows, QuotaRows, ResetRows, NoticeRows }) panel.Children.Clear();
        Row(AccountRows, "AuthType", snapshot.AccountType);
        Row(AccountRows, "Plan", snapshot.Plan ?? snapshot.Quotas.FirstOrDefault(quota => quota.Plan is not null)?.Plan);
        Row(AccountRows, "AuthRequired", State(snapshot.RequiresAuth));
        Row(AccountRows, "OrdinaryUsage", snapshot.OrdinaryUsageAllowed is null ? _text["NotReported"] : _text[snapshot.OrdinaryUsageAllowed.Value ? "Allowed" : "Blocked"]);
        var usage = snapshot.Usage;
        var today = usage?.Daily?.FirstOrDefault(day => day.Date == DateOnly.FromDateTime(DateTime.Today));
        Row(UsageRows, "TodayTokens", Number(today?.Tokens));
        foreach (var key in new[] { "lifetimeTokens", "peakDailyTokens", "currentStreakDays", "longestStreakDays", "longestRunningTurnSec" })
            Row(UsageRows, key, Number(usage?.Summary.GetValueOrDefault(key)));
        UpdateChart();
        if (snapshot.Quotas.Count == 0) Note(QuotaRows, _text["NotReported"]);
        foreach (var quota in snapshot.Quotas)
        {
            Heading(QuotaRows, quota.Name);
            Row(QuotaRows, "Plan", quota.Plan);
            Row(QuotaRows, "CreditsBalance", quota.Balance);
            Row(QuotaRows, "HasCredits", State(quota.HasCredits));
            Row(QuotaRows, "UnlimitedCredits", State(quota.Unlimited));
            foreach (var window in quota.Windows)
                Row(QuotaRows, window.DurationMinutes == 300 ? "FiveHours" : window.DurationMinutes == 10080 ? "Weekly" : "QuotaWindow",
                    _text.Format("ExtraQuotaValue", window.DurationMinutes, window.RemainingPercent.ToString("0.#", _text.Culture), Date(window.ResetsAt)));
            Row(QuotaRows, "LimitState", quota.ReachedType?.Replace('_', ' '));
            Row(QuotaRows, "SpendBlocked", State(quota.SpendBlocked));
            Row(QuotaRows, "SpendUsed", quota.SpendUsed);
            Row(QuotaRows, "SpendLimit", quota.SpendLimit);
            Row(QuotaRows, "SpendRemaining", quota.SpendRemaining is { } remaining ? remaining.ToString("0.#", _text.Culture) + "%" : null);
            Row(QuotaRows, "SpendResets", Date(quota.SpendResetsAt));
        }
        Note(QuotaRows, _text["CreditsHelp"]);
        if (snapshot.Resets?.Credits is not { Count: > 0 } credits) Note(ResetRows, _text["NotReported"]);
        else foreach (var credit in credits.OrderBy(credit => credit.ExpiresAt ?? DateTimeOffset.MaxValue))
        {
            Heading(ResetRows, credit.Title ?? _text["ResetCreditTitle"]);
            Row(ResetRows, "GrantedAt", Date(credit.GrantedAt));
            Row(ResetRows, "ExpiresAt", Date(credit.ExpiresAt));
            if (credit.Description is not null) Note(ResetRows, credit.Description);
        }
        if (snapshot.Resets?.ExpiryDetailsComplete == false) Note(ResetRows, _text["ResetsDetailsIncomplete"]);
        Row(NoticeRows, "MessagesEnabled", State(snapshot.MessagesEnabled));
        if (snapshot.Messages.Count == 0) Note(NoticeRows, _text[snapshot.MessagesEnabled == true ? "NoWorkspaceNotices" : "NotReported"]);
        foreach (var message in snapshot.Messages) { Note(NoticeRows, message.Body); Row(NoticeRows, "CreatedAt", Date(message.CreatedAt)); }
    }

    private void Week_Click(object sender, RoutedEventArgs e) { _days = 7; UpdateChart(); }
    private void Month_Click(object sender, RoutedEventArgs e) { _days = 30; UpdateChart(); }
    private void UpdateChart()
    {
        Chart.SetData(_snapshot?.Usage?.Daily, _days);
        WeekButton.IsEnabled = _days != 7; MonthButton.IsEnabled = _days != 30;
        var today = DateOnly.FromDateTime(DateTime.Today);
        ChartRange.Text = today.AddDays(1 - _days).ToString("d", _text.Culture) + " – " + today.ToString("d", _text.Culture);
    }

    private async void SearchThreads_Click(object sender, RoutedEventArgs e) => await LoadThreadsAsync(null);
    private async void MoreThreads_Click(object sender, RoutedEventArgs e) => await LoadThreadsAsync(_cursor);
    private async Task LoadThreadsAsync(string? cursor)
    {
        if (_threadBusy || _lifetime.IsCancellationRequested) return;
        _threadBusy = true; ChooseThread.IsEnabled = false; MoreThreads.IsEnabled = false;
        var revision = _accountRevision;
        if (cursor is null) _threadSearch = ThreadSearch.Text.Trim();
        try
        {
            var page = _client is null ? new ThreadPage([new("demo-chat", "Example conversation")], null)
                : await _client.ReadThreadsAsync(cursor, _threadSearch, _lifetime.Token);
            if (revision != _accountRevision || _lifetime.IsCancellationRequested) return;
            if (cursor is null) _threads.Clear();
            foreach (var choice in page.Threads) if (_threads.All(existing => existing.Id != choice.Id)) _threads.Add(choice);
            _cursor = page.Cursor; MoreThreads.Visibility = _cursor is null ? Visibility.Collapsed : Visibility.Visible;
            ShowThreadMenu();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UsageUnavailableException or TimeoutException or InvalidOperationException)
        { Note(ThreadRows, _text["ConnectionError"]); }
        finally { _threadBusy = false; ChooseThread.IsEnabled = true; MoreThreads.IsEnabled = true; }
    }
    private async void ChooseThread_Click(object sender, RoutedEventArgs e)
    {
        if (_threads.Count == 0) await LoadThreadsAsync(null); else ShowThreadMenu();
    }
    private void ShowThreadMenu()
    {
        var menu = new ContextMenu { PlacementTarget = ChooseThread, MaxHeight = 360, MaxWidth = 550 };
        if (_threads.Count == 0) menu.Items.Add(new MenuItem { Header = _text["NoThreads"], IsEnabled = false });
        foreach (var choice in _threads)
        {
            var item = new MenuItem { Header = new TextBlock { Text = choice.Name, MaxWidth = 480, TextTrimming = TextTrimming.CharacterEllipsis }, ToolTip = choice.Name };
            item.Click += async (_, _) => await SelectThreadAsync(choice);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    internal async Task SelectThreadAsync(ThreadChoice choice)
    {
        if (_threadBusy || _lifetime.IsCancellationRequested) return;
        _threadBusy = true; ChooseThread.IsEnabled = false;
        _selectedThread = choice;
        ContextThreadSelected?.Invoke(choice.Id);
        var revision = _accountRevision;
        ChooseThread.Content = new TextBlock { Text = choice.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        ChooseThread.ToolTip = choice.Name;
        ThreadRows.Children.Clear(); Note(ThreadRows, _text["Updating"]);
        try
        {
            var details = _client is null ? new ThreadDetails(DemoUsage(choice.Id), null) : await _client.ReadThreadDetailsAsync(choice.Id, _lifetime.Token);
            if (revision != _accountRevision || _lifetime.IsCancellationRequested || _selectedThread?.Id != choice.Id) return;
            _thread = details;
            RenderThread();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UsageUnavailableException or TimeoutException or InvalidOperationException)
        { ThreadRows.Children.Clear(); Note(ThreadRows, _text["ConnectionError"]); }
        finally { _threadBusy = false; ChooseThread.IsEnabled = true; }
    }
    private void RenderThread()
    {
        if (_thread is null) return;
        ThreadRows.Children.Clear();
        Row(ThreadRows, "EstimatedCredits", Estimate(_thread.Usage?.EstimatedCredits));
        Row(ThreadRows, "EstimatedUsd", Estimate(_thread.Usage?.EstimatedUsd));
        foreach (var group in _thread.Usage?.Groups ?? [])
        {
            Heading(ThreadRows, string.Join(" · ", new[] { group.Model, group.Effort, group.Speed }.Where(value => !string.IsNullOrWhiteSpace(value))));
            Row(ThreadRows, "TotalTokens", Number(group.Total)); Row(ThreadRows, "InputTokens", Number(group.Input));
            Row(ThreadRows, "CachedTokens", Number(group.Cached)); Row(ThreadRows, "NetInputTokens", Number(group.NetInput));
            Row(ThreadRows, "OutputTokens", Number(group.Output)); Row(ThreadRows, "EstimatedCredits", Estimate(group.EstimatedCredits));
        }
        foreach (var (label, field) in new[] { ("ContextWindow", "modelContextWindow"), ("ReasoningTokens", "reasoningOutputTokens"), ("LastTurnTokens", "totalTokens") })
        {
            long? value = null;
            if (_thread.Tokens is { } tokens)
            {
                var source = field == "modelContextWindow" ? tokens : tokens.TryGetProperty(field == "totalTokens" ? "last" : "total", out var breakdown) ? breakdown : default;
                if (source.ValueKind == JsonValueKind.Object && source.TryGetProperty(field, out var number) &&
                    number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out var amount) && amount >= 0) value = amount;
            }
            Row(ThreadRows, label, Number(value));
        }
    }

    private void SaveAlerts_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LowThreshold.Text, out var percent) || percent is < 1 or > 99 ||
            !int.TryParse(ExpiryThreshold.Text, out var hours) || hours is < 1 or > 168)
        { ExtraStatus.Text = _text["AlertsInvalid"]; return; }
        var previous = (_settings.LowQuotaAlerts, _settings.LowQuotaThreshold, _settings.ResetExpiryAlerts, _settings.ResetExpiryHours);
        try
        {
            _settings.LowQuotaAlerts = LowAlerts.IsChecked == true; _settings.LowQuotaThreshold = percent;
            _settings.ResetExpiryAlerts = ExpiryAlerts.IsChecked == true; _settings.ResetExpiryHours = hours;
            _save?.Invoke(); ExtraStatus.Text = _text["AlertsSaved"];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            (_settings.LowQuotaAlerts, _settings.LowQuotaThreshold, _settings.ResetExpiryAlerts, _settings.ResetExpiryHours) = previous;
            ExtraStatus.Text = _text["SettingsSaveError"];
        }
    }

    internal static UsageDetails DemoUsage(string? thread = null) => new(
        new Dictionary<string, long?> { ["lifetimeTokens"] = 1_284_500, ["peakDailyTokens"] = 145_700, ["currentStreakDays"] = 9, ["longestStreakDays"] = 21, ["longestRunningTurnSec"] = 540 },
        Enumerable.Range(0, 30).Select(index => new DailyUsage(DateOnly.FromDateTime(DateTime.Today).AddDays(index - 29), (index * 7919 % 70000) + 12000)).ToArray(),
        thread, thread is null ? null : 1.2345m, thread is null ? null : .1234m,
        thread is null ? [] : [new("example model", "high", "standard", 8400, 6000, 4000, 2000, 2400, 1.2345m)]);
    internal static ExtraInfoSnapshot DemoSnapshot() => new("chatgpt", "plus", true, true, DemoUsage(),
        [new("Codex", "plus", "12.50", true, false, false, null, "2.50", "15.00", 83, DateTimeOffset.UtcNow.AddDays(3),
            [new(73, 300, DateTimeOffset.UtcNow.AddHours(3)), new(11, 10080, DateTimeOffset.UtcNow.AddDays(6))])],
        new(1, [new("demo", DateTimeOffset.UtcNow.AddDays(9), DateTimeOffset.UtcNow.AddDays(-2), "Codex limit reset", "Example earned reset metadata")]),
        true, [new("Example workspace announcement", DateTimeOffset.UtcNow.AddHours(-1))], DateTimeOffset.UtcNow, []);
}
