using AutoDevRunner.Config;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// Optional in-process scheduler. Disabled by default — the recommended setup
/// uses Windows Task Scheduler to invoke `AutoDevRunner.exe --run-due`
/// (see scripts/installer.ps1). Enable via AutoDev:Scheduler:Enabled if you'd
/// rather keep the host running and let it self-schedule.
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly DueProjectsRunner _runner;
    private readonly ContinuousRunner _continuous;
    private readonly SchedulerOptions _opt;
    private readonly bool _continuousEnabled;
    private readonly ILogger<SchedulerService> _log;

    public SchedulerService(DueProjectsRunner runner, ContinuousRunner continuous, IOptions<AutoDevOptions> opt, ILogger<SchedulerService> log)
    {
        _runner = runner;
        _continuous = continuous;
        _opt = opt.Value.Scheduler;
        _continuousEnabled = opt.Value.Continuous.Enabled;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(0.1, _opt.IntervalHours));
        _log.LogInformation("In-process scheduler started. Interval: {Hours}h", _opt.IntervalHours);

        if (_opt.RunOnStartup)
            await SafeRun(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SafeRun(stoppingToken);
    }

    private async Task SafeRun(CancellationToken ct)
    {
        try
        {
            if (_continuousEnabled) await _continuous.RunAsync(ct);
            else await _runner.RunAllDueAsync(ct);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex) { _log.LogError(ex, "Scheduler tick failed."); }
    }
}
