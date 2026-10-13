namespace CodexUsageWidget.Core;

public sealed record QuotaAlert(string Kind, int? DurationMinutes, double? RemainingPercent, DateTimeOffset? Expiry);

/// <summary>Session-local deduplication; no endpoint calls or reset mutations.</summary>
public sealed class QuotaAlerts
{
    private readonly HashSet<string> _sent = [];
    public IReadOnlyList<QuotaAlert> Evaluate(UsageSnapshot snapshot, bool lowEnabled, int lowThreshold,
        bool expiryEnabled, int expiryHours, DateTimeOffset now)
    {
        var alerts = new List<QuotaAlert>();
        if (now - snapshot.ObservedAt > TimeSpan.FromMinutes(6)) return alerts;
        if (lowEnabled)
            foreach (var window in new[] { snapshot.FiveHour, snapshot.Weekly })
                if (window is not null && window.RemainingPercent <= lowThreshold &&
                    (window.ResetsAt is null || window.ResetsAt > now) &&
                    _sent.Add($"quota:{window.DurationMinutes}:{window.ResetsAt?.ToUnixTimeSeconds()}:{lowThreshold}"))
                    alerts.Add(new("low", window.DurationMinutes, window.RemainingPercent, null));
        if (expiryEnabled && snapshot.ResetCredits is { AvailableCount: > 0, Credits: { } credits })
            foreach (var credit in credits.Where(credit => credit.ExpiresAt > now && credit.ExpiresAt <= now.AddHours(expiryHours)))
                if (_sent.Add($"expiry:{credit.Id}:{credit.ExpiresAt?.ToUnixTimeSeconds()}"))
                    alerts.Add(new("expiry", null, null, credit.ExpiresAt));
        return alerts;
    }
}
