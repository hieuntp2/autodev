using System.Text.RegularExpressions;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public record RiskAssessment(RiskLevel Level, List<string> Reasons);

/// <summary>
/// Classifies a run's blast radius as safe / normal / risky by inspecting the
/// changed files and the task text. V1 only detects and surfaces risk (logged,
/// recorded in the run report, shown on the dashboard) — there is no blocking
/// approval gate. Pure and dependency-free so it is easy to unit test.
/// </summary>
public class RiskAssessor
{
    // Path patterns that make a change risky.
    private static readonly (Regex Rx, string Why)[] RiskyPaths =
    {
        (new(@"(^|/)appsettings.*\.json$", RegexOptions.IgnoreCase), "edits app configuration (appsettings)"),
        (new(@"(^|/)\.env($|\.)", RegexOptions.IgnoreCase), "edits environment/secret file (.env)"),
        (new(@"(^|/)(migrations?|__migrations__)(/|$)", RegexOptions.IgnoreCase), "touches database migrations"),
        (new(@"\.sql$", RegexOptions.IgnoreCase), "changes SQL / schema"),
        (new(@"(^|/)dockerfile$", RegexOptions.IgnoreCase), "changes Docker image build"),
        (new(@"(^|/)docker-compose.*\.ya?ml$", RegexOptions.IgnoreCase), "changes container orchestration"),
        (new(@"(^|/)\.github/workflows/", RegexOptions.IgnoreCase), "changes CI/CD workflow"),
        (new(@"(^|/)(deploy|publish|release)[^/]*\.(ps1|sh|ya?ml|cmd|bat)$", RegexOptions.IgnoreCase), "changes a deploy/release script"),
        (new(@"(^|/)(web|nginx)\.config$", RegexOptions.IgnoreCase), "changes server config"),
        (new(@"(secrets?|credentials?)", RegexOptions.IgnoreCase), "touches secrets/credentials"),
        (new(@"\.(pem|key|pfx|p12|keystore)$", RegexOptions.IgnoreCase), "touches keys/certificates"),
    };

    // Task-text signals that make a run risky regardless of files.
    private static readonly (Regex Rx, string Why)[] RiskyIntent =
    {
        (new(@"\bdrop\s+table\b|\btruncate\b", RegexOptions.IgnoreCase), "destructive SQL in the task"),
        (new(@"\bmigrat", RegexOptions.IgnoreCase), "database migration in the task"),
        (new(@"\bdeploy|\bproduction\b|\bprod\b", RegexOptions.IgnoreCase), "deploy/production in the task"),
        (new(@"\bforce[- ]?push\b|reset\s+--hard|\brm\s+-rf\b", RegexOptions.IgnoreCase), "destructive git/shell in the task"),
        (new(@"\bsecret|\bcredential|\bapi[_ ]?key\b", RegexOptions.IgnoreCase), "secrets/credentials in the task"),
        (new(@"\bdelete\b|\bremove\b.*\bfile|\bx(o|ó)a\b", RegexOptions.IgnoreCase), "file deletion in the task"),
        (new(@"\b(large|big|major|full|whole|entire|toàn bộ)[- ]?(scale )?(refactor|rewrite|rearchitect|migration)\b|\brefactor (the )?(entire|whole|codebase)\b|\brewrite\b.*\b(app|system|codebase)\b",
            RegexOptions.IgnoreCase), "large refactor/rewrite in the task"),
    };

    // A run that deletes many files is risky even if intent text is benign.
    private const int MassDeletionThreshold = 5;

    // File extensions considered "safe" (assets/docs/tests only).
    private static readonly Regex SafeAsset =
        new(@"\.(png|jpe?g|gif|webp|svg|md|txt|json)$|(^|/)tests?/|(_|\.)tests?\.", RegexOptions.IgnoreCase);

    public RiskAssessment Assess(IReadOnlyList<GitChange> changes, string? taskText)
    {
        var reasons = new List<string>();

        if (changes.Any(c => c.IsDelete))
            reasons.Add($"deletes {changes.Count(c => c.IsDelete)} file(s)");

        foreach (var change in changes)
            foreach (var (rx, why) in RiskyPaths)
                if (rx.IsMatch(NormalizeSlashes(change.Path)))
                {
                    reasons.Add($"{why} ({change.Path})");
                    break;
                }

        if (!string.IsNullOrWhiteSpace(taskText))
            foreach (var (rx, why) in RiskyIntent)
                if (rx.IsMatch(taskText))
                    reasons.Add(why);

        if (reasons.Count > 0)
            return new RiskAssessment(RiskLevel.Risky, Dedupe(reasons));

        // No risky signals: safe only if every change is an asset/doc/test.
        var paths = changes.Select(c => NormalizeSlashes(c.Path)).ToList();
        if (paths.Count > 0 && paths.All(p => SafeAsset.IsMatch(p)))
            return new RiskAssessment(RiskLevel.Safe, new() { "only assets/docs/tests changed" });

        return new RiskAssessment(RiskLevel.Normal, new());
    }

    private static string NormalizeSlashes(string p) => p.Replace('\\', '/');

    private static List<string> Dedupe(List<string> items) =>
        items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
