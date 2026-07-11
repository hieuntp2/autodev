using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunEventStoreTests : IDisposable
{
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ad-events-" + Guid.NewGuid().ToString("N"));

    public RunEventStoreTests() => Directory.CreateDirectory(_repo);

    public void Dispose()
    {
        try { Directory.Delete(_repo, recursive: true); } catch { }
    }

    [Fact]
    public void Event_path_is_deterministic_and_scoped_to_the_run()
    {
        var path = RunEventStore.PathFor(_repo, 123);

        Assert.Equal(Path.Combine(_repo, ".ai-runner", "runs", "run-123.events.jsonl"), path);
    }

    [Fact]
    public void Append_and_replay_preserve_sequence_json_and_terminal_state()
    {
        var store = new RunEventStore();
        using (var sink = store.Open(_repo, 7))
        {
            sink.Append("lifecycle", "started");
            sink.Append("agent", "quoted \"message\"\nnext line");
            sink.Append("terminal", "Success", terminal: true);
        }

        var all = store.ReadAfter(_repo, 7, afterSequence: 0);
        var tail = store.ReadAfter(_repo, 7, afterSequence: 1);

        Assert.Equal(new long[] { 1, 2, 3 }, all.Select(e => e.Sequence));
        Assert.Equal("quoted \"message\"\nnext line", all[1].Message);
        Assert.True(all[2].Terminal);
        Assert.Equal(new long[] { 2, 3 }, tail.Select(e => e.Sequence));
    }

    [Theory]
    [InlineData("still running - last output 30s ago", "heartbeat")]
    [InlineData("[agent] finished implementation", "agent")]
    [InlineData("[command] completed: dotnet test", "command")]
    [InlineData("ordinary pipeline message", "lifecycle")]
    public void Message_kind_is_suitable_for_dashboard_display(string message, string expected)
    {
        Assert.Equal(expected, RunEventStore.KindFor(message));
    }
}
