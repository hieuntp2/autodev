using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class GuardrailValidator
{
    private static readonly string[] DependencyFileNames =
    [
        "build.gradle",
        "settings.gradle",
        "gradle.properties",
        "package.json",
        "Directory.Packages.props"
    ];

    public GuardrailResult Validate(ProjectConfig project, IEnumerable<string> changedFiles, string diff)
    {
        var normalizedChangedFiles = changedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var protectedPaths = project.ProtectedPaths.Select(NormalizePath).ToArray();
        var blocked = normalizedChangedFiles
            .Where(file => IsProtected(file, protectedPaths) || IsBlockedRequirementFile(project, file))
            .ToArray();

        var dependencyChanges = normalizedChangedFiles
            .Where(IsDependencyFile)
            .ToArray();

        var diffLines = CountDiffLines(diff);
        var isDiffTooLarge = project.MaxDiffLines > 0 && diffLines > project.MaxDiffLines;

        return new GuardrailResult
        {
            HasProtectedPathChanges = blocked.Length > 0,
            ProtectedPathChanges = blocked,
            DependencyChanges = dependencyChanges,
            DiffLines = diffLines,
            IsDiffTooLarge = isDiffTooLarge,
            CanAutoCommit = blocked.Length == 0 && !isDiffTooLarge
        };
    }

    private static bool IsBlockedRequirementFile(ProjectConfig project, string file)
    {
        if (project.AllowRequirementDirectEdit)
        {
            return false;
        }

        var requirementFiles = new[]
        {
            project.MainRequirementFile,
            project.ScopeFile
        };

        return requirementFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Trim().Replace('\\', '/').TrimStart('/'))
            .Contains(file, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsProtected(string file, IReadOnlyCollection<string> protectedPaths)
    {
        foreach (var protectedPath in protectedPaths)
        {
            if (protectedPath.EndsWith('/'))
            {
                if (file.StartsWith(protectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(file, protectedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDependencyFile(string file)
    {
        var name = Path.GetFileName(file);
        return DependencyFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountDiffLines(string diff)
    {
        if (string.IsNullOrEmpty(diff))
        {
            return 0;
        }

        return diff.Split(["\r\n", "\n"], StringSplitOptions.None).Length;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('\\', '/').TrimStart('/');
    }
}
