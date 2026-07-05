namespace AutoDevRunner.Skills;

/// <summary>
/// A skill matched against some task text, with the keywords that triggered it.
/// Higher <see cref="Score"/> (number of distinct trigger hits) ranks first.
/// </summary>
public record SkillMatch(SkillManifest Skill, IReadOnlyList<string> MatchedKeywords)
{
    public int Score => MatchedKeywords.Count;
}

/// <summary>
/// A recorded decision: AutoDev chose skill X for a given project/task. Kept in
/// an in-memory ring buffer by the registry and surfaced on the dashboard so
/// there is a visible log of which skill was chosen for which task.
/// </summary>
public record SkillSelectionLog(
    string SkillId,
    string SkillName,
    IReadOnlyList<string> MatchedKeywords,
    string ProjectName,
    string? Task,
    DateTime SelectedAt);
