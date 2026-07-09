namespace AutoDevRunner.Models;

public enum PromptDirectiveAuthor
{
    User,
    Ai,
    Seed
}

/// <summary>
/// One immutable version of per-project prompt directives. The latest version is
/// the source of truth; .ai-runner/PROMPT.md is kept as a repo-local mirror.
/// </summary>
public class PromptDirective
{
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int Version { get; set; }

    public string Content { get; set; } = string.Empty;

    public PromptDirectiveAuthor Author { get; set; } = PromptDirectiveAuthor.User;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
