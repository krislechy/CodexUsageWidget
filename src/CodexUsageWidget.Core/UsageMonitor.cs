namespace CodexUsageWidget.Core;

/// <summary>Serializes refreshes and combines connection events with periodic polling.</summary>
public sealed class UsageMonitor : IAsyncDisposable
{
    private readonly CodexClient _client;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _loop;
    private bool _disposed;

    public event Action<UsageSnapshot>? SnapshotReceived;
    public event Action<string?>? StatusChanged;
    public event Action<bool>? BusyChanged;

    public UsageMonitor(CodexClient client, TimeSpan interval)
    {
        _client = client;
        _interval = interval;
        _client.LimitsUpdated += OnLimitsUpdated;
        _client.Disconnected += OnDisconnected;
    }

    public void Start() => _loop ??= Task.Run(RunAsync);

    private void OnLimitsUpdated(UsageSnapshot snapshot)
    {
        if (_lifetime.IsCancellationRequested) return;
        SnapshotReceived?.Invoke(snapshot);
        StatusChanged?.Invoke(null);
    }

    private void OnDisconnected()
    {
        if (!_lifetime.IsCancellationRequested) StatusChanged?.Invoke("Disconnected. Reconnecting on the next refresh.");
    }

    private async Task RunAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                await RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    public async Task RefreshAsync()
    {
        if (_lifetime.IsCancellationRequested || !await _refreshGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            BusyChanged?.Invoke(true);
            var snapshot = await _client.ReadLimitsAsync(_lifetime.Token).ConfigureAwait(false);
            SnapshotReceived?.Invoke(snapshot);
            StatusChanged?.Invoke(null);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is UsageUnavailableException or IOException or TimeoutException or
            System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusChanged?.Invoke(exception is System.ComponentModel.Win32Exception
                ? "Could not launch Codex CLI. Check the executable path in Settings." : exception.Message);
        }
        finally
        {
            BusyChanged?.Invoke(false);
            _refreshGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _client.LimitsUpdated -= OnLimitsUpdated;
        _client.Disconnected -= OnDisconnected;
        if (_loop is not null) await _loop.ConfigureAwait(false);
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try { await _client.DisposeAsync().ConfigureAwait(false); }
        finally { _refreshGate.Release(); }
        _lifetime.Dispose();
        _refreshGate.Dispose();
    }
}
