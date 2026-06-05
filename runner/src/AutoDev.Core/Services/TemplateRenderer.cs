using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class TemplateRenderer
{
    public string RenderPlannerTemplate(ProjectConfig project, string template, string projectContext, string? selectedTask = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectId"] = project.ProjectId,
            ["displayName"] = project.DisplayName,
            ["projectType"] = project.ProjectType,
            ["dailyGoal"] = project.DailyGoal,
            ["protectedPaths"] = RenderList(project.ProtectedPaths),
            ["allowedWritePaths"] = RenderList(project.AllowedWritePaths),
            ["buildCommands"] = RenderList(project.BuildCommands),
            ["testCommands"] = RenderList(project.TestCommands),
            ["projectContext"] = projectContext,
            ["selectedTask"] = selectedTask ?? string.Empty
        };

        return Render(template, values);
    }

    public string RenderCodexTaskTemplate(string template, string selectedTask, string runContract)
    {
        return Render(template, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["selectedTask"] = selectedTask,
            ["runContract"] = runContract
        });
    }

    public string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = template;
        foreach (var (key, value) in values)
        {
            rendered = rendered.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string RenderList(IEnumerable<string> values)
    {
        var list = values.ToArray();
        return list.Length == 0
            ? "- none"
            : string.Join(Environment.NewLine, list.Select(value => $"- {value}"));
    }
}
