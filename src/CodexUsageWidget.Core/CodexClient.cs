using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexUsageWidget.Core;

/// <summary>A read-only, reconnectable client of the local Codex stdio app-server.</summary>
public sealed class CodexClient : IAsyncDisposable
{
    private readonly Func<ProcessStartInfo> _startInfo;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private Connection? _connection;
    private bool _disposed;

    public event Action<UsageSnapshot>? LimitsUpdated;
    public event Action? Disconnected;

    public CodexClient(string? codexPath = null)
        : this(() => CodexLocator.CreateStartInfo(codexPath), TimeSpan.FromSeconds(20)) { }

    public CodexClient(Func<ProcessStartInfo> startInfo, TimeSpan requestTimeout)
    {
        _startInfo = startInfo;
        _timeout = requestTimeout;
    }

    public async Task<UsageSnapshot> ReadLimitsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = await connection.RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
            return UsageSnapshot.Parse(payload);
        }
        catch (TimeoutException)
        {
            // Discard the hung connection; the next refresh creates a clean one.
            connection.Abort();
            throw;
        }
    }

    private async Task<Connection> GetConnectionAsync(CancellationToken token)
    {
        await _connectionGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection?.IsAlive == true) return _connection;
            if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = new Connection(_startInfo(), _timeout, snapshot => LimitsUpdated?.Invoke(snapshot), () => Disconnected?.Invoke());
            try
            {
                await _connection.RequestAsync("initialize", new
                {
                    clientInfo = new { name = "codex_usage_widget", title = "Codex Usage Widget", version = "0.1.0" }
                }, token).ConfigureAwait(false);
                await _connection.NotifyAsync("initialized", new { }, token).ConfigureAwait(false);
                return _connection;
            }
            catch
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
                throw;
            }
        }
        finally { _connectionGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _connectionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally { _connectionGate.Release(); }
    }

    private sealed class Connection : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly TimeSpan _timeout;
        private readonly Action<UsageSnapshot> _updated;
        private readonly Action _disconnected;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
        private readonly Task _reader;
        private readonly Task _errorDrainer;
        private long _nextId;
        private volatile bool _closed;
        private bool _disposed;

        public bool IsAlive => !_closed && !_process.HasExited;

        public Connection(ProcessStartInfo info, TimeSpan timeout, Action<UsageSnapshot> updated, Action disconnected)
        {
            _timeout = timeout;
            _updated = updated;
            _disconnected = disconnected;
            _process = Process.Start(info) ?? throw new UsageUnavailableException("Could not start Codex CLI.");
            _process.StandardInput.AutoFlush = true;
            _errorDrainer = DrainErrorsAsync();
            _reader = ReadAsync();
        }

        public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken token)
        {
            if (!IsAlive) throw new IOException("Codex app-server disconnected.");
            var id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            deadline.CancelAfter(_timeout);
            try
            {
                await WriteAsync(new { id, method, @params = parameters }, deadline.Token).ConfigureAwait(false);
                return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                throw new TimeoutException("Codex did not respond in time. The next refresh will reconnect.");
            }
            finally { _pending.TryRemove(id, out _); }
        }

        public Task NotifyAsync(string method, object parameters, CancellationToken token) =>
            WriteAsync(new { method, @params = parameters }, token);

        private async Task WriteAsync(object message, CancellationToken token)
        {
            await _writeGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }

        private async Task ReadAsync()
        {
            try
            {
                while (await _process.StandardOutput.ReadLineAsync(_lifetime.Token).ConfigureAwait(false) is { } line)
                {
                    JsonDocument document;
                    try { document = JsonDocument.Parse(line); }
                    catch (JsonException) { continue; }
                    using (document)
                    {
                        var message = document.RootElement;
                        if (message.ValueKind != JsonValueKind.Object) continue;
                        if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) &&
                            _pending.TryGetValue(number, out var completion))
                        {
                            if (message.TryGetProperty("error", out _))
                                completion.TrySetException(new UsageUnavailableException("Codex rejected the request. Check codex login and your CLI version."));
                            else if (message.TryGetProperty("result", out var result))
                                completion.TrySetResult(result.Clone());
                            else
                                completion.TrySetException(new IOException("Codex returned an invalid response."));
                        }
                        else if (message.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String &&
                            method.GetString() == "account/rateLimits/updated" && message.TryGetProperty("params", out var parameters))
                        {
                            try { _updated(UsageSnapshot.Parse(parameters)); }
                            catch (UsageUnavailableException) { /* Keep the last good snapshot until polling succeeds. */ }
                        }
                        else if (message.TryGetProperty("id", out var serverId) && message.TryGetProperty("method", out _))
                        {
                            // The widget never approves actions or supplies authentication tokens.
                            await WriteAsync(new { id = serverId.Clone(), error = new { code = -32601, message = "Read-only client: method not supported" } }, _lifetime.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or JsonException or InvalidOperationException)
            {
                // All outstanding requests fail below. Raw protocol/auth data is never logged.
            }
            finally
            {
                _closed = true;
                foreach (var completion in _pending.Values)
                    completion.TrySetException(new IOException("Codex app-server disconnected. The widget will reconnect automatically."));
                _disconnected();
            }
        }

        private async Task DrainErrorsAsync()
        {
            try
            {
                while (await _process.StandardError.ReadLineAsync(_lifetime.Token).ConfigureAwait(false) is not null) { }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException) { }
        }

        public void Abort()
        {
            _closed = true;
            _lifetime.Cancel();
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            Abort();
            await Task.WhenAll(_reader, _errorDrainer).ConfigureAwait(false);
            _process.Dispose();
            _lifetime.Dispose();
            _writeGate.Dispose();
        }
    }
}
