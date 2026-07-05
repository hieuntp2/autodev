namespace AutoDevRunner.Skills;

/// <summary>
/// Optionally mirrors a selected skill into a target repo's working tree at
/// <c>.agents/skills/&lt;id&gt;</c> so a CLI that discovers skills from disk can see
/// it. The path is added to the repo's local git exclude (<c>.git/info/exclude</c>)
/// so the copy is never committed — the global store stays the single source of
/// truth and project repos stay clean. Best-effort and fail-soft. Singleton.
/// </summary>
public class SkillExporter
{
    private readonly ILogger<SkillExporter> _log;

    public SkillExporter(ILogger<SkillExporter> log) => _log = log;

    /// <summary>Returns the export path, or null on failure / nothing to do.</summary>
    public string? Export(SkillManifest skill, string repoPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(skill.SourcePath) || !Directory.Exists(skill.SourcePath))
                return null;

            var destRoot = Path.Combine(repoPath, ".agents", "skills");
            var dest = Path.Combine(destRoot, skill.Id);
            CopyDirectory(skill.SourcePath, dest, skipDirs: new[] { "samples", "__pycache__", ".git" });
            EnsureGitExcluded(repoPath, ".agents/");
            _log.LogInformation("Exported skill '{Id}' to {Dest} (git-excluded).", skill.Id, dest);
            return dest;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to export skill '{Id}' to {Repo}.", skill.Id, repoPath);
            return null;
        }
    }

    private static void CopyDirectory(string src, string dest, string[] skipDirs)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);

        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var name = Path.GetFileName(dir);
            if (skipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyDirectory(dir, Path.Combine(dest, name), skipDirs);
        }
    }

    private static void EnsureGitExcluded(string repoPath, string entry)
    {
        var excludePath = Path.Combine(repoPath, ".git", "info", "exclude");
        var dir = Path.GetDirectoryName(excludePath);
        if (dir is null || !Directory.Exists(Path.Combine(repoPath, ".git"))) return;
        Directory.CreateDirectory(dir);

        var lines = File.Exists(excludePath) ? File.ReadAllLines(excludePath).ToList() : new List<string>();
        if (!lines.Any(l => l.Trim() == entry.Trim()))
        {
            lines.Add(entry);
            File.WriteAllLines(excludePath, lines);
        }
    }
}
