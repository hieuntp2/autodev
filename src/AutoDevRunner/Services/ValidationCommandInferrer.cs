using System.Text.Json;

namespace AutoDevRunner.Services;

public static class ValidationCommandInferrer
{
    public static string? Infer(string repoPath)
    {
        try
        {
            if (Directory.EnumerateFiles(repoPath, "*.sln", SearchOption.TopDirectoryOnly).Any()
                || Directory.EnumerateFiles(repoPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            {
                return "dotnet build";
            }

            var packageJson = Path.Combine(repoPath, "package.json");
            if (File.Exists(packageJson))
            {
                var npm = InferNpm(packageJson);
                if (npm is not null) return npm;
            }

            var gradlewBat = Path.Combine(repoPath, "gradlew.bat");
            var gradlew = Path.Combine(repoPath, "gradlew");
            if (OperatingSystem.IsWindows() && File.Exists(gradlewBat))
                return "gradlew.bat assembleDebug";
            if (File.Exists(gradlew))
                return "./gradlew assembleDebug";
            if (File.Exists(gradlewBat))
                return "gradlew.bat assembleDebug";

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? InferNpm(string packageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
            if (!doc.RootElement.TryGetProperty("scripts", out var scripts)
                || scripts.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            if (HasScript(scripts, "build")) return "npm run build";
            if (HasScript(scripts, "test")) return "npm test";
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasScript(JsonElement scripts, string name) =>
        scripts.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());
}
