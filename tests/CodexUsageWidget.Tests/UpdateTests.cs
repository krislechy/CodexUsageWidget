using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CodexUsageWidget.Core;

internal static class UpdateTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("updates select the matching variant and never downgrade", () => Check(() =>
        {
            using var release = JsonDocument.Parse(ReleaseJson("v1.2.0"));
            var lite = GitHubUpdates.ParseRelease(release.RootElement, new(1, 1, 0), "Lite")!;
            Assert(lite.Archive.Name == "CodexUsageWidget-win-x64-lite.zip");
            Assert(GitHubUpdates.ParseRelease(release.RootElement, new(1, 2, 0), "Lite") is null);
            Assert(GitHubUpdates.ParseRelease(release.RootElement, new(2, 0, 0), "Lite") is null);
            using var preview = JsonDocument.Parse(ReleaseJson("v2.0.0", preview: true));
            Assert(GitHubUpdates.ParseRelease(preview.RootElement, new(1, 0, 0), "Portable") is null);
            using var invalid = JsonDocument.Parse(ReleaseJson("v2.0.0-beta"));
            Assert(GitHubUpdates.ParseRelease(invalid.RootElement, new(1, 0, 0), "Portable") is null);
        })),
        ("updates reject foreign URLs and missing checksum assets", () => Check(() =>
        {
            using var foreign = JsonDocument.Parse(ReleaseJson("v2.0.0").Replace("https://github.com/krislechy/", "https://github.com/other/"));
            Throws<InvalidDataException>(() => GitHubUpdates.ParseRelease(foreign.RootElement, new(1, 0, 0), "Lite"));
            using var missing = JsonDocument.Parse(ReleaseJson("v2.0.0").Replace("lite.zip.sha256", "lite.missing.sha256"));
            Throws<InvalidDataException>(() => GitHubUpdates.ParseRelease(missing.RootElement, new(1, 0, 0), "Lite"));
        })),
        ("update checks use public GitHub headers and tolerate no releases", async () =>
        {
            using var http = new HttpClient(new Handler(request =>
            {
                Assert(request.RequestUri!.AbsoluteUri == GitHubUpdates.LatestUrl);
                Assert(request.Headers.Authorization is null && request.Headers.UserAgent.Any());
                return new(HttpStatusCode.NotFound);
            }));
            Assert(await new GitHubUpdates(http).CheckAsync(new(1, 0, 0), "Lite") is null);
        }),
        ("checksum files must identify the exact archive", async () =>
        {
            using var root = JsonDocument.Parse(ReleaseJson("v2.0.0"));
            var release = GitHubUpdates.ParseRelease(root.RootElement, new(1, 0, 0), "Lite")!;
            using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('a', 64) + "  wrong.zip") }));
            await ThrowsAsync<InvalidDataException>(() => new GitHubUpdates(http).ReadChecksumAsync(release));
        }),
        ("verified updates preserve local files and remove obsolete owned files", () => Sandbox((install, stage, job) =>
        {
            Package(install, "1.0.0", "Lite", ("CodexUsageWidget.exe", "old"), ("obsolete.txt", "obsolete"));
            Package(stage, "1.1.0", "Lite", ("CodexUsageWidget.exe", "new"), ("docs/new.txt", "documentation"));
            File.WriteAllText(Path.Combine(install, "settings.json"), "user settings");
            UpdateInstallation.ApplyFiles(new(job, install, "1.1.0", "Lite", 0, 0));
            Assert(File.ReadAllText(Path.Combine(install, "CodexUsageWidget.exe")) == "new");
            Assert(File.ReadAllText(Path.Combine(install, "settings.json")) == "user settings");
            Assert(!File.Exists(Path.Combine(install, "obsolete.txt")));
            UpdateInstallation.Rollback(new(job, install, "1.1.0", "Lite", 0, 0));
            Assert(File.ReadAllText(Path.Combine(install, "CodexUsageWidget.exe")) == "old");
            Assert(File.Exists(Path.Combine(install, "obsolete.txt")) && !File.Exists(Path.Combine(install, "docs/new.txt")));
            Assert(UpdatePackages.ReadManifest(install).Version == "1.0.0");
        })),
        ("failed replacement rolls back files already installed", () => Sandbox((install, stage, job) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            Package(install, "1.0.0", "Lite", ("CodexUsageWidget.exe", "old"), ("obsolete.txt", "locked"));
            Package(stage, "1.1.0", "Lite", ("CodexUsageWidget.exe", "new"), ("new.txt", "new file"));
            using var locked = new FileStream(Path.Combine(install, "obsolete.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
            Throws<IOException>(() => UpdateInstallation.ApplyFiles(new(job, install, "1.1.0", "Lite", 0, 0)));
            Assert(File.ReadAllText(Path.Combine(install, "CodexUsageWidget.exe")) == "old");
            Assert(!File.Exists(Path.Combine(install, "new.txt")) && UpdatePackages.ReadManifest(install).Version == "1.0.0");
        })),
        ("updates refuse user-file collisions and corrupt staged files", () => Sandbox((install, stage, job) =>
        {
            Package(install, "1.0.0", "Lite", ("CodexUsageWidget.exe", "old"));
            Package(stage, "1.1.0", "Lite", ("CodexUsageWidget.exe", "new"), ("notes.txt", "release notes"));
            File.WriteAllText(Path.Combine(install, "notes.txt"), "user notes");
            Throws<IOException>(() => UpdateInstallation.ApplyFiles(new(job, install, "1.1.0", "Lite", 0, 0)));
            Assert(File.ReadAllText(Path.Combine(install, "notes.txt")) == "user notes");
            File.WriteAllText(Path.Combine(stage, "CodexUsageWidget.exe"), "tampered");
            Throws<InvalidDataException>(() => UpdateInstallation.ApplyFiles(new(job, install, "1.1.0", "Lite", 0, 0)));
            Assert(File.ReadAllText(Path.Combine(install, "CodexUsageWidget.exe")) == "old");
        })),
        ("release extraction validates hashes, variants and archive paths", () => Sandbox((install, stage, job) =>
        {
            Package(install, "1.1.0", "Lite", ("CodexUsageWidget.exe", "new"));
            var archive = Path.Combine(job, "release.zip");
            ZipFile.CreateFromDirectory(install, archive);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
            UpdatePackages.ExtractVerified(archive, stage, hash, new(1, 1, 0), "Lite");
            Assert(File.ReadAllText(Path.Combine(stage, "CodexUsageWidget.exe")) == "new");
            Throws<InvalidDataException>(() => UpdatePackages.ExtractVerified(archive, stage + "-bad", new string('0', 64), new(1, 1, 0), "Lite"));
            Throws<InvalidDataException>(() => UpdatePackages.ExtractVerified(archive, stage + "-wrong", hash, new(1, 1, 0), "Portable"));
            var malicious = Path.Combine(job, "malicious.zip");
            using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create)) { using var text = new StreamWriter(zip.CreateEntry("../escaped.txt").Open()); text.Write("escape"); }
            var maliciousHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(malicious)));
            Throws<InvalidDataException>(() => UpdatePackages.ExtractVerified(malicious, stage + "-escape", maliciousHash, new(1, 1, 0), "Lite"));
            Assert(!File.Exists(Path.Combine(job, "escaped.txt")));
            Throws<InvalidDataException>(() => UpdatePackages.ResolveFile(stage, "folder/../outside.txt"));
            Throws<InvalidDataException>(() => UpdatePackages.ResolveFile(stage, "file.txt:stream"));
            Throws<InvalidDataException>(() => UpdatePackages.ResolveFile(stage, "NUL.txt"));
        }))
    ];

    private static string ReleaseJson(string version, bool preview = false)
    {
        var prefix = "https://github.com/krislechy/CodexUsageWidget/releases/download/" + version + "/";
        var assets = new[] { "CodexUsageWidget-win-x64.zip", "CodexUsageWidget-win-x64.zip.sha256", "CodexUsageWidget-win-x64-lite.zip", "CodexUsageWidget-win-x64-lite.zip.sha256" }
            .Select(name => new { name, browser_download_url = prefix + name, size = 100 });
        return JsonSerializer.Serialize(new { tag_name = version, draft = false, prerelease = preview, assets });
    }
    private static void Package(string root, string version, string variant, params (string Path, string Body)[] values)
    {
        Directory.CreateDirectory(root);
        var files = new List<PackageFile>();
        foreach (var (relative, body) in values)
        {
            var path = UpdatePackages.ResolveFile(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, body);
            files.Add(new(relative, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), new FileInfo(path).Length));
        }
        File.WriteAllText(Path.Combine(root, UpdatePackages.ManifestName), JsonSerializer.Serialize(new PackageManifest(UpdatePackages.Product, version, variant, files), UpdatePackages.Json));
    }
    private static Task Sandbox(Action<string, string, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexUsageWidget-update-tests-" + Guid.NewGuid().ToString("N"));
        var job = Path.Combine(root, "job"); Directory.CreateDirectory(job);
        try { action(Path.Combine(root, "install"), Path.Combine(job, "staged"), job); }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }
    private static Task Check(Action action) { action(); return Task.CompletedTask; }
    private static void Assert(bool value) { if (!value) throw new Exception("Updater assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
