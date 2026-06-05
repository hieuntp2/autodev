using System.Text.Json;
using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class WorkspaceService(string rootPath)
{
    private static readonly string[] DailySubfolders =
    [
        "00-input",
        "01-planning",
        "02-implementation",
        "03-verification",
        "04-review",
        "05-retrospective"
    ];

    public async Task<RunContext> CreateAsync(ProjectConfig project, DateOnly runDate, CancellationToken cancellationToken = default)
    {
        var workspacePath = Path.Combine(rootPath, "workspaces", project.ProjectId, runDate.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(workspacePath);

        foreach (var folder in DailySubfolders)
        {
            Directory.CreateDirectory(Path.Combine(workspacePath, folder));
        }

        var metadata = new RunMetadata
        {
            ProjectId = project.ProjectId,
            RunDate = runDate.ToString("yyyy-MM-dd"),
            Branch = project.Branch,
            Status = "created"
        };
        await WriteMetadataAsync(workspacePath, metadata, cancellationToken);

        return new RunContext
        {
            Project = project,
            RunDate = runDate,
            WorkspacePath = workspacePath
        };
    }

    public async Task WriteMetadataAsync(string workspacePath, RunMetadata metadata, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(metadata, JsonDefaults.SerializerOptions);
        await File.WriteAllTextAsync(Path.Combine(workspacePath, "metadata.json"), json, cancellationToken);
    }

    public async Task<RunMetadata?> ReadMetadataAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(workspacePath, "metadata.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RunMetadata>(stream, JsonDefaults.SerializerOptions, cancellationToken);
    }

    public string? FindLatestWorkspace(string projectId)
    {
        var projectWorkspace = Path.Combine(rootPath, "workspaces", projectId);
        if (!Directory.Exists(projectWorkspace))
        {
            return null;
        }

        return Directory.EnumerateDirectories(projectWorkspace)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
