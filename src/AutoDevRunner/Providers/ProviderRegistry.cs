using AutoDevRunner.Models;

namespace AutoDevRunner.Providers;

/// <summary>Resolves providers by kind and by a project's priority order.</summary>
public class ProviderRegistry
{
    private readonly Dictionary<ProviderKind, IAiProvider> _providers;

    public ProviderRegistry(IEnumerable<IAiProvider> providers)
        => _providers = providers.ToDictionary(p => p.Kind);

    public IAiProvider? Get(ProviderKind kind)
        => _providers.TryGetValue(kind, out var p) ? p : null;

    public IReadOnlyCollection<IAiProvider> All => _providers.Values;

    /// <summary>Parse "Codex,Claude" into the enabled providers in that order.</summary>
    public IEnumerable<IAiProvider> ResolveOrder(string priorityCsv)
    {
        foreach (var token in priorityCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<ProviderKind>(token, ignoreCase: true, out var kind)
                && _providers.TryGetValue(kind, out var p) && p.IsEnabled)
            {
                yield return p;
            }
        }
    }
}
