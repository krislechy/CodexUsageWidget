using System;
using System.ComponentModel;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public sealed class WidgetViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly Localization _text = Localization.Current;
    private UsageSnapshot? _snapshot;
    private string? _errorKey;
    private string? _noticeKey;
    private bool _busy;
    private bool _resetBusy;
    private bool _pendingReset;
    private bool _resetRecoveryBlocked;
    private bool _compact;
    public event PropertyChangedEventHandler? PropertyChanged;

    public WidgetViewModel() => _text.PropertyChanged += OnLanguageChanged;
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => NotifyAll();
    public UsageSnapshot? Snapshot => _snapshot;
    public double FiveHourAvailable => _snapshot?.FiveHour?.RemainingPercent ?? 0;
    public double WeeklyAvailable => _snapshot?.Weekly?.RemainingPercent ?? 0;
    public string FiveHourRemaining => Percent(_snapshot?.FiveHour?.RemainingPercent);
    public string WeeklyRemaining => Percent(_snapshot?.Weekly?.RemainingPercent);
    public string FiveHourReset => Reset(_snapshot?.FiveHour);
    public string FiveHourCountdown => _snapshot?.FiveHour?.ResetsAt is { } reset
        ? FormatCountdown(reset, DateTimeOffset.UtcNow) : "--:--:--";
    internal static string FormatCountdown(DateTimeOffset reset, DateTimeOffset now)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, (reset - now).TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }
    public string WeeklyReset => Reset(_snapshot?.Weekly);
    public bool CanRefresh => !_busy && !_resetBusy;
    private bool SnapshotExpired => _snapshot is not null && DateTimeOffset.UtcNow - _snapshot.ObservedAt > TimeSpan.FromMinutes(6);
    public bool HasError => _errorKey is not null || SnapshotExpired;
    public string StatusText => _busy || _resetBusy ? _text["Updating"] : HasError
        ? _text[_snapshot is null ? "NoData" : "Stale"]
        : _noticeKey is not null ? _text[_noticeKey]
        : _snapshot is null ? _text["Connecting"] : _text.Format("UpdatedAt", _snapshot.ObservedAt.ToLocalTime().ToString("t", _text.Culture));
    public string StatusDetails => (_noticeKey is null ? "" : _text[_noticeKey] + "\n") +
        (_errorKey is not null ? _text[_errorKey] : SnapshotExpired ? _text["Stale"] + "\n" + Freshness : _text["StatusHelp"]);
    public string Freshness => _snapshot is null ? "" : _text.Format("LastData", _snapshot.ObservedAt.ToLocalTime().ToString("g", _text.Culture));
    public string CompactTooltip => $"{FiveHourAccessible}\n{WeeklyAccessible}\n{ResetAvailability}\n{ResetExpiry}\n{StatusText}\n{StatusDetails}";
    public bool IsCompact { get => _compact; set { _compact = value; NotifyAll(); } }
    public bool IsExpanded => !_compact;
    public string FiveHourAccessible => _text.Format("QuotaDescription", _text["FiveHours"], FiveHourRemaining, FiveHourReset);
    public string WeeklyAccessible => _text.Format("QuotaDescription", _text["Weekly"], WeeklyRemaining, WeeklyReset);
    public string ResetAvailability => _snapshot?.ResetCredits is not { } credits ? _text["ResetsUnknown"]
        : credits.AvailableCount == 0 ? _text["NoResets"] : _text.Format("AvailableResets", credits.AvailableCount);
    public string ResetExpiry => _snapshot?.ResetCredits is not { } credits || credits.AvailableCount == 0 ? ""
        : credits.EarliestKnownExpiry is not { } expiry ? _text["ExpiryUnknown"]
        : _text.Format(credits.ExpiryDetailsComplete ? "ExpiresFirst" : "KnownExpiry", expiry.ToLocalTime().ToString("g", _text.Culture));
    public string ResetDetails => _resetRecoveryBlocked ? _text["PendingStoreError"] : _pendingReset ? _text["ResetPending"] : HasError ? _text["RefreshToReset"]
        : _snapshot?.ResetCredits?.ExpiryDetailsComplete == false ? _text["ResetsDetailsIncomplete"] : $"{ResetAvailability}\n{ResetExpiry}";
    public string ResetButtonText => _text[_pendingReset ? "RetryReset" : "ResetAction"];
    public string ResetCountText => _snapshot?.ResetCredits?.AvailableCount.ToString(_text.Culture) ?? "?";
    public string SoonestExpiryText => _snapshot?.ResetCredits is { AvailableCount: > 0, EarliestKnownExpiry: { } expiry } credits
        ? _text.Format(credits.ExpiryDetailsComplete ? "ExpiresIn" : "KnownExpiresIn", FormatExpiryLeadTime(expiry, DateTimeOffset.UtcNow)) : "";
    internal static string FormatExpiryLeadTime(DateTimeOffset expiry, DateTimeOffset now)
    {
        var remaining = expiry - now;
        var days = remaining.TotalDays >= 1;
        var amount = days ? (int)remaining.TotalDays : Math.Max(0, (int)Math.Ceiling(remaining.TotalHours));
        return Localization.Current.Format(days ? "ExpiryDays" : "ExpiryHours", amount);
    }
    public bool CanChooseReset => CanReset && !_pendingReset;
    public string ResetListMessage => _pendingReset ? _text["ResetPending"] :
        _snapshot?.ResetCredits is not { } credits ? _text["ResetsUnknown"] :
        credits.AvailableCount == 0 ? _text["NoResets"] :
        credits.ExpiryDetailsComplete ? "" : _text["ResetsDetailsIncomplete"];
    public IReadOnlyList<ResetEntry> ResetEntries
    {
        get
        {
            if (_snapshot?.ResetCredits is not { AvailableCount: > 0 } credits) return [];
            var rows = credits.Credits?.Where(credit => credit.ExpiresAt is null || credit.ExpiresAt > DateTimeOffset.UtcNow)
                .OrderBy(credit => credit.ExpiresAt ?? DateTimeOffset.MaxValue).ToArray() ?? [];
            return rows.Select(credit => new ResetEntry(credit, _text["ResetCreditTitle"], credit.ExpiresAt is { } expiry
                ? _text.Format("ResetCreditExpires", expiry.ToLocalTime().ToString("g", _text.Culture) + " UTC" + expiry.ToLocalTime().ToString("zzz", CultureInfo.InvariantCulture))
                : _text["ExpiryUnknown"])).ToArray();
        }
    }
    public bool CanReset => !_busy && !_resetBusy && !_resetRecoveryBlocked && (_pendingReset ||
        (_errorKey is null && !SnapshotExpired && _snapshot?.ResetCredits?.AvailableCount > 0 &&
         _snapshot.ResetCredits.NextCredit(DateTimeOffset.UtcNow) is not null));
    public bool PendingReset => _pendingReset;
    public ResetCredit? NextResetCredit => _snapshot?.ResetCredits?.NextCredit(DateTimeOffset.UtcNow);
    public ResetCredit? FindSelectableCredit(string id) => CanChooseReset ? _snapshot?.ResetCredits?.Credits?
        .FirstOrDefault(credit => credit.Id == id && (credit.ExpiresAt is null || credit.ExpiresAt > DateTimeOffset.UtcNow)) : null;

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        if (_snapshot is not null && snapshot.ObservedAt < _snapshot.ObservedAt) return;
        if (!snapshot.ResetCreditsIncluded && _snapshot is not null)
            snapshot = snapshot with { ResetCredits = _snapshot.ResetCredits };
        _snapshot = snapshot;
        _errorKey = null;
        _noticeKey = null;
        NotifyAll();
    }

    public void SetError(string? error) { _errorKey = error is null ? null : ErrorKey(error); NotifyAll(); }
    public void SetNotice(string? key) { _noticeKey = key; NotifyAll(); }
    public void SetBusy(bool busy) { _busy = busy; NotifyAll(); }
    public void SetResetBusy(bool busy) { _resetBusy = busy; NotifyAll(); }
    public void SetPendingReset(bool pending, bool blocked = false) { _pendingReset = pending; _resetRecoveryBlocked = blocked; NotifyAll(); }
    public void Tick()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FiveHourCountdown)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SoonestExpiryText)));
        if (DateTimeOffset.UtcNow.Second % 15 == 0) NotifyAll();
    }
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Dispose() => _text.PropertyChanged -= OnLanguageChanged;

    private string Percent(double? value) => value is null ? "—" : $"{value.Value.ToString("0.#", _text.Culture)}%";
    private string Reset(QuotaWindow? window)
    {
        if (window is null) return _text["QuotaUnavailable"];
        if (window.ResetsAt is null) return _text["ResetTimeUnavailable"];
        if (window.ResetsAt <= DateTimeOffset.UtcNow) return _text["ResetTimePassed"];
        var format = _text.Language switch { "en" => "MMM d, HH:mm", "de" => "dd.MM., HH:mm", "zh-CN" or "ja" => "M月d日 HH:mm", _ => "d MMM, HH:mm" };
        return _text.Format("ResetAt", window.ResetsAt.Value.ToLocalTime().ToString(format, _text.Culture));
    }

    private string ErrorKey(string message)
    {
        if (_text.Catalog("en").ContainsKey(message)) return message;
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase)) return "CliMissing";
        if (message.Contains("respond in time", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        if (message.Contains("sign in", StringComparison.OrdinalIgnoreCase)) return "LoginRequired";
        if (message.Contains("did not return", StringComparison.OrdinalIgnoreCase)) return "LimitsUnavailable";
        if (message.Contains("rejected", StringComparison.OrdinalIgnoreCase)) return "RequestRejected";
        if (message.Contains("disconnect", StringComparison.OrdinalIgnoreCase)) return "Disconnected";
        if (message.Contains("save", StringComparison.OrdinalIgnoreCase)) return "SettingsSaveError";
        return "ConnectionError";
    }
}

public sealed record ResetEntry(ResetCredit Credit, string Title, string Expiry);
