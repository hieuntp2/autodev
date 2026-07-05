using System.Text.Json.Serialization;

namespace AutoDevRunner.Skills;

/// <summary>
/// A global AutoDev skill loaded from <c>AutoDevSkills/&lt;id&gt;/skill.json</c>.
/// Skills live once in the global store and are shared across every project the
/// runner develops — they are never duplicated into individual project repos.
/// The JSON fields map by name (case-insensitive); the last few properties are
/// populated by the registry after load, not from the manifest file.
/// </summary>
public class SkillManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Master on/off from the manifest. May be overridden at runtime.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Explicit one-line invocation hint, e.g. "Use $pixel-animation-artist to …".</summary>
    public string InvocationHint { get; set; } = string.Empty;

    /// <summary>Keywords that select this skill when found in a task/brief/plan.</summary>
    public List<string> Triggers { get; set; } = new();

    /// <summary>Relative script paths (e.g. generate/validate entrypoints).</summary>
    public SkillEntrypoints Entrypoints { get; set; } = new();

    /// <summary>Human-readable notes on where the skill's output should go.</summary>
    public List<string> OutputConvention { get; set; } = new();

    /// <summary>Relative path to the skill's markdown instructions (SKILL.md).</summary>
    public string? Docs { get; set; }

    /// <summary>Relative path to the output JSON schema, if any.</summary>
    public string? Schema { get; set; }

    // ---- Populated by the registry, not from the manifest file ----

    /// <summary>Absolute path to the skill's directory in the global store.</summary>
    [JsonIgnore]
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Effective enabled state (manifest AND registry/config overrides).</summary>
    [JsonIgnore]
    public bool EffectiveEnabled { get; set; } = true;

    /// <summary>Resolve an entrypoint (or any relative skill path) to an absolute path.</summary>
    public string? ResolvePath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || string.IsNullOrWhiteSpace(SourcePath))
            return null;
        return Path.GetFullPath(Path.Combine(SourcePath, relative));
    }

    public string? GeneratePath => ResolvePath(Entrypoints.Generate);
    public string? ValidatePath => ResolvePath(Entrypoints.Validate);
    public string? DocsPath => ResolvePath(Docs);
}

public class SkillEntrypoints
{
    public string? Generate { get; set; }
    public string? Validate { get; set; }
}
