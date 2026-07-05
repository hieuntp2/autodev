using System.Text.RegularExpressions;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

/// <summary>
/// Recognises generated asset artifacts among a run's changed files — sprite
/// sheets, GIFs, animation manifests, frame PNGs, other images, and reports —
/// and records their repo-relative paths so the dashboard can list (and preview)
/// them per project/run. Output from the pixel-animation-artist skill is
/// recognised first-class. Code/source files are intentionally NOT tracked as
/// artifacts. Pure and dependency-free for easy testing.
/// </summary>
public class ArtifactTracker
{
    private static readonly Regex FrameRx = new(@"(^|/)frames?/frame_\d+\.png$|(^|/)frame_\d+\.png$", RegexOptions.IgnoreCase);
    private const string PixelSkill = "pixel-animation-artist";

    /// <summary>Classify a single repo-relative path, or null if it is not an artifact.</summary>
    public ArtifactRef? Classify(string path, string repoPath)
    {
        var rel = path.Replace('\\', '/');
        var lower = rel.ToLowerInvariant();

        var (kind, skill) = lower switch
        {
            _ when lower.EndsWith(".animation.json") => (ArtifactKind.AnimationManifest, PixelSkill),
            _ when lower.EndsWith("_sheet.png") => (ArtifactKind.SpriteSheet, PixelSkill),
            _ when lower.EndsWith("_preview.gif") => (ArtifactKind.Gif, PixelSkill),
            _ when FrameRx.IsMatch(rel) => (ArtifactKind.Frame, PixelSkill),
            _ when lower.EndsWith(".gif") => (ArtifactKind.Gif, (string?)null),
            _ when lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")
                   || lower.EndsWith(".webp") => (ArtifactKind.Image, (string?)null),
            _ when lower.Contains("/runs/") && lower.EndsWith(".md") => (ArtifactKind.Report, (string?)null),
            _ => (ArtifactKind.Other, (string?)null)
        };

        if (kind == ArtifactKind.Other) return null;

        long size = 0;
        try
        {
            var full = Path.Combine(repoPath, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) size = new FileInfo(full).Length;
        }
        catch { /* size is best-effort */ }

        return new ArtifactRef(rel, kind, skill, size);
    }

    /// <summary>Track all artifact files among a run's changed files.</summary>
    public List<ArtifactRef> Track(IEnumerable<string> changedFiles, string repoPath)
        => Order(changedFiles.Select(f => Classify(f, repoPath)).Where(a => a is not null).Select(a => a!));

    /// <summary>
    /// Track artifacts declared by the AI in its summary (ARTIFACT_PATHS /
    /// GENERATED_FILES / ASSET_PATH), merged with the git-detected list. A
    /// declared path may be a file or a directory (expanded to its asset files).
    /// Paths outside the repo are ignored.
    /// </summary>
    public List<ArtifactRef> Merge(IReadOnlyList<ArtifactRef> gitTracked, string? declaredField, string repoPath)
    {
        var byPath = new Dictionary<string, ArtifactRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in gitTracked) byPath[a.Path] = a;

        foreach (var token in SplitPaths(declaredField))
        {
            var rel = ToRepoRelative(token, repoPath);
            if (rel is null) continue;
            var full = Path.Combine(repoPath, rel.Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(full))
            {
                foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                {
                    var childRel = Path.GetRelativePath(repoPath, file).Replace('\\', '/');
                    var a = Classify(childRel, repoPath);
                    if (a is not null) byPath.TryAdd(a.Path, a);
                }
            }
            else
            {
                var a = Classify(rel, repoPath);
                if (a is not null) byPath.TryAdd(a.Path, a);
            }
        }
        return Order(byPath.Values);
    }

    private static List<ArtifactRef> Order(IEnumerable<ArtifactRef> items)
        => items.OrderBy(a => a.Kind).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToList();

    private static IEnumerable<string> SplitPaths(string? field)
        => string.IsNullOrWhiteSpace(field)
            ? Enumerable.Empty<string>()
            : field.Split(new[] { '\n', '\r', ',', ';', '`' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.TrimStart('-', '*', ' ').Trim())
                    .Where(t => t.Length > 0);

    /// <summary>Normalise a declared path to a repo-relative path, or null if it escapes the repo.</summary>
    private static string? ToRepoRelative(string token, string repoPath)
    {
        try
        {
            var root = Path.GetFullPath(repoPath);
            var full = Path.IsPathRooted(token) ? Path.GetFullPath(token) : Path.GetFullPath(Path.Combine(root, token));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            return Path.GetRelativePath(root, full).Replace('\\', '/');
        }
        catch { return null; }
    }
}
