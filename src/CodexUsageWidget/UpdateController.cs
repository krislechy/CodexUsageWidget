using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

public sealed class UpdateController : INotifyPropertyChanged, IDisposable
{
    public static Version CurrentVersion => typeof(UpdateController).Assembly.GetName().Version is { } value
        ? new(value.Major, value.Minor, value.Build) : new(0, 0, 0);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly Func<bool> _autoEnabled;
    private readonly Func<bool> _canRestart;
    private readonly Action _exit;
    private readonly bool _preview;
    private readonly GitHubUpdates _github;
    private readonly string _install = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private readonly string _checkState = Path.Combine(UpdateInstallation.UpdatesRoot, "last-check.json");
    private string _status = "UpdateIdle";
    private bool _busy;
    private bool _manualRequested;
    private DateTimeOffset? _lastCheck;
    private WidgetRelease? _available;
    private UpdatePlan? _prepared;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsPackaged { get; }
    public string Variant { get; } = "Portable";
    public bool IsBusy => _busy;
    public bool IsInstalling { get; private set; }
    public bool CanCheck => !_busy && !_preview;
    public bool CanInstall => !_busy && _available is not null && IsPackaged && !_preview;
    public string Status => Localization.Current.Format("UpdateInstalledVersion", CurrentVersion.ToString(3), Variant) + "\n" +
        Localization.Current.Format(_status, _available?.Version.ToString(3) ?? "");

    public UpdateController(Func<bool> autoEnabled, Func<bool> canRestart, Action exit, bool preview = false)
    {
        _autoEnabled = autoEnabled; _canRestart = canRestart; _exit = exit; _preview = preview;
        _github = new(_http);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexUsageWidget/" + CurrentVersion.ToString(3));
        try
        {
            var manifest = UpdatePackages.ReadManifest(_install);
            if (UpdatePackages.ParseVersion(manifest.Version) == CurrentVersion)
            { Variant = manifest.Variant; IsPackaged = true; }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        if (!IsPackaged) _status = "UpdateDevelopment";
        try { if (File.Exists(_checkState)) _lastCheck = JsonSerializer.Deserialize<DateTimeOffset>(File.ReadAllText(_checkState)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        Localization.Current.PropertyChanged += LanguageChanged;
        _timer.Tick += async (_, _) => await CycleAsync();
    }

    public async void Start()
    {
        if (_preview) return;
        try { await Task.Delay(TimeSpan.FromSeconds(15), _lifetime.Token); }
        catch (OperationCanceledException) { return; }
        _timer.Start();
        await CycleAsync();
    }
    private async Task CycleAsync()
    {
        if (_lifetime.IsCancellationRequested || _busy || !IsPackaged) return;
        CleanupCompletedJobs();
        if (_autoEnabled() && (_lastCheck is null || DateTimeOffset.UtcNow - _lastCheck >= TimeSpan.FromHours(6)))
            await CheckNowAsync(automatic: true);
        await TryInstallAsync();
    }
    public async Task CheckNowAsync(bool automatic = false)
    {
        if (_busy || _preview || _lifetime.IsCancellationRequested) return;
        SetBusy(true, "UpdateChecking");
        try
        {
            Directory.CreateDirectory(UpdateInstallation.UpdatesRoot);
            _lastCheck = DateTimeOffset.UtcNow;
            UpdateInstallation.WriteJson(_checkState, _lastCheck.Value);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var available = await _github.CheckAsync(CurrentVersion, Variant, deadline.Token);
            if (_prepared is not null && available?.Version != UpdatePackages.ParseVersion(_prepared.Version)) _prepared = null;
            _available = available;
            _status = available is null ? "UpdateCurrent" : IsPackaged ? "UpdateAvailable" : "UpdateDevelopment";
            if (automatic && _autoEnabled() && available is not null && IsPackaged) await PrepareAsync();
        }
        catch (OperationCanceledException) { _status = "UpdateFailed"; }
        catch (Exception e) when (IsExpected(e)) { _status = "UpdateFailed"; }
        finally { SetBusy(false); }
    }
    public async Task<bool> PrepareManualAsync()
    {
        if (!CanInstall || _lifetime.IsCancellationRequested) return false;
        SetBusy(true, "UpdateDownloading");
        try { await PrepareAsync(); return _prepared is not null; }
        catch (OperationCanceledException) { _status = "UpdateFailed"; return false; }
        catch (Exception e) when (IsExpected(e)) { _status = "UpdateFailed"; return false; }
        finally { SetBusy(false); }
    }
    private async Task PrepareAsync()
    {
        if (_prepared is not null && _available?.Version == UpdatePackages.ParseVersion(_prepared.Version))
        { _status = "UpdateReady"; return; }
        if (_available is not { } release) return;
        _status = "UpdateDownloading"; Notify();
        var job = Path.Combine(UpdateInstallation.UpdatesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        var archive = Path.Combine(job, "package.zip");
        var checksum = await _github.ReadChecksumAsync(release, _lifetime.Token);
        await _github.DownloadAsync(release, archive, _lifetime.Token);
        await Task.Run(() => UpdatePackages.ExtractVerified(archive, Path.Combine(job, "staged"), checksum, release.Version, Variant), _lifetime.Token);
        _prepared = UpdateInstallation.PreparePlan(job, _install, release.Version, Variant);
        File.Delete(archive);
        _status = "UpdateReady";
    }
    public void RequestManualInstall() => _manualRequested = true;
    public async Task TryInstallAsync()
    {
        if (_busy || _prepared is not { } plan || (!_autoEnabled() && !_manualRequested) || !_canRestart() || _lifetime.IsCancellationRequested) return;
        SetBusy(true, "UpdateInstalling"); IsInstalling = true;
        try
        {
            var helper = Path.Combine(plan.JobDirectory, "updater.exe");
            await Task.Run(() => File.Copy(Environment.ProcessPath!, helper, overwrite: true), _lifetime.Token);
            if (!_canRestart()) { _status = "UpdateReady"; return; }
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = plan.JobDirectory };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(Path.Combine(plan.JobDirectory, "plan.json"));
            using var process = Process.Start(start) ?? throw new IOException("Could not start updater.");
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (File.Exists(Path.Combine(plan.JobDirectory, "ready"))) { _exit(); return; }
                if (process.HasExited) throw new IOException("Updater did not start.");
                await Task.Delay(100, _lifetime.Token);
            }
            // Cancel the helper before reporting failure; it must never apply later after an unrelated exit.
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            throw new IOException("Updater startup timed out.");
        }
        catch (OperationCanceledException) { _status = "UpdateFailed"; _prepared = null; _manualRequested = false; }
        catch (Exception e) when (IsExpected(e)) { _status = "UpdateFailed"; _prepared = null; _manualRequested = false; }
        finally { IsInstalling = false; SetBusy(false); }
    }
    private static bool IsExpected(Exception e) => e is IOException or UnauthorizedAccessException or HttpRequestException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception;
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e) => Notify();
    private void SetBusy(bool busy, string? status = null) { _busy = busy; if (status is not null) _status = status; Notify(); }
    private void Notify() { foreach (var name in new[] { nameof(Status), nameof(IsBusy), nameof(CanCheck), nameof(CanInstall) }) PropertyChanged?.Invoke(this, new(name)); }

    private static void CleanupCompletedJobs()
    {
        try
        {
            if (!Directory.Exists(UpdateInstallation.UpdatesRoot)) return;
            var jobs = new DirectoryInfo(UpdateInstallation.UpdatesRoot).EnumerateDirectories().OrderByDescending(job => job.LastWriteTimeUtc);
            var keptBackup = false;
            foreach (var job in jobs)
            {
                if (!Guid.TryParseExact(job.Name, "N", out _) || job.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    !File.Exists(Path.Combine(job.FullName, "result.json"))) continue;
                var result = JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(Path.Combine(job.FullName, "result.json")), UpdatePackages.Json);
                if (result?.Status != "success" || DateTimeOffset.UtcNow - result.CompletedAt < TimeSpan.FromSeconds(30)) continue;
                if (keptBackup) { Directory.Delete(job.FullName, recursive: true); continue; }
                keptBackup = true;
                foreach (var name in new[] { "updater.exe", "package.zip" })
                    File.Delete(UpdatePackages.ResolveFile(job.FullName, name));
                var staged = UpdatePackages.ResolveFile(job.FullName, "staged");
                if (Directory.Exists(staged) && !File.GetAttributes(staged).HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(staged, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public void Dispose()
    {
        _timer.Stop(); _lifetime.Cancel(); _http.Dispose();
        Localization.Current.PropertyChanged -= LanguageChanged;
    }
}
