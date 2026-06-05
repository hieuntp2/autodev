using System.Text;
using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class RunContextBuilder
{
    public string Build(ProjectConfig project, TargetRepoContext docs)
    {
        var sb = new StringBuilder();

        AppendSection(sb, "Agent Rules", docs.AgentRules, project.AgentRulesFile);
        AppendSection(sb, "Main Requirements", docs.MainRequirement, project.MainRequirementFile);
        AppendSection(sb, "Scope", docs.Scope, project.ScopeFile);
        AppendSection(sb, "Roadmap", docs.Roadmap, project.RoadmapFile);
        AppendSection(sb, "Backlog", docs.Backlog, project.BacklogFile);
        AppendSection(sb, "Implementation Status", docs.Status, project.StatusFile);

        if (docs.MissingDocPaths.Count > 0)
        {
            sb.AppendLine("## Missing Documents");
            foreach (var path in docs.MissingDocPaths)
            {
                sb.AppendLine($"- {path}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string heading, string content, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        sb.AppendLine($"## {heading}");
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            sb.AppendLine($"<!-- source: {sourcePath} -->");
        }

        sb.AppendLine(content.TrimEnd());
        sb.AppendLine();
    }
}
