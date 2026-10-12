using System;
using System.ComponentModel;
using System.Globalization;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public sealed class WidgetViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private UsageSnapshot? _snapshot;
    private string? _error;
    private bool _busy;
    private bool _compact;
    public event PropertyChangedEventHandler? PropertyChanged;

    public double FiveHourUsed => _snapshot?.FiveHour?.UsedPercent ?? 0;
    public double WeeklyUsed => _snapshot?.Weekly?.UsedPercent ?? 0;
    public string FiveHourRemaining => Percent(_snapshot?.FiveHour?.RemainingPercent);
    public string WeeklyRemaining => Percent(_snapshot?.Weekly?.RemainingPercent);
    public string FiveHourUsedLabel => Percent(_snapshot?.FiveHour?.UsedPercent);
    public string WeeklyUsedLabel => Percent(_snapshot?.Weekly?.UsedPercent);
    public string FiveHourReset => Reset(_snapshot?.FiveHour);
    public string WeeklyReset => Reset(_snapshot?.Weekly);
    public bool CanRefresh => !_busy;
    public bool HasError => _error is not null;
    public string StatusText => _busy ? "Обновление…" : _error is not null
        ? (_snapshot is null ? "Нет данных · откройте настройки" : "Данные устарели · повторим автоматически")
        : _snapshot is null ? "Подключение к Codex…" : $"Обновлено в {_snapshot.ObservedAt.ToLocalTime():HH:mm}";
    public string StatusDetails => _error ?? "Лимиты обновляются автоматически; кольца показывают расход, числа — остаток.";
    public string Freshness => _snapshot is null ? "" : $"Последние данные: {_snapshot.ObservedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}";
    public string CompactTooltip => $"5 часов: {FiveHourRemaining} осталось · {FiveHourReset}\nНеделя: {WeeklyRemaining} осталось · {WeeklyReset}\n{StatusText}\n{StatusDetails}";
    public bool IsCompact { get => _compact; set { _compact = value; NotifyAll(); } }
    public bool IsExpanded => !_compact;
    public string FiveHourAccessible => $"Лимит на 5 часов: осталось {FiveHourRemaining}. {FiveHourReset}";
    public string WeeklyAccessible => $"Недельный лимит: осталось {WeeklyRemaining}. {WeeklyReset}";

    public void SetSnapshot(UsageSnapshot snapshot)
    {
        if (_snapshot is not null && snapshot.ObservedAt < _snapshot.ObservedAt) return;
        _snapshot = snapshot;
        _error = null;
        NotifyAll();
    }

    public void SetError(string? error) { _error = error; NotifyAll(); }
    public void SetBusy(bool busy) { _busy = busy; NotifyAll(); }
    public void Tick() => NotifyAll();
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private static string Percent(double? value) => value is null ? "—" : $"{value.Value.ToString("0.#", Russian)}%";
    private static string Reset(QuotaWindow? window)
    {
        if (window is null) return "Лимит недоступен";
        if (window.ResetsAt is null) return "Время сброса недоступно";
        if (window.ResetsAt <= DateTimeOffset.UtcNow) return "Время сброса прошло · ожидаем Codex";
        return $"Сброс · {window.ResetsAt.Value.ToLocalTime().ToString("d MMM, HH:mm", Russian)}";
    }
}
