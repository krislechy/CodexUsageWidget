using System.Text.Json;

namespace CodexUsageWidget.Core;

public sealed record QuotaWindow(double UsedPercent, int DurationMinutes, DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

public sealed record UsageSnapshot(QuotaWindow? FiveHour, QuotaWindow? Weekly, DateTimeOffset ObservedAt)
{
    public static UsageSnapshot Parse(JsonElement payload, DateTimeOffset? observedAt = null)
    {
        JsonElement limits;
        if (payload.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty("codex", out limits))
        {
            // Prefer the general Codex bucket, never a model-specific bucket.
        }
        else if (!payload.TryGetProperty("rateLimits", out limits))
        {
            throw new UsageUnavailableException("Codex did not return subscription limits.");
        }

        if (limits.ValueKind != JsonValueKind.Object)
            throw new UsageUnavailableException("Subscription limits are unavailable. Sign in with ChatGPT using codex login.");

        if (limits.TryGetProperty("limitId", out var limitId) && limitId.ValueKind == JsonValueKind.String &&
            limitId.GetString() != "codex")
            throw new UsageUnavailableException("The general Codex subscription bucket is unavailable.");

        var windows = new[] { ReadWindow(limits, "primary"), ReadWindow(limits, "secondary") };
        var fiveHour = windows.FirstOrDefault(window => window?.DurationMinutes == 300);
        var weekly = windows.FirstOrDefault(window => window?.DurationMinutes == 10080);
        if (fiveHour is null && weekly is null)
            throw new UsageUnavailableException("Codex did not return a five-hour or weekly quota window.");
        return new(fiveHour, weekly, observedAt ?? DateTimeOffset.UtcNow);
    }

    private static QuotaWindow? ReadWindow(JsonElement limits, string key)
    {
        if (!limits.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number ||
            !used.TryGetDouble(out var value) || !double.IsFinite(value) ||
            !window.TryGetProperty("windowDurationMins", out var duration) || duration.ValueKind != JsonValueKind.Number ||
            !duration.TryGetInt32(out var minutes) || minutes <= 0)
            return null;

        DateTimeOffset? reset = null;
        if (window.TryGetProperty("resetsAt", out var timestamp) && timestamp.ValueKind == JsonValueKind.Number &&
            timestamp.TryGetInt64(out var seconds))
        {
            try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); }
            catch (ArgumentOutOfRangeException) { /* Preserve quota even without a valid date. */ }
        }
        return new(Math.Clamp(value, 0, 100), minutes, reset);
    }
}

public sealed class UsageUnavailableException(string message) : Exception(message);
