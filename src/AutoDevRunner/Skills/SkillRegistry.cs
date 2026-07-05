using System.Text.Json;
using AutoDevRunner.Config;
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
    private readonly LinkedList<SkillSelectionLog> _recent = new();
    private const int MaxRecent = 100;

    public string? Root { get; private set; }

    public SkillRegistry(IOptions<AutoDevOptions> opt, ILogger<SkillRegistry> log)
    {
        _opt = opt.Value.Skills;
        _log = log;
        _runtimeDisabled = LoadDisabledState();
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
    /// </summary>
    public IReadOnlyList<SkillMatch> Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<SkillMatch>();
        var haystack = text.ToLowerInvariant();

        var matches = new List<SkillMatch>();
        foreach (var skill in Enabled)
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

    private string StatePath => Path.Combine(AppContext.BaseDirectory, "skills-state.json");

    private HashSet<string> LoadDisabledState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var state = JsonSerializer.Deserialize<DisabledState>(File.ReadAllText(StatePath), JsonOpts);
                if (state?.Disabled is { } list)
                    return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read skill state; starting with none disabled.");
        }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveDisabledState()
    {
        try
        {
            List<string> snapshot;
            lock (_gate) snapshot = _runtimeDisabled.ToList();
            File.WriteAllText(StatePath,
                JsonSerializer.Serialize(new DisabledState { Disabled = snapshot },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not persist skill state to {Path}.", StatePath);
        }
    }

    private class DisabledState
    {
        public List<string> Disabled { get; set; } = new();
    }
}
