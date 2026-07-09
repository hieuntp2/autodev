using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class ValidationCommandInferrerTests : IDisposable
{
    private readonly string _repo;

    public ValidationCommandInferrerTests()
    {
        _repo = Path.Combine(Path.GetTempPath(), "adval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    [Fact]
    public void Infers_dotnet_build_from_solution()
    {
        File.WriteAllText(Path.Combine(_repo, "App.sln"), string.Empty);

        var command = ValidationCommandInferrer.Infer(_repo);

        Assert.Equal("dotnet build", command);
    }

    [Fact]
    public void Infers_npm_build_before_npm_test()
    {
        File.WriteAllText(Path.Combine(_repo, "package.json"),
            """{"scripts":{"test":"vitest","build":"vite build"}}""");

        var command = ValidationCommandInferrer.Infer(_repo);

        Assert.Equal("npm run build", command);
    }

    [Fact]
    public void Infers_npm_test_when_build_script_is_absent()
    {
        File.WriteAllText(Path.Combine(_repo, "package.json"),
            """{"scripts":{"test":"vitest"}}""");

        var command = ValidationCommandInferrer.Infer(_repo);

        Assert.Equal("npm test", command);
    }

    [Fact]
    public void Infers_gradle_wrapper()
    {
        File.WriteAllText(Path.Combine(_repo, "gradlew.bat"), string.Empty);

        var command = ValidationCommandInferrer.Infer(_repo);

        Assert.Equal("gradlew.bat assembleDebug", command);
    }

    [Fact]
    public void Returns_null_when_no_known_stack_is_found()
    {
        var command = ValidationCommandInferrer.Infer(_repo);

        Assert.Null(command);
    }
}
