using System.Text.Json;
using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Skills;

/// <summary>
/// Discovers and manages AutoDev's global skills. On construction it locates the
/// global skill store, loads every <c>skill.json</c>, and applies enabled/disabled
/// overrides (config + a persisted runtime toggle). It also matches task text to
/// skills by trigger keyword and keeps a short in-memory log of the selections
/// AutoDev has made. Singleton.
/// </summary>
public class SkillRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly SkillsOptions _opt;
    private readonly ILogger<SkillRegistry> _log;
    private readonly object _gate = new();

    private List<SkillManifest> _skills = new();
    private HashSet<string> _runtimeDisabled = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, HashSet<string>> _projectDisabled = new();
    private Dictionary<LifecycleStage, HashSet<string>> _stageDisabled = new();
    private Dictionary<int, Dictionary<LifecycleStage, HashSet<string>>> _projectStageDisabled = new();
    private readonly LinkedList<SkillSelectionLog> _recent = new();
    private const int MaxRecent = 100;

    public string? Root { get; private set; }

    public SkillRegistry(IOptions<AutoDevOptions> opt, ILogger<SkillRegistry> log)
    {
        _opt = opt.Value.Skills;
        _log = log;
        LoadDisabledState();
        Reload();
    }

    /// <summary>All discovered skills (with effective enabled state applied).</summary>
    public IReadOnlyList<SkillManifest> All
    {
        get { lock (_gate) return _skills.ToList(); }
    }

    /// <summary>Only skills that are effectively enabled.</summary>
    public IReadOnlyList<SkillManifest> Enabled
    {
        get { lock (_gate) return _skills.Where(s => s.EffectiveEnabled).ToList(); }
    }

    /// <summary>
    /// Skills effectively enabled for one project: globally enabled AND not
    /// switched off for that project. Skills default to on for every project.
    /// With a <paramref name="stage"/>, skills switched off for that lifecycle
    /// stage — globally or for this specific project — are also excluded
    /// (default: on for every stage).
    /// </summary>
    public IReadOnlyList<SkillManifest> EnabledFor(int projectId, LifecycleStage? stage = null)
    {
        lock (_gate)
            return _skills.Where(s => s.EffectiveEnabled
                                      && !IsProjectDisabledNoLock(projectId, s.Id)
                                      && (stage is not { } st
                                          || (!IsStageDisabledNoLock(st, s.Id)
                                              && !IsProjectStageDisabledNoLock(projectId, st, s.Id))))
                .ToList();
    }

    /// <summary>Skill ids switched off for this project (independent of global state).</summary>
    public IReadOnlyCollection<string> DisabledForProject(int projectId)
    {
        lock (_gate)
            return _projectDisabled.TryGetValue(projectId, out var set)
                ? set.ToList()
                : Array.Empty<string>();
    }

    /// <summary>Skill ids switched off for one lifecycle stage (independent of global/project state).</summary>
    public IReadOnlyCollection<string> DisabledForStage(LifecycleStage stage)
    {
        lock (_gate)
            return _stageDisabled.TryGetValue(stage, out var set)
                ? set.ToList()
                : Array.Empty<string>();
    }

    /// <summary>
    /// Per-project lifecycle overrides: stage → skill ids switched off for that
    /// stage in THIS project only (empty map = project follows the global matrix).
    /// </summary>
    public IReadOnlyDictionary<LifecycleStage, IReadOnlyCollection<string>> DisabledStagesForProject(int projectId)
    {
        lock (_gate)
            return _projectStageDisabled.TryGetValue(projectId, out var map)
                ? map.ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<string>)kv.Value.ToList())
                : new Dictionary<LifecycleStage, IReadOnlyCollection<string>>();
    }

    /// <summary>True if the skill is switched off for this stage in this project.</summary>
    public bool IsProjectStageDisabled(int projectId, LifecycleStage stage, string skillId)
    {
        lock (_gate) return IsProjectStageDisabledNoLock(projectId, stage, skillId);
    }

    public SkillManifest? Get(string id)
    {
        lock (_gate)
            return _skills.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Re-scan the global store from disk.</summary>
    public void Reload()
    {
        if (!_opt.Enabled)
        {
            lock (_gate) { _skills = new(); Root = null; }
            _log.LogInformation("Skill system disabled (AutoDev:Skills:Enabled=false).");
            return;
        }

        var root = ResolveRoot();
        var loaded = new List<SkillManifest>();

        if (root is null || !Directory.Exists(root))
        {
            _log.LogWarning("Skill store not found (configured Root='{Root}'). No skills loaded.", _opt.Root);
        }
        else
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "skill.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var skill = JsonSerializer.Deserialize<SkillManifest>(File.ReadAllText(manifestPath), JsonOpts);
                    if (skill is null || string.IsNullOrWhiteSpace(skill.Id))
                    {
                        _log.LogWarning("Skipping malformed skill manifest: {Path}", manifestPath);
                        continue;
                    }
                    skill.SourcePath = dir;
                    loaded.Add(skill);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to load skill manifest: {Path}", manifestPath);
                }
            }
        }

        lock (_gate)
        {
            Root = root;
            _skills = loaded.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase).ToList();
            ApplyEffectiveEnabled();
        }

        _log.LogInformation("Loaded {Count} skill(s) from {Root}: {Ids}",
            loaded.Count, root, string.Join(", ", loaded.Select(s => s.Id)));
    }

    /// <summary>
    /// Rank enabled skills whose trigger keywords appear in <paramref name="text"/>.
    /// Case-insensitive substring match; skills with more distinct hits rank first.
    /// With a <paramref name="projectId"/>, skills switched off for that project
    /// are excluded; with a <paramref name="stage"/>, skills switched off for
    /// that lifecycle stage are excluded too.
    /// </summary>
    public IReadOnlyList<SkillMatch> Match(string? text, int? projectId = null, LifecycleStage? stage = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<SkillMatch>();
        var haystack = text.ToLowerInvariant();

        var pool = projectId is { } pid
            ? EnabledFor(pid, stage)
            : stage is { } st
                ? Enabled.Where(s => !IsStageDisabled(st, s.Id)).ToList()
                : Enabled;
        var matches = new List<SkillMatch>();
        foreach (var skill in pool)
        {
            var hits = skill.Triggers
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Where(t => haystack.Contains(t.ToLowerInvariant()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (hits.Count > 0)
                matches.Add(new SkillMatch(skill, hits));
        }
        return matches.OrderByDescending(m => m.Score).ThenBy(m => m.Skill.Id).ToList();
    }

    /// <summary>Toggle a skill on/off at runtime; persists across restarts.</summary>
    public bool SetEnabled(string id, bool enabled)
    {
        var skill = Get(id);
        if (skill is null) return false;

        lock (_gate)
        {
            if (enabled) _runtimeDisabled.Remove(id);
            else _runtimeDisabled.Add(id);
            ApplyEffectiveEnabled();
        }
        SaveDisabledState();
        _log.LogInformation("Skill '{Id}' {State}.", id, enabled ? "enabled" : "disabled");
        return true;
    }

    /// <summary>True if the skill is switched off for this lifecycle stage.</summary>
    public bool IsStageDisabled(LifecycleStage stage, string skillId)
    {
        lock (_gate) return IsStageDisabledNoLock(stage, skillId);
    }

    /// <summary>
    /// Toggle a skill for ONE lifecycle stage; persists across restarts. Stages
    /// default to all skills on; this only ever narrows what a stage may use.
    /// </summary>
    public bool SetEnabledForStage(LifecycleStage stage, string id, bool enabled)
    {
        var skill = Get(id);
        if (skill is null) return false;

        lock (_gate)
        {
            if (!_stageDisabled.TryGetValue(stage, out var set))
                _stageDisabled[stage] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (enabled) set.Remove(skill.Id);
            else set.Add(skill.Id);
            if (set.Count == 0) _stageDisabled.Remove(stage);
        }
        SaveDisabledState();
        _log.LogInformation("Skill '{Id}' {State} for lifecycle stage {Stage}.",
            skill.Id, enabled ? "enabled" : "disabled", stage);
        return true;
    }

    /// <summary>
    /// Toggle a skill for ONE lifecycle stage of ONE project; persists across
    /// restarts. Narrows on top of the global stage matrix — it cannot re-enable
    /// a skill that is off globally or off for the stage globally.
    /// </summary>
    public bool SetEnabledForProjectStage(int projectId, LifecycleStage stage, string id, bool enabled)
    {
        var skill = Get(id);
        if (skill is null) return false;

        lock (_gate)
        {
            if (!_projectStageDisabled.TryGetValue(projectId, out var map))
                _projectStageDisabled[projectId] = map = new Dictionary<LifecycleStage, HashSet<string>>();
            if (!map.TryGetValue(stage, out var set))
                map[stage] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (enabled) set.Remove(skill.Id);
            else set.Add(skill.Id);
            if (set.Count == 0) map.Remove(stage);
            if (map.Count == 0) _projectStageDisabled.Remove(projectId);
        }
        SaveDisabledState();
        _log.LogInformation("Skill '{Id}' {State} for stage {Stage} of project {ProjectId}.",
            skill.Id, enabled ? "enabled" : "disabled", stage, projectId);
        return true;
    }

    /// <summary>
    /// Toggle a skill for ONE project only; persists across restarts. Does not
    /// touch the global state — a globally disabled skill stays off everywhere.
    /// </summary>
    public bool SetEnabledForProject(int projectId, string id, bool enabled)
    {
        var skill = Get(id);
        if (skill is null) return false;

        lock (_gate)
        {
            if (!_projectDisabled.TryGetValue(projectId, out var set))
                _projectDisabled[projectId] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (enabled) set.Remove(skill.Id);
            else set.Add(skill.Id);
            if (set.Count == 0) _projectDisabled.Remove(projectId);
        }
        SaveDisabledState();
        _log.LogInformation("Skill '{Id}' {State} for project {ProjectId}.",
            skill.Id, enabled ? "enabled" : "disabled", projectId);
        return true;
    }

    public void RecordSelection(SkillSelectionLog entry)
    {
        lock (_gate)
        {
            _recent.AddFirst(entry);
            while (_recent.Count > MaxRecent) _recent.RemoveLast();
        }
    }

    public IReadOnlyList<SkillSelectionLog> RecentSelections
    {
        get { lock (_gate) return _recent.ToList(); }
    }

    // ---- internals ----

    private void ApplyEffectiveEnabled()
    {
        foreach (var s in _skills)
        {
            var configDisabled = _opt.Disabled.Any(d => string.Equals(d, s.Id, StringComparison.OrdinalIgnoreCase));
            var runtimeDisabled = _runtimeDisabled.Contains(s.Id);
            s.EffectiveEnabled = s.Enabled && !configDisabled && !runtimeDisabled;
        }
    }

    /// <summary>
    /// Resolve the global skill store. Order: configured Root (absolute, or
    /// relative to base/current dir), then an "AutoDevSkills" folder found by
    /// walking up from the app base dir and the current working dir.
    /// </summary>
    private string? ResolveRoot()
    {
        if (!string.IsNullOrWhiteSpace(_opt.Root))
        {
            if (Path.IsPathRooted(_opt.Root) && Directory.Exists(_opt.Root))
                return Path.GetFullPath(_opt.Root);
            foreach (var base_ in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var candidate = Path.GetFullPath(Path.Combine(base_, _opt.Root));
                if (Directory.Exists(candidate)) return candidate;
            }
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var found = FindUpwards(start, "AutoDevSkills");
            if (found is not null) return found;
        }
        return null;
    }

    private static string? FindUpwards(string start, string folderName)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, folderName);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private bool IsProjectDisabledNoLock(int projectId, string skillId) =>
        _projectDisabled.TryGetValue(projectId, out var set) && set.Contains(skillId);

    private bool IsStageDisabledNoLock(LifecycleStage stage, string skillId) =>
        _stageDisabled.TryGetValue(stage, out var set) && set.Contains(skillId);

    private bool IsProjectStageDisabledNoLock(int projectId, LifecycleStage stage, string skillId) =>
        _projectStageDisabled.TryGetValue(projectId, out var map)
        && map.TryGetValue(stage, out var set) && set.Contains(skillId);

    private string StatePath =>
        string.IsNullOrWhiteSpace(_opt.StateFile)
            ? Path.Combine(AppContext.BaseDirectory, "skills-state.json")
            : Path.GetFullPath(_opt.StateFile);

    private void LoadDisabledState()
    {
        _runtimeDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _projectDisabled = new Dictionary<int, HashSet<string>>();
        _stageDisabled = new Dictionary<LifecycleStage, HashSet<string>>();
        _projectStageDisabled = new Dictionary<int, Dictionary<LifecycleStage, HashSet<string>>>();
        try
        {
            if (!File.Exists(StatePath)) return;
            var state = JsonSerializer.Deserialize<DisabledState>(File.ReadAllText(StatePath), JsonOpts);
            if (state?.Disabled is { } list)
                _runtimeDisabled = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            if (state?.ProjectDisabled is { } perProject)
                _projectDisabled = perProject.ToDictionary(
                    kv => kv.Key,
                    kv => new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase));
            if (state?.StageDisabled is { } perStage)
                foreach (var (name, ids) in perStage)
                    if (Enum.TryParse<LifecycleStage>(name, ignoreCase: true, out var stage))
                        _stageDisabled[stage] = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            if (state?.ProjectStageDisabled is { } perProjectStage)
                foreach (var (projectId, stages) in perProjectStage)
                {
                    var map = new Dictionary<LifecycleStage, HashSet<string>>();
                    foreach (var (name, ids) in stages)
                        if (Enum.TryParse<LifecycleStage>(name, ignoreCase: true, out var stage))
                            map[stage] = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                    if (map.Count > 0) _projectStageDisabled[projectId] = map;
                }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read skill state; starting with none disabled.");
        }
    }

    private void SaveDisabledState()
    {
        try
        {
            DisabledState snapshot;
            lock (_gate)
                snapshot = new DisabledState
                {
                    Disabled = _runtimeDisabled.ToList(),
                    ProjectDisabled = _projectDisabled.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()),
                    StageDisabled = _stageDisabled.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.ToList()),
                    ProjectStageDisabled = _projectStageDisabled.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value.ToDictionary(s => s.Key.ToString(), s => s.Value.ToList()))
                };
            File.WriteAllText(StatePath,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not persist skill state to {Path}.", StatePath);
        }
    }

    private class DisabledState
    {
        public List<string> Disabled { get; set; } = new();

        /// <summary>Per-project switched-off skill ids, keyed by project id.</summary>
        public Dictionary<int, List<string>> ProjectDisabled { get; set; } = new();

        /// <summary>Per-lifecycle-stage switched-off skill ids, keyed by stage name.</summary>
        public Dictionary<string, List<string>> StageDisabled { get; set; } = new();

        /// <summary>Per-project lifecycle overrides: project id → stage name → switched-off skill ids.</summary>
        public Dictionary<int, Dictionary<string, List<string>>> ProjectStageDisabled { get; set; } = new();
    }
}
