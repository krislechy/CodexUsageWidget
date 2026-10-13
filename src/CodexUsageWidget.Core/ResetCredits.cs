using System.Text.Json;

namespace CodexUsageWidget.Core;

public sealed record ResetCredit(string Id, DateTimeOffset? ExpiresAt, DateTimeOffset? GrantedAt = null, string? Title = null, string? Description = null);

public sealed record ResetCredits(int AvailableCount, IReadOnlyList<ResetCredit>? Credits)
{
    public bool ExpiryDetailsComplete => Credits is not null && Credits.Count == AvailableCount && Credits.All(credit => credit.ExpiresAt > DateTimeOffset.UtcNow);
    public DateTimeOffset? EarliestKnownExpiry => Credits?.Where(credit => credit.ExpiresAt > DateTimeOffset.UtcNow)
        .Select(credit => credit.ExpiresAt).Min();
    public ResetCredit? NextCredit(DateTimeOffset now) => Credits?
        .Where(credit => credit.ExpiresAt is null || credit.ExpiresAt > now)
        .OrderBy(credit => credit.ExpiresAt ?? DateTimeOffset.MaxValue).FirstOrDefault();

    public static ResetCredits? Parse(JsonElement payload)
    {
        if (!payload.TryGetProperty("rateLimitResetCredits", out var resets) || resets.ValueKind != JsonValueKind.Object ||
            !resets.TryGetProperty("availableCount", out var count) || count.ValueKind != JsonValueKind.Number ||
            !count.TryGetInt32(out var available) || available < 0) return null;
        List<ResetCredit>? credits = null;
        if (resets.TryGetProperty("credits", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            credits = [];
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "available" ||
                    !row.TryGetProperty("resetType", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "codexRateLimits" ||
                    !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())) continue;
                DateTimeOffset? expiresAt = null;
                if (row.TryGetProperty("expiresAt", out var expiry) && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out var seconds))
                {
                    try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds); }
                    catch (ArgumentOutOfRangeException) { }
                }
                if (credits.All(credit => credit.Id != id.GetString())) credits.Add(new(id.GetString()!, expiresAt,
                    ExtraJson.Time(row, "grantedAt"), ExtraJson.Text(row, "title"), ExtraJson.Text(row, "description")));
            }
        }
        return new(available, credits);
    }
}

public sealed record ResetRequest(string IdempotencyKey, string CreditId);
public enum ResetOutcome { Reset, AlreadyRedeemed, NothingToReset, NoCredit }

/// <summary>Durable retry identity: an uncertain reset can never silently become a new attempt.</summary>
public sealed class PendingResetStore(string path)
{
    public ResetRequest? Load()
    {
        if (!File.Exists(path)) return null;
        var request = JsonSerializer.Deserialize<ResetRequest>(File.ReadAllText(path));
        if (request is null || !Guid.TryParse(request.IdempotencyKey, out _) || string.IsNullOrWhiteSpace(request.CreditId))
            throw new IOException("Pending reset metadata is invalid. Do not start a new reset until it is recovered.");
        return request;
    }

    public ResetRequest Begin(string creditId)
    {
        var previous = Load();
        if (previous is not null) return previous;
        ArgumentException.ThrowIfNullOrWhiteSpace(creditId);
        var request = new ResetRequest(Guid.NewGuid().ToString(), creditId);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(request));
        File.Move(temporary, path, overwrite: false);
        return request;
    }

    public void Complete() => File.Delete(path);
}
