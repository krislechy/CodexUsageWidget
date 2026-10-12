using System.Diagnostics;

namespace CodexUsageWidget.Core;

public static class CodexLocator
{
    public static string Find(string? configuredPath = null)
    {
        var explicitPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Environment.GetEnvironmentVariable("CODEX_USAGE_WIDGET_CODEX_PATH") : configuredPath;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(explicitPath));
            Validate(path);
            return path;
        }
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => directory.Trim('"')).ToArray();
        foreach (var name in new[] { "codex.exe", "codex.cmd" })
            foreach (var directory in directories)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
        throw new UsageUnavailableException("Codex CLI was not found. Install it or select codex.exe in Settings.");
    }

    public static ProcessStartInfo CreateStartInfo(string? configuredPath)
    {
        var path = Find(configuredPath);
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = $"/d /s /c \"\"{path}\" app-server --listen stdio://\"";
        }
        else
        {
            info.FileName = path;
            info.ArgumentList.Add("app-server");
            info.ArgumentList.Add("--listen");
            info.ArgumentList.Add("stdio://");
        }
        return info;
    }

    private static void Validate(string path)
    {
        if (!File.Exists(path)) throw new UsageUnavailableException("The selected Codex executable does not exist.");
        if (path.IndexOfAny(['"', '\r', '\n', '%']) >= 0)
            throw new UsageUnavailableException("The Codex path contains unsupported shell characters.");
        if (!new[] { ".exe", ".cmd" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new UsageUnavailableException("Select codex.exe or codex.cmd.");
    }
}
