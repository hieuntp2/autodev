using System.Text.Json;

namespace AutoDevOrchestrator;

/// <summary>
/// Configuration, resolved in order of precedence:
/// process environment variables > repo-root .env file > tools/autodev-orchestrator/appsettings.json > defaults.
/// </summary>
public sealed class OrchestratorConfig
{
    public string? OpenAiApiKey { get; private set; }
    public string OpenAiBaseUrl { get; private set; } = "https://api.openai.com/v1";
    public string DailyModel { get; private set; } = "gpt-5.4-nano";
    public string WeeklyModel { get; private set; } = "gpt-5.4-mini";
    public int MaxOutputTokens { get; private set; } = 2500;
    public bool EnableWeeklyReview { get; private set; }
    public bool DryRun { get; private set; }
    public bool RunImplementer { get; private set; }
    public string ImplementerCommand { get; private set; } = "";
    public string GardenAppRelPath { get; private set; } = "apps/daily-pixel-garden";

    private static readonly string[] KnownKeys =
    [
        "OPENAI_API_KEY", "OPENAI_BASE_URL", "OPENAI_DAILY_MODEL", "OPENAI_WEEKLY_MODEL",
        "OPENAI_MAX_OUTPUT_TOKENS", "OPENAI_ENABLE_WEEKLY_REVIEW",
        "AUTODEV_DRY_RUN", "AUTODEV_RUN_IMPLEMENTER", "AUTODEV_IMPLEMENTER_COMMAND",
        "AUTODEV_GARDEN_APP_PATH"
    ];

    public static OrchestratorConfig Load(RepoPaths paths)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var appsettings = Path.Combine(paths.ToolDir, "appsettings.json");
        if (File.Exists(appsettings))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(appsettings));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                        or JsonValueKind.True or JsonValueKind.False)
                    {
                        values[prop.Name] = prop.Value.ToString();
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new OrchestratorException($"Invalid JSON in {appsettings}: {ex.Message}");
            }
        }

        foreach (var (key, value) in ParseEnvFile(Path.Combine(paths.Root, ".env")))
            values[key] = value;

        foreach (var key in KnownKeys)
        {
            var env = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env)) values[key] = env;
        }

        var config = new OrchestratorConfig();
        if (values.TryGetValue("OPENAI_API_KEY", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
            config.OpenAiApiKey = apiKey.Trim();
        if (values.TryGetValue("OPENAI_BASE_URL", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
            config.OpenAiBaseUrl = baseUrl.TrimEnd('/');
        if (values.TryGetValue("OPENAI_DAILY_MODEL", out var daily) && !string.IsNullOrWhiteSpace(daily))
            config.DailyModel = daily.Trim();
        if (values.TryGetValue("OPENAI_WEEKLY_MODEL", out var weekly) && !string.IsNullOrWhiteSpace(weekly))
            config.WeeklyModel = weekly.Trim();
        if (values.TryGetValue("OPENAI_MAX_OUTPUT_TOKENS", out var maxTokens) && int.TryParse(maxTokens, out var parsed) && parsed > 0)
            config.MaxOutputTokens = parsed;
        config.EnableWeeklyReview = GetBool(values, "OPENAI_ENABLE_WEEKLY_REVIEW");
        config.DryRun = GetBool(values, "AUTODEV_DRY_RUN");
        config.RunImplementer = GetBool(values, "AUTODEV_RUN_IMPLEMENTER");
        if (values.TryGetValue("AUTODEV_IMPLEMENTER_COMMAND", out var cmd))
            config.ImplementerCommand = cmd.Trim();
        if (values.TryGetValue("AUTODEV_GARDEN_APP_PATH", out var appPath) && !string.IsNullOrWhiteSpace(appPath))
            config.GardenAppRelPath = appPath.Trim().Replace('\\', '/').Trim('/');

        return config;
    }

    public string GardenAppFullPath(RepoPaths paths) =>
        Path.Combine(paths.Root, GardenAppRelPath.Replace('/', Path.DirectorySeparatorChar));

    private static bool GetBool(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var raw) &&
        (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1" ||
         raw.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Key, string Value)> ParseEnvFile(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var idx = trimmed.IndexOf('=');
            if (idx < 1) continue;
            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim().Trim('"', '\'');
            yield return (key, value);
        }
    }
}
