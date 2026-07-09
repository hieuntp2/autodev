using System.Text.RegularExpressions;
using AutoDevRunner.Models;

namespace AutoDevRunner.Services;

public enum TaskTier
{
    Light,
    Standard,
    Deep
}

public static class TaskTierClassifier
{
    private static readonly Regex LightIntent =
        new(@"\b(docs?|documentation|tidy|cleanup|comment|comments|rename[- ]?only|format)\b",
            RegexOptions.IgnoreCase);

    private static readonly Regex DeepIntent =
        new(@"\b(multi[- ]?file|architecture|architectural|rearchitect|large refactor|major refactor|cross[- ]cutting)\b",
            RegexOptions.IgnoreCase);

    public static TaskTier Classify(TaskProposal? proposal, RiskAssessment risk,
        RunLessons lessons, string? taskText)
    {
        var text = string.Join('\n', new[] { taskText, proposal?.Title, proposal?.Reason }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        if (risk.Level is RiskLevel.Risky)
            return TaskTier.Deep;

        if (lessons.IsRepeatedlyFailing(proposal?.Title ?? taskText))
            return TaskTier.Deep;

        if (ValidationFailedLastRunForTask(lessons, proposal?.Title ?? taskText))
            return TaskTier.Deep;

        if (DeepIntent.IsMatch(text))
            return TaskTier.Deep;

        if (risk.Level is RiskLevel.Safe
            && (string.Equals(proposal?.Source, "maintenance", StringComparison.OrdinalIgnoreCase)
                || LightIntent.IsMatch(text)))
        {
            return TaskTier.Light;
        }

        return TaskTier.Standard;
    }

    private static bool ValidationFailedLastRunForTask(RunLessons lessons, string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || lessons.Recent.Count == 0) return false;
        var norm = RunLessons.Norm(title);
        var latest = lessons.Recent.OrderByDescending(r => r.StartedAt).FirstOrDefault();
        return latest is { ValidationRun: true, ValidationPassed: false, Task: not null }
               && RunLessons.Norm(latest.Task!) == norm;
    }
}
