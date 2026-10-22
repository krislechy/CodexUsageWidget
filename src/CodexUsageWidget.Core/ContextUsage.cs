using System.Text.Json;

namespace CodexUsageWidget.Core;

public sealed record ContextUsage(long Used, long Capacity)
{
    public double Percent => Math.Clamp(100d * Used / Capacity, 0, 100);

    public static ContextUsage? ParseLogLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "event_msg") return null;
            var payload = root.GetProperty("payload");
            if (payload.GetProperty("type").GetString() != "token_count") return null;
            var info = payload.GetProperty("info");
            var capacity = info.GetProperty("model_context_window").GetInt64();
            var used = info.GetProperty("last_token_usage").GetProperty("total_tokens").GetInt64();
            return capacity > 0 && used >= 0 ? new(used, capacity) : null;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { return null; }
    }
}
