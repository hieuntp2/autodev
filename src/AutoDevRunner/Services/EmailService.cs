using System.Net.Http.Json;
using System.Text;
using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Services;

/// <summary>
/// Sends the post-run summary email through the EmailJS REST API.
/// No-op when disabled or not configured.
/// </summary>
public class EmailService
{
    private readonly EmailOptions _opt;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EmailService> _log;

    public EmailService(IOptions<AutoDevOptions> opt, IHttpClientFactory httpFactory, ILogger<EmailService> log)
    {
        _opt = opt.Value.Email;
        _httpFactory = httpFactory;
        _log = log;
    }

    public bool Enabled =>
        _opt.Enabled
        && !string.IsNullOrWhiteSpace(_opt.ServiceId)
        && !string.IsNullOrWhiteSpace(_opt.TemplateId)
        && !string.IsNullOrWhiteSpace(_opt.PublicKey)
        && !string.IsNullOrWhiteSpace(_opt.ToEmail);

    public string BuildReport(Project project, RunRecord run, ParsedSummary? summary,
        string? costSummary = null, string? sessionResult = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AutoDev run report — {project.Name}");
        sb.AppendLine(new string('=', 50));
        sb.AppendLine($"Provider : {run.Provider}");
        sb.AppendLine($"Branch   : {run.Branch}");
        sb.AppendLine($"Status   : {run.Status}");
        if (!string.IsNullOrWhiteSpace(sessionResult))
            sb.AppendLine($"Result   : {sessionResult}");
        if (!string.IsNullOrWhiteSpace(run.Reason))
            sb.AppendLine($"Reason   : {run.Reason}");
        sb.AppendLine($"Started  : {run.StartedAt:u}");
        sb.AppendLine($"Finished : {run.FinishedAt:u}");
        sb.AppendLine($"Usage    : {run.Usage ?? "Unknown / provider does not expose usage"}");
        if (!string.IsNullOrWhiteSpace(costSummary))
            sb.AppendLine($"Cost     : {costSummary}");
        sb.AppendLine();

        if (summary is not null)
        {
            sb.AppendLine($"Task     : {summary.Task ?? "-"}");
            sb.AppendLine($"Done     : {summary.Done ?? "-"}");
            sb.AppendLine($"Pending  : {summary.Pending ?? "-"}");
            sb.AppendLine($"Ideas    : {summary.Ideas ?? "-"}");
            sb.AppendLine();
        }

        sb.AppendLine("Changed files:");
        sb.AppendLine(string.IsNullOrWhiteSpace(run.ChangedFiles) ? "  (none)" : run.ChangedFiles);
        sb.AppendLine();

        sb.AppendLine($"Validation: {(run.ValidationRun ? (run.ValidationPassed ? "PASSED" : "FAILED") : "not run")}");
        if (run.ValidationRun && !run.ValidationPassed && !string.IsNullOrWhiteSpace(run.ValidationOutput))
        {
            sb.AppendLine("--- validation output (tail) ---");
            sb.AppendLine(Tail(run.ValidationOutput!, 2000));
        }

        if (!string.IsNullOrWhiteSpace(run.CommitSha))
            sb.AppendLine($"\nCommit: {run.CommitSha}");
        if (!string.IsNullOrWhiteSpace(run.LogPath))
            sb.AppendLine($"Log: {run.LogPath}");

        return sb.ToString();
    }

    public async Task<bool> SendAsync(string subject, string body, CancellationToken ct = default)
        => await SendAsync(subject, body, project: null, status: null, ct);

    public async Task<bool> SendAsync(string subject, string body, string? project, string? status,
        CancellationToken ct = default)
    {
        if (!Enabled)
        {
            _log.LogInformation("EmailJS disabled or not configured; skipping report send.");
            return false;
        }

        try
        {
            var payload = new
            {
                service_id = _opt.ServiceId,
                template_id = _opt.TemplateId,
                user_id = _opt.PublicKey,
                accessToken = string.IsNullOrWhiteSpace(_opt.PrivateKey) ? null : _opt.PrivateKey,
                template_params = new Dictionary<string, string?>
                {
                    ["subject"] = subject,
                    ["message"] = body,
                    ["to_email"] = _opt.ToEmail,
                    ["project"] = project,
                    ["status"] = status
                }
            };

            var http = _httpFactory.CreateClient();
            var res = await http.PostAsJsonAsync(_opt.ApiUrl, payload, ct);
            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Sent EmailJS report to {To}", _opt.ToEmail);
                return true;
            }

            var detail = await res.Content.ReadAsStringAsync(ct);
            _log.LogError("EmailJS send failed: {Status} {Detail}", (int)res.StatusCode, detail);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send EmailJS report.");
            return false;
        }
    }

    private static string Tail(string s, int max) =>
        s.Length <= max ? s : "…\n" + s[^max..];
}
