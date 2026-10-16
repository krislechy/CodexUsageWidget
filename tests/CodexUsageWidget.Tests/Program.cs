using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CodexUsageWidget.Core;

if (args.FirstOrDefault() == "--fake-server")
{
    await FakeServerAsync(args.ElementAtOrDefault(1) ?? "normal");
    return 0;
}

if (args.Contains("--live-extra"))
{
    await using var client = new CodexClient();
    var info = await client.ReadExtraInfoAsync();
    Console.WriteLine($"Extra info: account={info.AccountType is not null}; usage={info.Usage is not null}; daily={info.Usage?.Daily?.Count}; quotas={info.Quotas.Count}; workspaceSupported={info.MessagesEnabled}; unavailable={string.Join(',', info.UnavailableSections)}.");
    var threads = await client.ReadThreadsAsync();
    Console.WriteLine($"Thread picker: returned={threads.Threads.Count}; nextPage={threads.Cursor is not null}.");
    return 0;
}

if (args.Contains("--live"))
{
    await using var client = new CodexClient();
    var snapshot = await client.ReadLimitsAsync();
    Console.WriteLine($"Live Codex connection OK. 5h remaining: {snapshot.FiveHour?.RemainingPercent}; weekly remaining: {snapshot.Weekly?.RemainingPercent}.");
    Console.WriteLine($"Reset dates present: 5h={snapshot.FiveHour?.ResetsAt is not null}, weekly={snapshot.Weekly?.ResetsAt is not null}.");
    Console.WriteLine($"Reset availability: {snapshot.ResetCredits?.AvailableCount.ToString() ?? "unknown"}; expiry details complete: {snapshot.ResetCredits?.ExpiryDetailsComplete}.");
    return 0;
}

var tests = new List<(string Name, Func<Task> Run)>
{
    ("remaining percentage and timestamp", () => Check(() =>
    {
        var snapshot = Parse(Limits(73, 11));
        Equal(27d, snapshot.FiveHour!.RemainingPercent);
        Equal(89d, snapshot.Weekly!.RemainingPercent);
        Equal(DateTimeOffset.FromUnixTimeSeconds(1900000000), snapshot.FiveHour.ResetsAt!.Value);
    })),
    ("multi-bucket result prefers general Codex", () => Check(() =>
    {
        var payload = "{\"rateLimits\":{\"primary\":{\"usedPercent\":99,\"windowDurationMins\":300}},\"rateLimitsByLimitId\":{\"codex\":" + Bucket(23, 31) + ",\"codex_other\":" + Bucket(90, 90) + "}}";
        Equal(77d, Parse(payload).FiveHour!.RemainingPercent);
    })),
    ("windows identified by duration, not slot", () => Check(() =>
    {
        var snapshot = Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":11,\"windowDurationMins\":10080},\"secondary\":{\"usedPercent\":73,\"windowDurationMins\":300}}}");
        Equal(27d, snapshot.FiveHour!.RemainingPercent);
        Equal(89d, snapshot.Weekly!.RemainingPercent);
    })),
    ("missing window remains unavailable", () => Check(() =>
    {
        var snapshot = Parse("{\"rateLimits\":{\"primary\":null,\"secondary\":{\"usedPercent\":11,\"windowDurationMins\":10080}}}");
        True(snapshot.FiveHour is null);
        Equal(89d, snapshot.Weekly!.RemainingPercent);
    })),
    ("percentages clamped at both boundaries", () => Check(() =>
    {
        var snapshot = Parse(Limits(-30, 160));
        Equal(100d, snapshot.FiveHour!.RemainingPercent);
        Equal(0d, snapshot.Weekly!.RemainingPercent);
    })),
    ("invalid reset preserves usable quota", () => Check(() =>
    {
        var snapshot = Parse(Limits(73, 11).Replace("1900000000", "9223372036854775807"));
        True(snapshot.FiveHour!.ResetsAt is null);
        Equal(27d, snapshot.FiveHour.RemainingPercent);
    })),
    ("malformed numeric field does not become zero", () => Check(() =>
    {
        var snapshot = Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":\"bad\",\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":11,\"windowDurationMins\":10080}}}");
        True(snapshot.FiveHour is null);
    })),
    ("empty account produces explicit unavailable error", () => Check(() =>
    {
        Throws<UsageUnavailableException>(() => Parse("{\"rateLimits\":null}"));
        Throws<UsageUnavailableException>(() => Parse("{}"));
        Throws<UsageUnavailableException>(() => Parse("{\"rateLimits\":{\"limitId\":\"codex_other\"}}"));
    })),
    ("stdio handshake, notifications and server requests", async () =>
    {
        await using var client = FakeClient("normal");
        var notification = new TaskCompletionSource<UsageSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.LimitsUpdated += snapshot => notification.TrySetResult(snapshot);
        var snapshot = await client.ReadLimitsAsync();
        Equal(27d, snapshot.FiveHour!.RemainingPercent);
        var updated = await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Equal(35d, updated.FiveHour!.RemainingPercent);
        Equal(27d, (await client.ReadLimitsAsync()).FiveHour!.RemainingPercent);
    }),
    ("concurrent requests correlate by numeric id", async () =>
    {
        await using var client = FakeClient("normal");
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.ReadLimitsAsync()));
        True(snapshots.All(snapshot => snapshot.FiveHour!.RemainingPercent == 27));
    }),
    ("timeout recovers with a new process", async () =>
    {
        var starts = 0;
        await using var client = new CodexClient(() => StartInfo(Interlocked.Increment(ref starts) == 1 ? "hang" : "normal"), TimeSpan.FromSeconds(2));
        await ThrowsAsync<TimeoutException>(() => client.ReadLimitsAsync());
        Equal(27d, (await client.ReadLimitsAsync()).FiveHour!.RemainingPercent);
        Equal(2, starts);
    }),
    ("process exit recovers on the next request", async () =>
    {
        var starts = 0;
        await using var client = new CodexClient(() => StartInfo(Interlocked.Increment(ref starts) == 1 ? "exit" : "normal"), TimeSpan.FromSeconds(5));
        await ThrowsAsync<IOException>(() => client.ReadLimitsAsync());
        Equal(27d, (await client.ReadLimitsAsync()).FiveHour!.RemainingPercent);
    }),
    ("request cancellation does not corrupt the next request", async () =>
    {
        await using var client = FakeClient("delay");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        await ThrowsAsync<OperationCanceledException>(() => client.ReadLimitsAsync(cancellation.Token));
        Equal(27d, (await client.ReadLimitsAsync()).FiveHour!.RemainingPercent);
    }),
    ("server errors never expose raw account data", async () =>
    {
        await using var client = FakeClient("error");
        var exception = await ThrowsAsync<UsageUnavailableException>(() => client.ReadLimitsAsync());
        True(!exception.Message.Contains("secret@example.com", StringComparison.Ordinal));
    }),
    ("reset count is authoritative when details are capped", () => Check(() =>
    {
        var snapshot = Parse(WithResets(3, "[{\"id\":\"r1\",\"status\":\"available\",\"resetType\":\"codexRateLimits\",\"expiresAt\":1900000000}]"));
        Equal(3, snapshot.ResetCredits!.AvailableCount);
        True(!snapshot.ResetCredits.ExpiryDetailsComplete);
        Equal(DateTimeOffset.FromUnixTimeSeconds(1900000000), snapshot.ResetCredits.EarliestKnownExpiry!.Value);
    })),
    ("earliest reset selection skips expired and spent credits", () => Check(() =>
    {
        var snapshot = Parse(WithResets(2, "[{\"id\":\"expired\",\"status\":\"available\",\"resetType\":\"codexRateLimits\",\"expiresAt\":1},{\"id\":\"later\",\"status\":\"available\",\"resetType\":\"codexRateLimits\",\"expiresAt\":1900500000},{\"id\":\"first\",\"status\":\"available\",\"resetType\":\"codexRateLimits\",\"expiresAt\":1900000000},{\"id\":\"spent\",\"status\":\"used\",\"resetType\":\"codexRateLimits\",\"expiresAt\":1800000000}]"));
        Equal("first", snapshot.ResetCredits!.NextCredit(DateTimeOffset.UtcNow)!.Id);
    })),
    ("unknown reset metadata stays unknown", () => Check(() =>
    {
        True(Parse(Limits(73, 11)).ResetCredits is null);
        var snapshot = Parse(WithResets(3, "null"));
        Equal(3, snapshot.ResetCredits!.AvailableCount);
        True(snapshot.ResetCredits.EarliestKnownExpiry is null);
        True(snapshot.ResetCredits.NextCredit(DateTimeOffset.UtcNow) is null);
        True(!snapshot.ResetCredits.ExpiryDetailsComplete);
    })),
    ("pending reset identity survives restart and is never replaced", () => Check(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexUsageWidget-test-" + Guid.NewGuid());
        var path = Path.Combine(directory, "pending.json");
        try
        {
            var request = new PendingResetStore(path).Begin("first");
            var reopened = new PendingResetStore(path);
            Equal(request.IdempotencyKey, reopened.Begin("different").IdempotencyKey);
            Equal("first", reopened.Load()!.CreditId);
            reopened.Complete();
            True(reopened.Load() is null);
            File.WriteAllText(path, "{\"IdempotencyKey\":\"invalid\",\"CreditId\":\"first\"}");
            Throws<IOException>(() => reopened.Begin("different"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    })),
    ("all reset outcomes are parsed and quota is refreshed", async () =>
    {
        foreach (var (value, expected) in new[] { ("reset", ResetOutcome.Reset), ("alreadyRedeemed", ResetOutcome.AlreadyRedeemed), ("nothingToReset", ResetOutcome.NothingToReset), ("noCredit", ResetOutcome.NoCredit) })
        {
            await using var monitor = new UsageMonitor(FakeClient("reset:" + value), TimeSpan.FromMinutes(1));
            var observed = false;
            monitor.SnapshotReceived += _ => observed = true;
            var outcome = await monitor.ConsumeResetAsync(new(Guid.NewGuid().ToString(), "test-credit"));
            True(outcome == expected);
            True(observed);
        }
    }),
    ("lost reset response reuses the same durable attempt", async () =>
    {
        var ledger = Path.Combine(Path.GetTempPath(), "CodexUsageWidget-ledger-" + Guid.NewGuid() + ".json");
        var starts = 0;
        try
        {
            await using var client = new CodexClient(() =>
            {
                var info = StartInfo(Interlocked.Increment(ref starts) == 1 ? "reset-lost" : "reset:reset");
                info.Environment["FAKE_RESET_LEDGER"] = ledger;
                return info;
            }, TimeSpan.FromSeconds(2));
            var request = new ResetRequest(Guid.NewGuid().ToString(), "test-credit");
            await ThrowsAsync<TimeoutException>(() => client.ConsumeResetAsync(request));
            True(await client.ConsumeResetAsync(request) == ResetOutcome.AlreadyRedeemed);
            Equal(1, JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ledger))!.Count);
        }
        finally { if (File.Exists(ledger)) File.Delete(ledger); }
    }),
    ("extra info preserves unknown fields, credit units and reported days", () => Check(() =>
    {
        using var account = JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\",\"email\":\"private@example.com\"},\"requiresOpenaiAuth\":true}");
        using var usage = JsonDocument.Parse("{\"summary\":{\"lifetimeTokens\":12000,\"peakDailyTokens\":null},\"dailyUsageBuckets\":[{\"startDate\":\"2026-10-13\",\"tokens\":0},{\"startDate\":\"bad\",\"tokens\":9},{\"startDate\":\"2026-10-12\",\"tokens\":-1}],\"threadUsage\":{\"threadId\":\"a\",\"estimatedUsageCreditsMicros\":1234567,\"estimatedUsageUsdMicros\":null,\"groups\":[]}}");
        using var limits = JsonDocument.Parse("{\"ordinaryUsageAllowed\":false,\"rateLimitsByLimitId\":{\"codex\":{\"limitName\":\"Codex\",\"credits\":{\"balance\":\"12.50\",\"hasCredits\":true,\"unlimited\":false}},\"model\":{\"primary\":{\"usedPercent\":100,\"windowDurationMins\":300}}}}");
        using var notices = JsonDocument.Parse("{\"featureEnabled\":true,\"messages\":[{\"messageBody\":\"active\",\"createdAt\":1900000000,\"archivedAt\":null},{\"messageBody\":\"old\",\"archivedAt\":1900000000}]}");
        var result = ExtraInfoSnapshot.Parse(account.RootElement, usage.RootElement, limits.RootElement, notices.RootElement);
        Equal("plus", result.Plan!); True(result.OrdinaryUsageAllowed == false); Equal(2, result.Quotas.Count);
        Equal("12.50", result.Quotas[0].Balance!); True(result.Quotas[1].HasCredits is null);
        Equal(0d, result.Quotas[1].Windows[0].RemainingPercent);
        Equal(1, result.Usage!.Daily!.Count); Equal(0L, result.Usage.Daily[0].Tokens);
        True(result.Usage.Summary["peakDailyTokens"] is null); Equal(1.234567m, result.Usage.EstimatedCredits!.Value);
        True(result.Usage.EstimatedUsd is null); Equal(1, result.Messages.Count);
        True(!JsonSerializer.Serialize(result).Contains("private@example.com", StringComparison.Ordinal));
        var empty = ExtraInfoSnapshot.Parse(null, null, null, null);
        True(empty.Usage is null && empty.OrdinaryUsageAllowed is null && empty.MessagesEnabled is null);
    })),
    ("extra RPC failures stay isolated and selected thread usage is correlated", async () =>
    {
        await using var client = FakeClient("extra");
        var snapshot = await client.ReadExtraInfoAsync();
        Equal("plus", snapshot.Plan!); Equal(42L, snapshot.Usage!.Summary["lifetimeTokens"]!.Value);
        True(snapshot.UnavailableSections.SequenceEqual(new[] { "WorkspaceNotices" }));
        var page = await client.ReadThreadsAsync();
        Equal("a", page.Threads[0].Id); Equal("Exact title", page.Threads[0].Name); Equal("next", page.Cursor!);
        var selected = await client.ReadThreadDetailsAsync("a");
        Equal(2m, selected.Usage!.EstimatedCredits!.Value);
        var mismatch = await client.ReadThreadDetailsAsync("other");
        True(mismatch.Usage is null);
    }),
    ("quota notifications deduplicate and reject stale or expired data", () => Check(() =>
    {
        var now = DateTimeOffset.UtcNow;
        var alerts = new QuotaAlerts();
        var snapshot = new UsageSnapshot(new(95, 300, now.AddHours(2)), null, now,
            new(2, [new("soon", now.AddHours(1)), new("expired", now.AddHours(-1))]));
        Equal(0, alerts.Evaluate(snapshot, false, 10, false, 24, now).Count);
        Equal(0, alerts.Evaluate(snapshot with { ObservedAt = now.AddMinutes(-7) }, true, 10, true, 24, now).Count);
        Equal(2, alerts.Evaluate(snapshot, true, 10, true, 24, now).Count);
        Equal(0, alerts.Evaluate(snapshot, true, 10, true, 24, now).Count);
        Equal(1, alerts.Evaluate(snapshot with { FiveHour = new(96, 300, now.AddHours(3)) }, true, 10, true, 24, now).Count);
    })),
    ("every locale has the full catalog and valid placeholders", () => Check(() =>
    {
        var localizer = new Localization();
        Equal("en", localizer.Language);
        var english = localizer.Catalog("en");
        foreach (var language in Localization.Languages)
        {
            var catalog = localizer.Catalog(language.Code);
            True(catalog.Keys.Order().SequenceEqual(english.Keys.Order()));
            localizer.SetLanguage(language.Code);
            foreach (var (key, value) in catalog)
            {
                True(!string.IsNullOrWhiteSpace(value));
                var arguments = System.Text.CompositeFormat.Parse(value).MinimumArgumentCount;
                Equal(System.Text.CompositeFormat.Parse(english[key]).MinimumArgumentCount, arguments);
                _ = localizer.Format(key, Enumerable.Repeat<object?>("42", arguments).ToArray());
            }
        }
        localizer.SetLanguage("unsupported");
        Equal("en", localizer.Language);
    })),
    ("monitor polls automatically and shuts down cleanly", async () =>
    {
        await using var monitor = new UsageMonitor(FakeClient("plain"), TimeSpan.FromMilliseconds(100));
        var count = 0;
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.SnapshotReceived += _ => { if (Interlocked.Increment(ref count) >= 2) polled.TrySetResult(); };
        monitor.Start();
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        True(count >= 2);
    })
};

tests.AddRange(UpdateTests.All);
var failures = 0;
if (!args.Contains("--check-locales")) tests.RemoveAll(test => test.Name.StartsWith("every locale", StringComparison.Ordinal));
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {exception.GetType().Name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

static Task Check(Action action) { action(); return Task.CompletedTask; }
static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Equal<T>(T expected, T actual) where T : IEquatable<T>
{ if (!expected.Equals(actual)) throw new Exception($"Expected {expected}; got {actual}."); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T exception) { return exception; } throw new Exception($"Expected {typeof(T).Name}."); }
static UsageSnapshot Parse(string json) { using var document = JsonDocument.Parse(json); return UsageSnapshot.Parse(document.RootElement); }
static string Bucket(double fiveHour, double weekly) => JsonSerializer.Serialize(new
{
    limitId = "codex", primary = new { usedPercent = fiveHour, windowDurationMins = 300, resetsAt = 1900000000 },
    secondary = new { usedPercent = weekly, windowDurationMins = 10080, resetsAt = 1900500000 }
});
static string Limits(double fiveHour, double weekly) => "{\"rateLimits\":" + Bucket(fiveHour, weekly) + "}";
static string WithResets(int count, string credits) => "{\"rateLimits\":" + Bucket(73, 11) + ",\"rateLimitResetCredits\":{\"availableCount\":" + count + ",\"credits\":" + credits + "}}";
static CodexClient FakeClient(string mode) => new(() => StartInfo(mode), TimeSpan.FromSeconds(5));
static ProcessStartInfo StartInfo(string mode)
{
    var info = new ProcessStartInfo("dotnet")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    info.ArgumentList.Add("--fake-server");
    info.ArgumentList.Add(mode);
    return info;
}

static async Task FakeServerAsync(string mode)
{
    var initialized = false;
    var acknowledged = false;
    var requests = 0;
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var document = JsonDocument.Parse(line);
        var request = document.RootElement;
        if (!request.TryGetProperty("method", out var method)) continue;
        if (method.GetString() == "initialized") { acknowledged = true; continue; }
        var id = request.GetProperty("id").Clone();
        if (method.GetString() == "initialize")
        { initialized = true; Console.WriteLine(JsonSerializer.Serialize(new { id, result = new { userAgent = "test" } })); continue; }
        if (!initialized || !acknowledged) throw new InvalidOperationException("Handshake missing.");
        if (method.GetString() == "account/rateLimitResetCredit/consume")
        {
            var parameters = request.GetProperty("params");
            var key = parameters.GetProperty("idempotencyKey").GetString()!;
            if (!Guid.TryParse(key, out _) || parameters.GetProperty("creditId").GetString() != "test-credit")
                throw new InvalidOperationException("Invalid reset request.");
            var ledgerPath = Environment.GetEnvironmentVariable("FAKE_RESET_LEDGER");
            var ledger = ledgerPath is not null && File.Exists(ledgerPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ledgerPath))! : new Dictionary<string, string>();
            var outcome = ledger.ContainsKey(key) ? "alreadyRedeemed" : mode.StartsWith("reset:") ? mode[6..] : "reset";
            if (outcome == "reset")
            {
                ledger[key] = "test-credit";
                if (ledgerPath is not null) File.WriteAllText(ledgerPath, JsonSerializer.Serialize(ledger));
            }
            if (mode == "reset-lost") continue;
            Console.WriteLine(JsonSerializer.Serialize(new { id, result = new { outcome } }));
            continue;
        }
        if (mode == "extra")
        {
            var extra = method.GetString() switch
            {
                "account/read" => "{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\"},\"requiresOpenaiAuth\":true}",
                "account/rateLimits/read" => Limits(73, 11),
                "account/usage/read" when request.GetProperty("params").ValueKind == JsonValueKind.Object => "{\"summary\":{},\"threadUsage\":{\"threadId\":\"a\",\"estimatedUsageCreditsMicros\":2000000,\"groups\":[]}}",
                "account/usage/read" => "{\"summary\":{\"lifetimeTokens\":42}}",
                "thread/list" => "{\"data\":[{\"id\":\"a\",\"name\":\"Exact title\"}],\"nextCursor\":\"next\"}",
                _ => null
            };
            if (extra is null) Console.WriteLine(JsonSerializer.Serialize(new { id, error = new { code = -32601, message = "private@example.com" } }));
            else { using var extraJson = JsonDocument.Parse(extra); Console.WriteLine(JsonSerializer.Serialize(new { id, result = extraJson.RootElement })); }
            continue;
        }
        if (mode == "exit") return;
        if (mode == "hang") continue;
        if (mode == "error")
        { Console.WriteLine(JsonSerializer.Serialize(new { id, error = new { code = -1, message = "secret@example.com" } })); continue; }
        if (mode == "delay" && ++requests == 1) await Task.Delay(900);
        Console.Error.WriteLine("Test diagnostic: stderr must be drained.");
        Console.WriteLine("non-json diagnostic");
        if (mode == "normal")
        {
            Console.WriteLine("{\"id\":\"server-request\",\"method\":\"attestation/generate\",\"params\":{}}");
            Console.WriteLine("{\"method\":\"account/rateLimits/updated\",\"params\":{\"rateLimits\":{\"primary\":{\"usedPercent\":\"bad\",\"windowDurationMins\":300}}}}");
            Console.WriteLine("{\"method\":\"account/rateLimits/updated\",\"params\":" + Limits(65, 11) + "}");
        }
        using var payload = JsonDocument.Parse(Limits(73, 11));
        Console.WriteLine(JsonSerializer.Serialize(new { id, result = payload.RootElement }));
    }
}
