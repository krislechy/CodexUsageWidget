using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexUsageWidget.Core;

public sealed record UpdatePlan(string JobDirectory, string InstallDirectory, string Version, string Variant,
    int ParentProcessId, long ParentStartTicks);
public sealed record UpdateResult(string Status, string Version, DateTimeOffset CompletedAt);

public static class UpdateInstallation
{
    public static string UpdatesRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget", "updates");
    public static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, UpdatePackages.Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }
    public static UpdatePlan ReadPlan(string planPath)
    {
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath), UpdatePackages.Json)
            ?? throw new InvalidDataException("Invalid update plan.");
        var job = Path.GetFullPath(plan.JobDirectory);
        var root = Path.GetFullPath(UpdatesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!job.StartsWith(root, StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(job) != root.TrimEnd(Path.DirectorySeparatorChar) ||
            !Guid.TryParseExact(Path.GetFileName(job), "N", out _) || Path.GetFullPath(planPath) != Path.Combine(job, "plan.json") ||
            UpdatePackages.ParseVersion(plan.Version) is null || plan.Variant is not ("Lite" or "Portable"))
            throw new InvalidDataException("Untrusted update plan location.");
        _ = UpdatePackages.ResolveFile(job, "staged/update-manifest.json");
        _ = UpdatePackages.ResolveFile(plan.InstallDirectory, "CodexUsageWidget.exe");
        if (Directory.Exists(Path.Combine(plan.InstallDirectory, ".git"))) throw new InvalidDataException("A source checkout cannot be updated in place.");
        return plan;
    }

    public static UpdatePlan PreparePlan(string job, string install, Version version, string variant)
    {
        var current = UpdatePackages.ReadManifest(install);
        var staged = UpdatePackages.ReadManifest(Path.Combine(job, "staged"));
        if (current.Variant != variant || staged.Variant != variant || UpdatePackages.ParseVersion(staged.Version) != version ||
            version <= UpdatePackages.ParseVersion(current.Version)) throw new InvalidDataException("Invalid update direction or variant.");
        UpdatePackages.VerifyFiles(Path.Combine(job, "staged"), staged);
        var probe = UpdatePackages.ResolveFile(install, ".update-write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        using var process = Process.GetCurrentProcess();
        var plan = new UpdatePlan(Path.GetFullPath(job), Path.GetFullPath(install), version.ToString(3), variant, process.Id, process.StartTime.ToUniversalTime().Ticks);
        WriteJson(Path.Combine(job, "plan.json"), plan);
        return ReadPlan(Path.Combine(job, "plan.json"));
    }

    /// <summary>Replace only package-owned files and roll back on any failure. Unknown local files are preserved.</summary>
    public static void ApplyFiles(UpdatePlan plan)
    {
        var stage = Path.Combine(plan.JobDirectory, "staged");
        var backup = Path.Combine(plan.JobDirectory, "backup");
        var previous = UpdatePackages.ReadManifest(plan.InstallDirectory);
        var next = UpdatePackages.ReadManifest(stage);
        if (previous.Variant != plan.Variant || next.Variant != plan.Variant || UpdatePackages.ParseVersion(next.Version) != UpdatePackages.ParseVersion(plan.Version) ||
            UpdatePackages.ParseVersion(next.Version) <= UpdatePackages.ParseVersion(previous.Version)) throw new InvalidDataException("Update identity changed.");
        UpdatePackages.VerifyFiles(stage, next);
        var previousNames = previous.Files.Select(file => file.Path).Append(UpdatePackages.ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextNames = next.Files.Select(file => file.Path).Append(UpdatePackages.ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in nextNames)
            if (!previousNames.Contains(name) && (File.Exists(UpdatePackages.ResolveFile(plan.InstallDirectory, name)) || Directory.Exists(UpdatePackages.ResolveFile(plan.InstallDirectory, name))))
                throw new IOException("An update would overwrite a local file.");
        if (Directory.Exists(backup)) throw new IOException("Backup already exists; refusing to replace it.");
        Directory.CreateDirectory(backup);
        var saved = new List<PackageFile>();
        foreach (var name in previousNames)
        {
            var source = UpdatePackages.ResolveFile(plan.InstallDirectory, name);
            if (!File.Exists(source)) continue;
            var destination = UpdatePackages.ResolveFile(backup, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
            using var stream = File.OpenRead(destination);
            saved.Add(new(name, Convert.ToHexString(SHA256.HashData(stream)), stream.Length));
        }
        WriteJson(Path.Combine(plan.JobDirectory, "backup-files.json"), saved);
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in nextNames)
            {
                var source = UpdatePackages.ResolveFile(stage, name);
                var destination = UpdatePackages.ResolveFile(plan.InstallDirectory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".update-" + Path.GetFileName(plan.JobDirectory);
                try
                {
                    File.Copy(source, temporary, overwrite: false); File.Move(temporary, destination, overwrite: true);
                    touched.Add(name);
                    WriteJson(Path.Combine(plan.JobDirectory, "applied-files.json"), touched);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            UpdatePackages.VerifyFiles(plan.InstallDirectory, next);
            foreach (var obsolete in previousNames.Except(nextNames, StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(UpdatePackages.ResolveFile(plan.InstallDirectory, obsolete)); touched.Add(obsolete);
                WriteJson(Path.Combine(plan.JobDirectory, "applied-files.json"), touched);
            }
        }
        catch
        {
            RestoreFiles(plan, touched);
            throw;
        }
    }

    public static void Rollback(UpdatePlan plan)
    {
        var touched = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(Path.Combine(plan.JobDirectory, "applied-files.json")), UpdatePackages.Json)
            ?? throw new InvalidDataException("Missing update journal.");
        RestoreFiles(plan, touched.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static void RestoreFiles(UpdatePlan plan, HashSet<string> touched)
    {
        var backup = Path.Combine(plan.JobDirectory, "backup");
        var files = JsonSerializer.Deserialize<List<PackageFile>>(File.ReadAllText(Path.Combine(plan.JobDirectory, "backup-files.json")), UpdatePackages.Json)
            ?? throw new InvalidDataException("Missing update backup.");
        foreach (var file in files.Where(file => touched.Contains(file.Path)))
        {
            var source = UpdatePackages.ResolveFile(backup, file.Path);
            using (var stream = File.OpenRead(source))
                if (stream.Length != file.Length || !Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Update backup checksum mismatch.");
            File.Copy(source, UpdatePackages.ResolveFile(plan.InstallDirectory, file.Path), overwrite: true);
        }
        var next = UpdatePackages.ReadManifest(Path.Combine(plan.JobDirectory, "staged"));
        var saved = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in next.Files.Select(file => file.Path).Append(UpdatePackages.ManifestName).Where(name => touched.Contains(name) && !saved.Contains(name)))
            File.Delete(UpdatePackages.ResolveFile(plan.InstallDirectory, name));
    }

    public static async Task<int> RunHelperAsync(string planPath)
    {
        UpdatePlan? plan = null;
        var installed = false;
        var ownsLock = false;
        using var updateLock = new Semaphore(1, 1, @"Local\CodexUsageWidget.Update");
        try
        {
            plan = ReadPlan(planPath);
            if (!string.Equals(Environment.ProcessPath, Path.Combine(plan.JobDirectory, "updater.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update helper must run from its job directory.");
            ownsLock = updateLock.WaitOne(0);
            if (!ownsLock) return 1;
            using var parent = Process.GetProcessById(plan.ParentProcessId);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks ||
                !string.Equals(parent.MainModule?.FileName, Path.Combine(plan.InstallDirectory, "CodexUsageWidget.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update parent identity changed.");
            var previous = UpdatePackages.ReadManifest(plan.InstallDirectory);
            if (previous.Variant != plan.Variant) throw new InvalidDataException("Installed variant changed.");
            File.WriteAllText(Path.Combine(plan.JobDirectory, "ready"), "ready");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            ApplyFiles(plan); installed = true;
            WriteJson(Path.Combine(plan.JobDirectory, "result.json"), new UpdateResult("success", plan.Version, DateTimeOffset.UtcNow));
            updateLock.Release(); ownsLock = false;
            await StartApplicationAsync(plan, verifyStartup: true).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            if (plan is not null)
            {
                try
                {
                    if (installed) Rollback(plan);
                    WriteJson(Path.Combine(plan.JobDirectory, "result.json"), new UpdateResult("failed", plan.Version, DateTimeOffset.UtcNow));
                    if (File.Exists(Path.Combine(plan.JobDirectory, "ready")) && !IsParentAlive(plan))
                    {
                        if (ownsLock) { updateLock.Release(); ownsLock = false; }
                        await StartApplicationAsync(plan, verifyStartup: false).ConfigureAwait(false);
                    }
                }
                catch (Exception recovery) when (recovery is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            return 1;
        }
        finally { if (ownsLock) updateLock.Release(); }
    }

    private static bool IsParentAlive(UpdatePlan plan)
    {
        try { using var process = Process.GetProcessById(plan.ParentProcessId); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks; }
        catch (ArgumentException) { return false; }
    }
    private static async Task StartApplicationAsync(UpdatePlan plan, bool verifyStartup)
    {
        var start = new ProcessStartInfo(UpdatePackages.ResolveFile(plan.InstallDirectory, "CodexUsageWidget.exe"))
        { WorkingDirectory = plan.InstallDirectory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (verifyStartup) { start.ArgumentList.Add("--updated"); start.ArgumentList.Add(plan.JobDirectory); }
        using var process = Process.Start(start) ?? throw new IOException("Could not restart the application.");
        if (!verifyStartup) return;
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (File.Exists(Path.Combine(plan.JobDirectory, "app-ready"))) return;
            if (process.HasExited) throw new IOException("Updated application failed to start.");
            await Task.Delay(100).ConfigureAwait(false);
        }
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); }
        throw new IOException("Updated application did not confirm startup.");
    }
}
