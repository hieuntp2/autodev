using AutoDev.Cli;
using AutoDev.Codex;
using AutoDev.Core.Services;
using AutoDev.Git;
using AutoDev.OpenAI;
using AutoDev.Verification;

var rootPath = Directory.GetCurrentDirectory();
var cancellationToken = CancellationToken.None;

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

var command = args[0].ToLowerInvariant();
var projectId = ReadProjectId(args);
if (string.IsNullOrWhiteSpace(projectId))
{
    Console.Error.WriteLine("Missing required option: --project <projectId>");
    PrintUsage();
    return ExitCodes.UsageError;
}

var commandRunner = new CommandRunner();
var orchestrator = new DailyRunOrchestrator(
    rootPath,
    new ProjectConfigLoader(rootPath),
    new WorkspaceService(rootPath),
    new GitService(commandRunner),
    new OpenAIPlannerClient(new HttpClient()),
    new CodexRunner(commandRunner),
    new VerificationRunner(commandRunner));

return command switch
{
    "run" => await orchestrator.RunAsync(projectId, cancellationToken),
    "status" => await orchestrator.StatusAsync(projectId, cancellationToken),
    "plan" or "implement" or "verify" or "review" or "retrospective" => UnsupportedForMvp(command),
    _ => UnknownCommand(command)
};

static string? ReadProjectId(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--project", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            return args[i + 1];
        }
    }

    return null;
}

static int UnsupportedForMvp(string command)
{
    Console.Error.WriteLine($"Command '{command}' is reserved for expansion. Use 'run' or 'status' in the MVP.");
    return ExitCodes.UsageError;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return ExitCodes.UsageError;
}

static void PrintUsage()
{
    Console.WriteLine("""
    AutoDev Orchestrator

    Usage:
      autodev run --project ai-pet
      autodev status --project ai-pet

    Exit codes:
      0  Success (changes committed or no-op)
      1  Unhandled exception
      2  Usage error (bad arguments)
      3  Invalid config
      4  Blocked (no eligible task)
      5  Failed build
      6  Unsafe write blocked
    """);
}
