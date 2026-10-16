using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace CodexUsageWidget.Core;

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);
public sealed record WidgetRelease(Version Version, string Tag, Uri Page, ReleaseAsset Archive, ReleaseAsset Checksum);
public sealed record PackageFile(string Path, string Sha256, long Length);
public sealed record PackageManifest(string Product, string Version, string Variant, List<PackageFile> Files);

public static class UpdatePackages
{
    public const string ManifestName = "update-manifest.json";
    public const string Product = "CodexUsageWidget";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string ArchiveName(string variant) => variant switch
    {
        "Lite" => "CodexUsageWidget-win-x64-lite.zip",
        "Portable" => "CodexUsageWidget-win-x64.zip",
        _ => throw new InvalidDataException("Unknown package variant.")
    };
    public static Version? ParseVersion(string? tag) => tag is not null &&
        Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$", RegexOptions.CultureInvariant) &&
        Version.TryParse(tag.TrimStart('v'), out var version) ? version : null;

    public static string ResolveFile(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length == 0 || parts.Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
            part.EndsWith(' ') || part.EndsWith('.') || part.Any(char.IsControl) ||
            Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains(':')) ||
            parts[0].Equals(".git", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe package path.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes its directory.");
        var parent = Path.GetDirectoryName(result);
        while (parent is not null && parent.Length >= fullRoot.Length - 1)
        {
            if (Directory.Exists(parent) && File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Package directory contains a link.");
            parent = Path.GetDirectoryName(parent);
        }
        if (File.Exists(result) && File.GetAttributes(result).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Package file is a link.");
        return result;
    }

    public static PackageManifest ReadManifest(string directory)
    {
        var path = ResolveFile(directory, ManifestName);
        if (new FileInfo(path).Length > 1_000_000) throw new InvalidDataException("Package manifest is too large.");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Missing package manifest.");
        if (manifest.Product != Product || ParseVersion(manifest.Version) is null || manifest.Variant is not ("Portable" or "Lite") ||
            manifest.Files is not { Count: > 0 and <= 2000 } ||
            manifest.Files.Any(file => file is null || string.IsNullOrWhiteSpace(file.Path) || file.Path.Contains('\\') || string.IsNullOrWhiteSpace(file.Sha256)) ||
            manifest.Files.Select(file => file.Path.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count ||
            !manifest.Files.Any(file => file.Path == "CodexUsageWidget.exe")) throw new InvalidDataException("Invalid package manifest.");
        foreach (var file in manifest.Files)
        {
            _ = ResolveFile(directory, file.Path);
            if (file.Path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || file.Length < 0 || file.Length > 300_000_000 ||
                !Regex.IsMatch(file.Sha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new InvalidDataException("Invalid package file.");
        }
        if (manifest.Files.Sum(file => file.Length) > 600_000_000) throw new InvalidDataException("Package is too large.");
        return manifest;
    }

    public static void VerifyFiles(string directory, PackageManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            var path = ResolveFile(directory, file.Path);
            using var stream = File.OpenRead(path);
            if (stream.Length != file.Length || !Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Package file verification failed.");
        }
    }

    public static void ExtractVerified(string archivePath, string stage, string expectedHash, Version version, string variant)
    {
        using (var stream = File.OpenRead(archivePath))
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release checksum mismatch.");
        if (Directory.Exists(stage)) throw new IOException("Update staging directory already exists.");
        Directory.CreateDirectory(stage);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 2500) throw new InvalidDataException("Too many package entries.");
        long expanded = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (relative.Length == 0) continue;
            var target = ResolveFile(stage, relative);
            if (!paths.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                entry.Length > 300_000_000 || (expanded += entry.Length) > 600_000_000)
                throw new InvalidDataException("Unsafe release archive.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
        }
        var manifest = ReadManifest(stage);
        if (ParseVersion(manifest.Version) != version || manifest.Variant != variant) throw new InvalidDataException("Release identity does not match the requested package.");
        var allowed = manifest.Files.Select(file => ResolveFile(stage, file.Path)).Append(ResolveFile(stage, ManifestName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Any(file => !allowed.Contains(file)))
            throw new InvalidDataException("Unlisted release files.");
        VerifyFiles(stage, manifest);
    }
}

public sealed class GitHubUpdates(HttpClient http)
{
    public const string Repository = "krislechy/CodexUsageWidget";
    public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
    public async Task<WidgetRelease?> CheckAsync(Version current, string variant, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.UserAgent.ParseAdd("CodexUsageWidget/" + current.ToString(3));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await ReadBoundedAsync(response, 2_000_000, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(bytes);
        return ParseRelease(json.RootElement, current, variant);
    }

    public static WidgetRelease? ParseRelease(JsonElement root, Version current, string variant)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out var preview) || preview.ValueKind != JsonValueKind.False) return null;
        var tag = ExtraJson.Text(root, "tag_name");
        var version = UpdatePackages.ParseVersion(tag);
        if (version is null || version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        var name = UpdatePackages.ArchiveName(variant);
        var prefix = "https://github.com/" + Repository + "/releases/download/" + tag + "/";
        ReleaseAsset Asset(string expected)
        {
            var rows = ExtraJson.Array(root, "assets")?.Where(row => ExtraJson.Text(row, "name") == expected).ToArray();
            if (rows is not { Length: 1 } || ExtraJson.Text(rows[0], "browser_download_url") != prefix + expected ||
                ExtraJson.Number(rows[0], "size") is not (> 0 and <= 300_000_000)) throw new InvalidDataException("Release assets are incomplete or invalid.");
            return new(expected, new Uri(prefix + expected), ExtraJson.Number(rows[0], "size")!.Value);
        }
        return new(version, tag!, new Uri("https://github.com/" + Repository + "/releases/tag/" + tag), Asset(name), Asset(name + ".sha256"));
    }

    public async Task DownloadAsync(WidgetRelease release, string archivePath, CancellationToken token = default)
    {
        using var response = await http.GetAsync(release.Archive.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920]; long size = 0;
        while (await input.ReadAsync(buffer, token).ConfigureAwait(false) is var read && read > 0)
        {
            size += read;
            if (size > release.Archive.Size || size > 300_000_000) throw new InvalidDataException("Release download exceeds its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        if (size != release.Archive.Size) throw new InvalidDataException("Release download is incomplete.");
    }

    public async Task<string> ReadChecksumAsync(WidgetRelease release, CancellationToken token = default)
    {
        using var response = await http.GetAsync(release.Checksum.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = System.Text.Encoding.UTF8.GetString(await ReadBoundedAsync(response, 4096, token).ConfigureAwait(false)).Trim();
        var match = Regex.Match(text, @"\A([a-fA-F0-9]{64})[ \t]+\*?" + Regex.Escape(release.Archive.Name) + @"\z", RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("Invalid release checksum file.");
        return match.Groups[1].Value;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int max, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Response too large.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (await input.ReadAsync(buffer, token).ConfigureAwait(false) is var read && read > 0)
        {
            if (output.Length + read > max) throw new InvalidDataException("Response too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
