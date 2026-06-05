using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class ProjectConfigValidator
{
    public ConfigValidationResult Validate(ProjectConfig config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(config.ProjectId))
        {
            errors.Add("projectId is required.");
        }

        if (string.IsNullOrWhiteSpace(config.DisplayName))
        {
            errors.Add("displayName is required.");
        }

        if (string.IsNullOrWhiteSpace(config.RepoPath))
        {
            errors.Add("repoPath is required.");
        }
        else if (!Directory.Exists(config.RepoPath))
        {
            errors.Add($"repoPath does not exist on disk: {config.RepoPath}");
        }

        if (string.IsNullOrWhiteSpace(config.Branch))
        {
            errors.Add("branch is required.");
        }

        if (string.IsNullOrWhiteSpace(config.ProjectType))
        {
            errors.Add("projectType is required.");
        }

        if (string.IsNullOrWhiteSpace(config.BacklogFile))
        {
            errors.Add("backlogFile is required for autonomous task selection.");
        }

        if (config.BuildCommands.Count == 0)
        {
            warnings.Add("buildCommands is empty — build verification will be skipped.");
        }

        if (errors.Count > 0)
        {
            return ConfigValidationResult.Invalid(errors, warnings);
        }

        ValidateDocPaths(config, errors, warnings);
        ValidatePathPolicy(config, warnings);

        return errors.Count > 0
            ? ConfigValidationResult.Invalid(errors, warnings)
            : ConfigValidationResult.Valid(warnings);
    }

    private static void ValidateDocPaths(ProjectConfig config, List<string> errors, List<string> warnings)
    {
        var repoPath = config.RepoPath!;

        CheckDocPath(repoPath, config.BacklogFile, "backlogFile", errors);
        CheckDocPathOptional(repoPath, config.MainRequirementFile, "mainRequirementFile", warnings);
        CheckDocPathOptional(repoPath, config.ScopeFile, "scopeFile", warnings);
        CheckDocPathOptional(repoPath, config.RoadmapFile, "roadmapFile", warnings);
        CheckDocPathOptional(repoPath, config.StatusFile, "statusFile", warnings);
        CheckDocPathOptional(repoPath, config.AgentRulesFile, "agentRulesFile", warnings);
    }

    private static void CheckDocPath(string repoPath, string? relativePath, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var fullPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            errors.Add($"{fieldName} not found at: {relativePath}");
        }
    }

    private static void CheckDocPathOptional(string repoPath, string? relativePath, string fieldName, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var fullPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            warnings.Add($"{fieldName} configured but not found at: {relativePath}");
        }
    }

    private static void ValidatePathPolicy(ProjectConfig config, List<string> warnings)
    {
        var policy = new PathPolicy(config.AllowedWritePaths, config.ProtectedPaths);
        if (policy.HasPathOverlap(out var overlaps))
        {
            foreach (var overlap in overlaps)
            {
                warnings.Add($"Path overlap detected: {overlap}. Protected paths take precedence.");
            }
        }
    }
}
