using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CodexUsageWidget.Core;

namespace CodexUsageWidget;

internal static class SessionPicker
{
    internal sealed record Group(string Key, string Name, IReadOnlyList<ThreadChoice> Threads);

    internal static IReadOnlyList<Group> GroupThreads(IReadOnlyList<ThreadChoice> threads, string? metadata)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var createdAt = new Dictionary<string, long>(StringComparer.Ordinal);
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(metadata ?? "{}");
            var root = doc.RootElement;
            if (root.TryGetProperty("local-projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
                foreach (var project in projects.EnumerateObject())
                {
                    if (project.Value.ValueKind != JsonValueKind.Object) continue;
                    if (project.Value.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        names[project.Name] = name.GetString()!;
                    if (project.Value.TryGetProperty("createdAt", out var date) && date.ValueKind == JsonValueKind.Number &&
                        date.TryGetInt64(out var timestamp)) createdAt[project.Name] = timestamp;
                }
            if (root.TryGetProperty("thread-project-assignments", out var map) && map.ValueKind == JsonValueKind.Object)
                foreach (var item in map.EnumerateObject())
                    if (item.Value.ValueKind == JsonValueKind.Object && item.Value.TryGetProperty("projectId", out var id) && id.ValueKind == JsonValueKind.String)
                        assignments[item.Name] = id.GetString()!;
        }
        catch (JsonException) { }
        return threads.GroupBy(thread => assignments.GetValueOrDefault(thread.Id) ?? "")
            .Select(group => new Group(group.Key, group.Key.Length == 0 ? Localization.Current["NoProject"] :
                names.GetValueOrDefault(group.Key) ?? group.Select(thread => thread.Cwd?.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault())
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key,
                group.OrderByDescending(thread => thread.LastUsedAt).ToArray()))
            .OrderBy(group => group.Key.Length == 0)
            .ThenByDescending(group => createdAt.GetValueOrDefault(group.Key, long.MinValue))
            .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static async Task<IReadOnlyList<ThreadChoice>> ReadAllAsync(CodexClient client, string? search = null)
    {
        var result = new Dictionary<string, ThreadChoice>();
        var cursors = new HashSet<string>();
        string? cursor = null;
        do
        {
            var page = await client.ReadThreadsAsync(cursor, search);
            foreach (var thread in page.Threads) result[thread.Id] = thread;
            cursor = page.Cursor;
        } while (cursor is not null && cursors.Add(cursor));
        return result.Values.ToArray();
    }

    internal static ContextMenu Build(IReadOnlyList<Group> groups, string? selected, Action<ThreadChoice> choose)
    {
        var menu = new ContextMenu { MaxHeight = 440, MaxWidth = 540 };
        foreach (var group in groups)
        {
            var heading = new MenuItem
            {
                Header = new TextBlock { Text = group.Name, MaxWidth = 420, TextTrimming = TextTrimming.CharacterEllipsis },
                IsChecked = group.Threads.Any(thread => thread.Id == selected)
            };
            foreach (var thread in group.Threads)
            {
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = thread.Name, MaxWidth = 460, TextTrimming = TextTrimming.CharacterEllipsis },
                    ToolTip = thread.Name, IsCheckable = true, IsChecked = thread.Id == selected
                };
                item.Click += (_, e) => { e.Handled = true; menu.IsOpen = false; choose(thread); };
                heading.Items.Add(item);
            }
            menu.Items.Add(heading);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = Localization.Current["NoThreads"], IsEnabled = false });
        return menu;
    }

    internal static async Task<ContextMenu?> OpenAsync(FrameworkElement target, Func<Task<IReadOnlyList<ThreadChoice>>> load,
        string? selected, Action<ThreadChoice> choose)
    {
        var loading = new ContextMenu { PlacementTarget = target };
        loading.Items.Add(new MenuItem { Header = Localization.Current["Updating"], IsEnabled = false });
        loading.IsOpen = true;
        try
        {
            var threads = await load();
            var metadata = await Task.Run(() =>
            {
                try
                {
                    var home = Environment.GetEnvironmentVariable("CODEX_HOME") ??
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                    return File.ReadAllText(Path.Combine(home, ".codex-global-state.json"));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
            });
            if (!loading.IsOpen) return null;
            loading.IsOpen = false;
            var menu = Build(GroupThreads(threads, metadata), selected, choose);
            menu.PlacementTarget = target;
            menu.IsOpen = true;
            return menu;
        }
        catch (Exception e) when (e is IOException or UsageUnavailableException or TimeoutException or InvalidOperationException or ObjectDisposedException)
        {
            if (loading.IsOpen) { loading.Items.Clear(); loading.Items.Add(new MenuItem { Header = Localization.Current["ConnectionError"], IsEnabled = false }); }
        }
        return loading;
    }
}
