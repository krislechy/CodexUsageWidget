using System.Globalization;
using System.Text.Json;

namespace CodexUsageWidget.Core;

public sealed record DailyUsage(DateOnly Date, long Tokens);
public sealed record InfoMetric(string Key, string Value);
public sealed record WorkspaceNotice(string Body, DateTimeOffset? CreatedAt);
public sealed record ExtraQuota(string Name, string? Plan, string? Balance, bool? HasCredits, bool? Unlimited,
    bool? SpendBlocked, string? ReachedType, string? SpendUsed, string? SpendLimit, double? SpendRemaining,
    DateTimeOffset? SpendResetsAt, IReadOnlyList<QuotaWindow> Windows);
public sealed record UsageDetails(IReadOnlyDictionary<string, long?> Summary, IReadOnlyList<DailyUsage>? Daily,
    string? ThreadId, decimal? EstimatedCredits, decimal? EstimatedUsd, IReadOnlyList<UsageGroup> Groups)
{
    public static UsageDetails Parse(JsonElement payload)
    {
        var summary = ExtraJson.Object(payload, "summary");
        var values = new[] { "lifetimeTokens", "peakDailyTokens", "longestRunningTurnSec", "currentStreakDays", "longestStreakDays" }
            .ToDictionary(key => key, key => ExtraJson.Number(summary, key));
        IReadOnlyList<DailyUsage>? daily = null;
        if (ExtraJson.Array(payload, "dailyUsageBuckets") is { } rows)
            daily = rows.Select(row => (Date: ExtraJson.Text(row, "startDate"), Tokens: ExtraJson.Number(row, "tokens")))
                .Where(row => row.Tokens is >= 0 && DateOnly.TryParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .Select(row => new DailyUsage(DateOnly.ParseExact(row.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture), row.Tokens!.Value))
                .GroupBy(row => row.Date).Select(group => group.Last()).OrderBy(row => row.Date).ToArray();
        var thread = ExtraJson.Object(payload, "threadUsage");
        var groups = ExtraJson.Array(thread, "groups")?.Select(UsageGroup.Parse).ToArray() ?? [];
        return new(values, daily, ExtraJson.Text(thread, "threadId"), ExtraJson.Number(thread, "estimatedUsageCreditsMicros") / 1_000_000m,
            ExtraJson.Number(thread, "estimatedUsageUsdMicros") / 1_000_000m, groups);
    }
}

public sealed record UsageGroup(string? Model, string? Effort, string? Speed, long? Total, long? Input, long? Cached,
    long? NetInput, long? Output, decimal? EstimatedCredits)
{
    internal static UsageGroup Parse(JsonElement row) => new(ExtraJson.Text(row, "model"), ExtraJson.Text(row, "reasoningEffort"),
        ExtraJson.Text(row, "speed"), ExtraJson.Number(row, "totalTokens"), ExtraJson.Number(row, "inputTokens"),
        ExtraJson.Number(row, "cachedInputTokens"), ExtraJson.Number(row, "netNewInputTokens"), ExtraJson.Number(row, "outputTokens"),
        ExtraJson.Number(row, "estimatedUsageCreditsMicros") / 1_000_000m);
}

public sealed record ExtraInfoSnapshot(string? AccountType, string? Plan, bool? RequiresAuth, bool? OrdinaryUsageAllowed,
    UsageDetails? Usage, IReadOnlyList<ExtraQuota> Quotas, ResetCredits? Resets, bool? MessagesEnabled,
    IReadOnlyList<WorkspaceNotice> Messages, DateTimeOffset FetchedAt, IReadOnlyList<string> UnavailableSections)
{
    public static ExtraInfoSnapshot Parse(JsonElement? accountPayload, JsonElement? usagePayload, JsonElement? limitsPayload,
        JsonElement? messagesPayload, IReadOnlyList<string>? unavailable = null)
    {
        var account = ExtraJson.Object(accountPayload, "account");
        var quotas = new List<ExtraQuota>();
        var buckets = ExtraJson.Object(limitsPayload, "rateLimitsByLimitId");
        if (buckets is { } map)
            foreach (var bucket in map.EnumerateObject()) quotas.Add(ParseQuota(bucket.Value, bucket.Name));
        else if (ExtraJson.Object(limitsPayload, "rateLimits") is { } legacy) quotas.Add(ParseQuota(legacy, "codex"));
        var notices = ExtraJson.Array(messagesPayload, "messages")?.Where(row => ExtraJson.Time(row, "archivedAt") is null)
            .Where(row => ExtraJson.Text(row, "messageBody") is not null)
            .Select(row => new WorkspaceNotice(ExtraJson.Text(row, "messageBody")!, ExtraJson.Time(row, "createdAt"))).ToArray() ?? [];
        return new(ExtraJson.Text(account, "type"), ExtraJson.Text(account, "planType"), ExtraJson.Boolean(accountPayload, "requiresOpenaiAuth"),
            ExtraJson.Boolean(limitsPayload, "ordinaryUsageAllowed"), usagePayload is { } usage ? UsageDetails.Parse(usage) : null,
            quotas, limitsPayload is { } limits ? ResetCredits.Parse(limits) : null, ExtraJson.Boolean(messagesPayload, "featureEnabled"),
            notices, DateTimeOffset.UtcNow, unavailable ?? []);
    }

    private static ExtraQuota ParseQuota(JsonElement row, string id)
    {
        var credits = ExtraJson.Object(row, "credits");
        var spend = ExtraJson.Object(row, "individualLimit");
        var windows = new List<QuotaWindow>();
        foreach (var slot in new[] { "primary", "secondary" })
            if (ExtraJson.Object(row, slot) is { } window && ExtraJson.Number(window, "usedPercent") is { } used &&
                ExtraJson.Number(window, "windowDurationMins") is { } duration && duration > 0 && duration <= int.MaxValue)
                windows.Add(new(Math.Clamp(used, 0, 100), (int)duration, ExtraJson.Time(window, "resetsAt")));
        return new(ExtraJson.Text(row, "limitName") ?? id, ExtraJson.Text(row, "planType"), ExtraJson.Text(credits, "balance"),
            ExtraJson.Boolean(credits, "hasCredits"), ExtraJson.Boolean(credits, "unlimited"), ExtraJson.Boolean(row, "spendControlReached"),
            ExtraJson.Text(row, "rateLimitReachedType"), ExtraJson.Text(spend, "used"), ExtraJson.Text(spend, "limit"),
            ExtraJson.Number(spend, "remainingPercent"), ExtraJson.Time(spend, "resetsAt"), windows);
    }
}

public sealed record ThreadChoice(string Id, string Name);
public sealed record ThreadPage(IReadOnlyList<ThreadChoice> Threads, string? Cursor)
{
    public static ThreadPage Parse(JsonElement payload) => new(ExtraJson.Array(payload, "data")?
        .Where(row => !string.IsNullOrWhiteSpace(ExtraJson.Text(row, "id")))
        .Select(row => new ThreadChoice(ExtraJson.Text(row, "id")!, ExtraJson.Text(row, "name") ?? ExtraJson.Text(row, "id")!)).ToArray() ?? [],
        ExtraJson.Text(payload, "nextCursor"));
}

public sealed record ThreadDetails(UsageDetails? Usage, JsonElement? Tokens);

internal static class ExtraJson
{
    internal static JsonElement? Property(JsonElement? parent, string key) => parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(key, out var child) ? child : null;
    internal static JsonElement? Object(JsonElement? parent, string key) => Property(parent, key) is { ValueKind: JsonValueKind.Object } value ? value : null;
    internal static JsonElement[]? Array(JsonElement? parent, string key) => Property(parent, key) is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray().ToArray() : null;
    internal static string? Text(JsonElement? parent, string key) => Property(parent, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    internal static long? Number(JsonElement? parent, string key) => Property(parent, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) && number >= 0 ? number : null;
    internal static bool? Boolean(JsonElement? parent, string key) => Property(parent, key) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : null;
    internal static DateTimeOffset? Time(JsonElement? parent, string key)
    {
        if (Number(parent, key) is not { } number) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(number); } catch (ArgumentOutOfRangeException) { return null; }
    }
}
