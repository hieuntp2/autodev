namespace AutoDev.Core.Services;

public sealed class PathPolicy
{
    private readonly string[] _allowedWritePaths;
    private readonly string[] _protectedPaths;

    public PathPolicy(IEnumerable<string> allowedWritePaths, IEnumerable<string> protectedPaths)
    {
        _allowedWritePaths = allowedWritePaths.Select(Normalize).ToArray();
        _protectedPaths = protectedPaths.Select(Normalize).ToArray();
    }

    public bool IsWriteAllowed(string path)
    {
        var normalized = Normalize(path);
        if (IsProtected(normalized))
        {
            return false;
        }

        if (_allowedWritePaths.Length == 0)
        {
            return false;
        }

        return _allowedWritePaths.Any(allowed => IsInsidePath(normalized, allowed));
    }

    public bool IsProtected(string path)
    {
        var normalized = Normalize(path);
        return _protectedPaths.Any(p => MatchesPath(normalized, p));
    }

    public bool IsInAllowedPath(string path)
    {
        var normalized = Normalize(path);
        return _allowedWritePaths.Any(allowed => IsInsidePath(normalized, allowed));
    }

    public bool HasPathOverlap(out IReadOnlyList<string> overlaps)
    {
        var found = new List<string>();
        foreach (var allowed in _allowedWritePaths)
        {
            foreach (var prot in _protectedPaths)
            {
                if (IsInsidePath(prot, allowed) || IsInsidePath(allowed, prot))
                {
                    found.Add($"'{allowed}' overlaps with protected '{prot}'");
                }
            }
        }

        overlaps = found;
        return found.Count > 0;
    }

    private static bool IsInsidePath(string file, string directory)
    {
        var dir = directory.EndsWith('/') ? directory : directory + "/";
        return file.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
            || string.Equals(file, directory.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPath(string file, string pattern)
    {
        if (pattern.EndsWith('/'))
        {
            return file.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(file, pattern, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path) =>
        path.Trim().Replace('\\', '/').TrimStart('/');
}
