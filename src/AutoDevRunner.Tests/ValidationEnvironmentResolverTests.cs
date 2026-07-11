using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ValidationEnvironmentResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ad-java-" + Guid.NewGuid().ToString("N"));

    public ValidationEnvironmentResolverTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Gradle_prefers_configured_java_home()
    {
        var configured = FakeJava("configured");
        var fallback = FakeJava("fallback");

        var env = ValidationEnvironmentResolver.Resolve(
            "gradlew.bat assembleDebug", configured, new[] { fallback }, windows: true);

        Assert.Equal(configured, env["JAVA_HOME"]);
        Assert.StartsWith(Path.Combine(configured, "bin") + Path.PathSeparator, env["PATH"]);
    }

    [Fact]
    public void Gradle_falls_back_to_first_installed_android_studio_jbr()
    {
        var missing = Path.Combine(_root, "missing");
        var jbr = FakeJava("Android Studio jbr");

        var env = ValidationEnvironmentResolver.Resolve(
            ".\\gradlew assembleDebug", null, new[] { missing, jbr }, windows: true);

        Assert.Equal(jbr, env["JAVA_HOME"]);
    }

    [Fact]
    public void Non_gradle_command_has_no_environment_override()
    {
        var env = ValidationEnvironmentResolver.Resolve(
            "dotnet test", FakeJava("configured"), Array.Empty<string>(), windows: true);

        Assert.Empty(env);
    }

    private string FakeJava(string name)
    {
        var home = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", "java.exe"), string.Empty);
        return home;
    }
}
