using System.Text.Json;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public static class ProjectSettingsTuner
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<ProjectSettingChange> Apply(
        Project project,
        string? proposal,
        string source,
        DateTime utcNow)
    {
        var proposed = Parse(proposal);
        if (proposed.Count == 0) return Array.Empty<ProjectSettingChange>();

        var changes = new List<ProjectSettingChange>();
        foreach (var item in proposed)
        {
            if (KeyComparer.Equals(item.Key, "ValidationCommand"))
                ApplyString(project, "ValidationCommand", project.ValidationCommand, item.Value, source, utcNow, changes,
                    v => project.ValidationCommand = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
            else if (KeyComparer.Equals(item.Key, "ProviderPriority"))
                ApplyString(project, "ProviderPriority", project.ProviderPriority, item.Value, source, utcNow, changes,
                    v => project.ProviderPriority = v.Trim());
            else if (KeyComparer.Equals(item.Key, "MaxRunMinutes"))
                ApplyMaxRunMinutes(project, item.Value, source, utcNow, changes);
        }

        return changes;
    }

    private static void ApplyString(
        Project project,
        string key,
        string? oldValue,
        string? proposed,
        string source,
        DateTime utcNow,
        List<ProjectSettingChange> changes,
        Action<string> apply)
    {
        if (string.IsNullOrWhiteSpace(proposed)) return;
        var next = proposed.Trim();
        if (string.Equals(oldValue ?? string.Empty, next, StringComparison.Ordinal)) return;
        apply(next);
        changes.Add(Change(project.Id, key, oldValue, next, source, utcNow));
    }

    private static void ApplyMaxRunMinutes(
        Project project,
        string? proposed,
        string source,
        DateTime utcNow,
        List<ProjectSettingChange> changes)
    {
        if (!int.TryParse(proposed, out var minutes) || minutes <= 0) return;
        if (project.MaxRunMinutes == minutes) return;
        var old = project.MaxRunMinutes.ToString();
        project.MaxRunMinutes = minutes;
        changes.Add(Change(project.Id, "MaxRunMinutes", old, minutes.ToString(), source, utcNow));
    }

    private static ProjectSettingChange Change(
        int projectId,
        string key,
        string? oldValue,
        string? newValue,
        string source,
        DateTime utcNow) => new()
    {
        ProjectId = projectId,
        Key = key,
        OldValue = oldValue,
        NewValue = newValue,
        Source = source,
        CreatedAt = utcNow
    };

    private static Dictionary<string, string?> Parse(string? proposal)
    {
        var result = new Dictionary<string, string?>(KeyComparer);
        if (string.IsNullOrWhiteSpace(proposal)) return result;

        var text = proposal.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return result;

        if (text.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind is not JsonValueKind.Object) return result;
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = prop.Value.ValueKind is JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
                return result;
            }
            catch (JsonException)
            {
                return result;
            }
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf(':');
            if (idx < 0) idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length > 0) result[key] = value;
        }

        return result;
    }
}
