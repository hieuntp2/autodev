using AutoDev.Core.Models;

namespace AutoDev.Core.Services;

public sealed class RequirementProposalWriter
{
    public async Task<string?> WriteAsync(
        ProjectConfig project,
        string problem,
        IReadOnlyList<string> affectedDocs,
        string suggestedChange,
        string reason,
        string risk,
        bool requiresUserApproval,
        CancellationToken cancellationToken = default)
    {
        if (!project.AllowRequirementProposal)
        {
            return null;
        }

        var policy = new PathPolicy(project.AllowedWritePaths, project.ProtectedPaths);
        var proposalDir = "docs/active/requirement-proposals";

        if (!policy.IsWriteAllowed(proposalDir + "/"))
        {
            Console.WriteLine($"[RequirementProposalWriter] {proposalDir} is not within allowedWritePaths — proposal skipped.");
            return null;
        }

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmm");
        var relativePath = $"{proposalDir}/proposal-{timestamp}.md";
        var fullPath = Path.Combine(project.RepoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var content = BuildProposalContent(problem, affectedDocs, suggestedChange, reason, risk, requiresUserApproval);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return relativePath;
    }

    private static string BuildProposalContent(
        string problem,
        IReadOnlyList<string> affectedDocs,
        string suggestedChange,
        string reason,
        string risk,
        bool requiresUserApproval)
    {
        return $"""
        # Requirement Proposal

        ## Problem

        {problem}

        ## Affected Documents

        {string.Join("\n", affectedDocs.Select(d => $"- {d}"))}

        ## Suggested Change

        {suggestedChange}

        ## Reason

        {reason}

        ## Risk

        {risk}

        ## Requires User Approval

        {(requiresUserApproval ? "Yes — do not apply without review." : "No — low-risk proposal, can be applied after review.")}
        """;
    }
}
