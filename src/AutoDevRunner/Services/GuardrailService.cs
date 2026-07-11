using System.Text.RegularExpressions;

namespace AutoDevRunner.Services;

public record GuardrailResult(bool Ok, List<string> Violations)
{
    public static GuardrailResult Pass() => new(true, new());
}

/// <summary>
/// Post-run safety checks. The AI does its own editing, so guardrails are
/// enforced here by inspecting what changed before we commit.
/// </summary>
public class GuardrailService
{
    // File patterns the AI must never modify.
    private static readonly Regex[] ProtectedPatterns =
    {
        new(@"(^|/|\\)\.env($|\.)", RegexOptions.IgnoreCase),
        new(@"(^|/|\\)secrets?($|[./\\])", RegexOptions.IgnoreCase),
        new(@"(^|/|\\)credentials?($|[./\\])", RegexOptions.IgnoreCase),
        new(@"\.(pem|key|pfx|p12|keystore)$", RegexOptions.IgnoreCase),
        new(@"(^|/|\\)id_rsa", RegexOptions.IgnoreCase),
        new(@"(^|/|\\)\.git(/|\\)", RegexOptions.IgnoreCase),
    };

    /// <summary>Returns the guardrail instructions injected into every AI prompt.</summary>
    public static string PromptGuardrails(bool allowMain, bool autoPush,
        string? repoPath = null, bool blockDeletions = true, bool blockOutOfProject = true)
    {
        var scope = string.IsNullOrWhiteSpace(repoPath) ? "the project repository" : $"`{repoPath}`";
        var deletionRule = blockDeletions
            ? "- Do NOT delete files. If something looks obsolete, leave it (or empty its body) rather than removing it — the runner blocks and reverts any run that deletes files.\n"
            : "- Deletions inside the project are permitted when they serve this task; keep them focused, intentional, and easy to justify in the summary.\n";
        var scopeRule = blockOutOfProject
            ? $"- Stay INSIDE the project. Only create/modify files under {scope}. Do NOT touch, create, or delete anything outside it (no absolute paths, no `..` escaping the repo, no edits to your home dir, system files, or other projects) — the runner blocks any run that writes outside the project.\n"
            : string.Empty;

        return
$@"## Hard safety rules (must follow)
- Do NOT modify secret files: .env, credentials, *.pem, *.key, private keys, keystores.
- Do NOT run destructive commands: no `rm -rf` of the project, no disk formatting, no `git reset --hard`, no force push.
{deletionRule}{scopeRule}- {(allowMain ? "Stay on the current branch." : "Do NOT switch to main/master; work only on the AI branch already checked out for you.")}
- Do NOT commit or push. The runner handles commit and push after guardrails and validation.{(autoPush ? " Auto-push is enabled for the runner." : string.Empty)}
- Make focused, incremental changes. Leave the repo in a buildable state.";
    }

    /// <summary>Check the list of changed files against protected patterns.</summary>
    public GuardrailResult Check(IEnumerable<string> changedFiles)
    {
        var violations = new List<string>();
        foreach (var file in changedFiles)
            foreach (var pattern in ProtectedPatterns)
                if (pattern.IsMatch(file))
                {
                    violations.Add(file);
                    break;
                }

        return violations.Count == 0
            ? GuardrailResult.Pass()
            : new GuardrailResult(false, violations);
    }
}
