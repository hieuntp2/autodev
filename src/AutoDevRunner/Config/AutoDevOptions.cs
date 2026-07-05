namespace AutoDevRunner.Config;

public class AutoDevOptions
{
    public const string SectionName = "AutoDev";

    public SchedulerOptions Scheduler { get; set; } = new();
    public ProvidersOptions Providers { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
    public PlannerOptions Planner { get; set; } = new();
    public ContinuousOptions Continuous { get; set; } = new();
    public SkillsOptions Skills { get; set; } = new();
    public ProjectMemoryOptions ProjectMemory { get; set; } = new();
    public RiskOptions Risk { get; set; } = new();
}

/// <summary>Controls whether AutoDev writes back to a project's memory files.</summary>
public class ProjectMemoryOptions
{
    /// <summary>
    /// When true, after a run AutoDev appends the run's ideas/decisions/next tasks
    /// into <c>.ai-runner/IDEAS.md</c>, <c>DECISIONS.md</c> and <c>BACKLOG.md</c>
    /// (deduplicated, dated). When false (default) it only records them in the run
    /// log and never edits the project's memory files.
    /// </summary>
    public bool AutoWriteEnabled { get; set; } = false;
}

/// <summary>Controls autonomous handling of risky tasks.</summary>
public class RiskOptions
{
    /// <summary>
    /// When false (default), a run whose task is classified <c>Risky</c> (DB
    /// migration, mass deletion, deploy/production, secret/config edits, large
    /// refactor) is NOT executed autonomously — it is blocked and reported as
    /// requiring manual approval/config. When true, risky runs proceed.
    /// </summary>
    public bool AllowRiskyAutonomousRuns { get; set; } = false;
}

/// <summary>
/// Global AutoDev skill system. Skills live once in a shared store
/// (<c>AutoDevSkills/</c>) and are selected per run by keyword-matching the
/// project brief / notes / task / creative plan against each skill's triggers.
/// The selected skill is injected explicitly into the CLI prompt.
/// </summary>
public class SkillsOptions
{
    /// <summary>Master switch for the whole skill system.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the global skill store. Absolute wins; a relative path is tried
    /// against the app base dir and the current dir. When empty, the registry
    /// auto-discovers an "AutoDevSkills" folder by walking up from those roots.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>Skill ids force-disabled by config (in addition to the runtime toggle).</summary>
    public List<string> Disabled { get; set; } = new();

    /// <summary>
    /// When true, keyword-matching auto-selects skills for a run. When false,
    /// skills are only used if a project explicitly names one (future use).
    /// </summary>
    public bool AutoSelect { get; set; } = true;

    /// <summary>
    /// Optionally copy the selected skill into <c>&lt;repo&gt;/.agents/skills/&lt;id&gt;</c>
    /// so a CLI that discovers skills from the working tree can see it. The copy
    /// is added to the repo's local git exclude so it is never committed. Off by
    /// default: the skill is normally injected into the prompt with absolute
    /// script paths instead, keeping project repos clean.
    /// </summary>
    public bool ExportToProject { get; set; } = false;
}

/// <summary>
/// Continuous mode: instead of one run per trigger, keep running back-to-back
/// (each run auto-commits per project policy) while at least one provider is
/// still viable, i.e. its known quota usage is below <see cref="MaxUsagePercent"/>
/// and it has not just reported a quota/rate limit. Codex usage is read from
/// its local session files (5h + weekly windows); Claude has no headless usage
/// API, so it is assumed viable until it reports a quota error.
/// </summary>
public class ContinuousOptions
{
    /// <summary>When true, `--run-due` loops until no provider is viable.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Stop using a provider once its usage reaches this percentage.</summary>
    public double MaxUsagePercent { get; set; } = 95;

    /// <summary>Safety cap on back-to-back runs per invocation.</summary>
    public int MaxRunsPerSession { get; set; } = 24;

    /// <summary>Pause between consecutive runs.</summary>
    public int DelayBetweenRunsSeconds { get; set; } = 20;

    /// <summary>How long to bench a provider after it reports a quota error
    /// (used when no exact reset time is known).</summary>
    public int QuotaCooldownMinutes { get; set; } = 60;
}

/// <summary>
/// Optional OpenAI "creative planner" pre-step. Before the AI CLI runs, one
/// OpenAI Responses API call is made with the file_search tool attached to the
/// configured knowledge-base vector store(s). The resulting creative brief is
/// injected into the CLI prompt so the implementer builds against it.
/// The whole step is fail-soft: if disabled, unconfigured, or the call fails,
/// the run proceeds with the base prompt.
/// </summary>
public class PlannerOptions
{
    /// <summary>Master switch. When false, no OpenAI call is made.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Responses API model, e.g. "gpt-4.1".</summary>
    public string Model { get; set; } = "gpt-4.1";

    /// <summary>
    /// OpenAI API key. Preferred here (in appsettings) so no global
    /// OPENAI_API_KEY env var is required. Falls back to the OPENAI_API_KEY
    /// environment variable only when this is empty.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Knowledge-base vector store ids attached to every planner call via
    /// file_search. Change these to swap the knowledge base. Empty disables
    /// file search (the planner then reasons from the brief alone).
    /// </summary>
    public List<string> VectorStoreIds { get; set; } = new();

    /// <summary>Responses API endpoint. Override for Azure/proxy setups.</summary>
    public string ApiUrl { get; set; } = "https://api.openai.com/v1/responses";
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
