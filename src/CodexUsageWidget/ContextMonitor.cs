using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

/// <summary>Reads only bounded journal tails, never stores or logs conversation content.</summary>
internal sealed class ContextMonitor : IDisposable
{
    private string? _id;
    private string? _path;
    private long _length = -1;
    private ContextUsage? _value;
    private DateTime _lastSearch;
    private DateTime _writeTime;
    private FileSystemWatcher? _watcher;
    private int _rescan = 1;
    private volatile bool _disposed;

    public void Dispose() { _disposed = true; _watcher?.Dispose(); }

    private void Changed(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(e.FullPath, _path, StringComparison.OrdinalIgnoreCase) || e.ChangeType == WatcherChangeTypes.Deleted)
            Interlocked.Exchange(ref _rescan, 1);
    }

    internal Task<ContextUsage?> ReadAsync(string? id) => Task.Run(() =>
    {
        if (_disposed) return null;
        if (id != _id)
        {
            _watcher?.Dispose(); _watcher = null;
            _id = id; _path = null; _length = -1; _value = null; _lastSearch = default;
            Interlocked.Exchange(ref _rescan, 1);
        }
        if (!Guid.TryParse(id, out _)) return null;
        try
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            var sessions = Path.Combine(home, "sessions");
            if (_watcher is null && Directory.Exists(sessions))
            {
                _watcher = new FileSystemWatcher(sessions, $"*{id}*.jsonl")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Created += Changed; _watcher.Changed += Changed;
                _watcher.Deleted += Changed; _watcher.Renamed += Changed;
                _watcher.Error += (_, _) => Interlocked.Exchange(ref _rescan, 1);
                _watcher.EnableRaisingEvents = true;
                Interlocked.Exchange(ref _rescan, 1);
                if (_disposed) { _watcher.Dispose(); return null; }
            }
            if (Interlocked.Exchange(ref _rescan, 0) != 0 || DateTime.UtcNow - _lastSearch > TimeSpan.FromSeconds(30))
            {
                // A resumed thread may write a new journal ending in _<window-id>.jsonl.
                // Watcher hints switch immediately; periodic discovery recovers missed events.
                _lastSearch = DateTime.UtcNow;
                var latest = Directory.Exists(sessions) ? Directory.EnumerateFiles(sessions, $"*-{id}*.jsonl", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        var name = Path.GetFileNameWithoutExtension(path);
                        var start = name.LastIndexOf("-" + id, StringComparison.OrdinalIgnoreCase);
                        if (start < 0) return false;
                        var suffix = name[(start + id!.Length + 1)..];
                        return suffix.Length == 0 || suffix[0] == '_' && Guid.TryParse(suffix[1..], out _);
                    })
                    .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc)
                    .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.FullName : null;
                if (!string.Equals(latest, _path, StringComparison.OrdinalIgnoreCase))
                {
                    _path = latest; _length = -1; _value = null;
                }
            }
            if (_path is null) return null;
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            var writeTime = File.GetLastWriteTimeUtc(_path);
            if (length == _length && writeTime == _writeTime) return _value;
            stream.Seek(Math.Max(0, stream.Length - 262144), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            ContextUsage? value = null;
            foreach (var line in lines.Reverse())
            {
                if (ContextUsage.ParseLogLine(line) is { } usage) { value = usage; break; }
                if (ContextUsage.IsCompactionLine(line)) break;
            }
            _length = length; _writeTime = writeTime;
            return _value = value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _length = -1; Interlocked.Exchange(ref _rescan, 1);
            return _value = null;
        }
    });
}
