namespace AutoDevRunner.Config;

public class AutoDevOptions
{
    public const string SectionName = "AutoDev";

    public SchedulerOptions Scheduler { get; set; } = new();
    public ProvidersOptions Providers { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
}

public class SchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public double IntervalHours { get; set; } = 5;
    public bool RunOnStartup { get; set; } = false;
}

public class ProvidersOptions
{
    public ProviderCliOptions Codex { get; set; } = new();
    public ProviderCliOptions Claude { get; set; } = new();
}

public class ProviderCliOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Executable to invoke (e.g. "codex" or "claude").</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Argument template. "{PROMPT}" is replaced with the (escaped) prompt.</summary>
    public string Arguments { get; set; } = string.Empty;
}

/// <summary>
/// EmailJS REST configuration. Reports are sent by POSTing to the EmailJS
/// "send" API with your service/template ids and keys. Your EmailJS template
/// receives these params: subject, message, to_email, project, status.
/// (Enable "Allow EmailJS API for non-browser applications" in your EmailJS
/// account and set the PrivateKey for server-side sending.)
/// </summary>
public class EmailOptions
{
    public bool Enabled { get; set; } = false;
    public string ApiUrl { get; set; } = "https://api.emailjs.com/api/v1.0/email/send";
    public string ServiceId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>EmailJS public key (sent as user_id).</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>EmailJS private key (sent as accessToken; required for non-browser calls).</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Recipient; passed to the template as to_email.</summary>
    public string ToEmail { get; set; } = string.Empty;
}
