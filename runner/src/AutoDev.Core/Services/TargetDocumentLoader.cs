using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class TargetDocumentLoader
{
    private static readonly string[] SkippedPatterns =
    [
        ".env",
        "local.properties",
        "appsettings.production.json"
    ];

    private static readonly string[] SkippedPrefixes =
    [
        "secrets/",
        "keystore/",
        ".git/"
    ];

    public async Task<TargetRepoContext> LoadAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();

        var agentRules = await ReadDocAsync(project.RepoPath, project.AgentRulesFile, missing, cancellationToken);
        var mainRequirement = await ReadDocAsync(project.RepoPath, project.MainRequirementFile, missing, cancellationToken);
        var scope = await ReadDocAsync(project.RepoPath, project.ScopeFile, missing, cancellationToken);
        var roadmap = await ReadDocAsync(project.RepoPath, project.RoadmapFile, missing, cancellationToken);
        var backlog = await ReadDocAsync(project.RepoPath, project.BacklogFile, missing, cancellationToken);
        var status = await ReadDocAsync(project.RepoPath, project.StatusFile, missing, cancellationToken);

        return new TargetRepoContext
        {
            AgentRules = agentRules,
            MainRequirement = mainRequirement,
            Scope = scope,
            Roadmap = roadmap,
            Backlog = backlog,
            Status = status,
            MissingDocPaths = missing
        };
    }

    private static async Task<string> ReadDocAsync(
        string repoPath,
        string? relativePath,
        List<string> missingPaths,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        if (ShouldSkip(normalized))
        {
            return string.Empty;
        }

        var fullPath = Path.Combine(repoPath, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            missingPaths.Add(relativePath);
            return string.Empty;
        }

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    private static bool ShouldSkip(string normalizedPath)
    {
        if (SkippedPatterns.Any(p => string.Equals(normalizedPath, p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return SkippedPrefixes.Any(p => normalizedPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
