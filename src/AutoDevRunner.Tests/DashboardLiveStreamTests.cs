using Xunit;

namespace AutoDevRunner.Tests;

public class DashboardLiveStreamTests
{
    [Fact]
    public void Run_detail_connects_to_sse_and_closes_on_terminal_event()
    {
        var appJs = File.ReadAllText(RepoFile("src", "AutoDevRunner", "wwwroot", "app.js"));

        Assert.Contains("new EventSource", appJs);
        Assert.Contains("/events", appJs);
        Assert.Contains("live-events", appJs);
        Assert.Contains("event.terminal", appJs);
        Assert.Contains("closeRunEventStream", appJs);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutoDevRunner.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }
}
