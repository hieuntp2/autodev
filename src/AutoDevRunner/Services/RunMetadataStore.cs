using System.Text.Json;
using System.Text.Json.Serialization;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// Reads/writes the per-run JSON sidecar (<c>&lt;stamp&gt;-run&lt;id&gt;.json</c>) that lives
/// next to the run's markdown log under <c>&lt;repo&gt;/.ai-runner/runs/</c>. This is the
/// dashboard's structured source for task lifecycle, selected skills, risk and
/// artifacts — no DB schema change required.
/// </summary>
public class RunMetadataStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Sidecar path derived from the markdown log path (…run7.md → …run7.json).</summary>
    public static string SidecarPathFor(string markdownLogPath) =>
        Path.ChangeExtension(markdownLogPath, ".json");

    public async Task WriteAsync(string markdownLogPath, RunMetadata meta, CancellationToken ct = default)
    {
        var path = SidecarPathFor(markdownLogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(meta, JsonOpts), ct);
    }

    public RunMetadata? Read(string sidecarPath)
    {
        try
        {
            return File.Exists(sidecarPath)
                ? JsonSerializer.Deserialize<RunMetadata>(File.ReadAllText(sidecarPath), JsonOpts)
                : null;
        }
        catch { return null; }
    }

    public RunMetadata? ReadForLog(string? markdownLogPath) =>
        string.IsNullOrWhiteSpace(markdownLogPath) ? null : Read(SidecarPathFor(markdownLogPath));

    /// <summary>All run sidecars for a repo, newest first.</summary>
    public IReadOnlyList<RunMetadata> ReadAllForRepo(string repoPath)
    {
        var dir = Path.Combine(repoPath, ProjectGoalService.Dir, "runs");
        if (!Directory.Exists(dir)) return Array.Empty<RunMetadata>();
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(Read)
            .Where(m => m is not null)
            .Select(m => m!)
            .OrderByDescending(m => m.StartedAt)
            .ToList();
    }
}
