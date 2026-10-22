using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

/// <summary>Reads only bounded journal tails, never stores or logs conversation content.</summary>
internal sealed class ContextMonitor
{
    private string? _id;
    private string? _path;
    private long _length = -1;
    private ContextUsage? _value;
    private DateTime _lastSearch;

    internal Task<ContextUsage?> ReadAsync(string? id) => Task.Run(() =>
    {
        if (id != _id) { _id = id; _path = null; _length = -1; _value = null; _lastSearch = default; }
        if (!Guid.TryParse(id, out _)) return null;
        try
        {
            if (_path is null && DateTime.UtcNow - _lastSearch > TimeSpan.FromSeconds(30))
            {
                _lastSearch = DateTime.UtcNow;
                var home = Environment.GetEnvironmentVariable("CODEX_HOME") ??
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                var sessions = Path.Combine(home, "sessions");
                if (Directory.Exists(sessions))
                    _path = Directory.EnumerateFiles(sessions, $"*{id}.jsonl", SearchOption.AllDirectories).FirstOrDefault();
            }
            if (_path is null) return null;
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length == _length) return _value;
            _length = stream.Length;
            stream.Seek(Math.Max(0, stream.Length - 262144), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            foreach (var line in lines.Reverse())
                if (ContextUsage.ParseLogLine(line) is { } usage) return _value = usage;
            return _value = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return _value = null; }
    });
}
