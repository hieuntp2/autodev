namespace AutoDevRunner.Services;

/// <summary>Builds per-validation process overrides without mutating machine environment variables.</summary>
public static class ValidationEnvironmentResolver
{
    public static IReadOnlyDictionary<string, string?> Resolve(
        string command,
        string? configuredJavaHome = null,
        IEnumerable<string>? javaCandidates = null,
        bool? windows = null)
    {
        if (!IsGradle(command) || !(windows ?? OperatingSystem.IsWindows()))
            return new Dictionary<string, string?>();

        javaCandidates ??= DefaultCandidates();
        var javaHome = new[] { configuredJavaHome }
            .Concat(javaCandidates.Cast<string?>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .FirstOrDefault(HasJavaExecutable);
        if (javaHome is null) return new Dictionary<string, string?>();

        var bin = Path.Combine(javaHome, "bin");
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["JAVA_HOME"] = javaHome,
            ["PATH"] = bin + Path.PathSeparator + currentPath
        };
    }

    private static bool IsGradle(string command) =>
        command.Contains("gradlew", StringComparison.OrdinalIgnoreCase)
        || command.StartsWith("gradle ", StringComparison.OrdinalIgnoreCase)
        || command.Equals("gradle", StringComparison.OrdinalIgnoreCase);

    private static bool HasJavaExecutable(string home) =>
        File.Exists(Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"))
        || File.Exists(Path.Combine(home, "bin", "java.exe"));

    private static IEnumerable<string> DefaultCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles)) yield break;
        yield return Path.Combine(programFiles, "Android", "Android Studio", "jbr");
        yield return Path.Combine(programFiles, "Android", "Android Studio Preview", "jbr");
    }
}
