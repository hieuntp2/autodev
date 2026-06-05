using System.Text.Json;
using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class ProjectConfigLoader(string rootPath)
{
    public async Task<ProjectConfig> LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("Project id is required.", nameof(projectId));
        }

        var configPath = Path.Combine(rootPath, "projects", $"{projectId}.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Project config not found: {configPath}", configPath);
        }

        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync<ProjectConfig>(
            stream,
            JsonDefaults.SerializerOptions,
            cancellationToken);

        if (config is null)
        {
            throw new InvalidOperationException($"Project config is empty or invalid: {configPath}");
        }

        if (!string.Equals(config.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Project config id '{config.ProjectId}' does not match requested id '{projectId}'.");
        }

        return config;
    }
}
