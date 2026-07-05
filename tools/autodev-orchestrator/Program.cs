namespace AutoDevOrchestrator;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "status";
            var paths = RepoPaths.Discover();
            paths.EnsureDirectories();
            var config = OrchestratorConfig.Load(paths);

            return command switch
            {
                "status" => Commands.Status(paths, config),
                "report" => Commands.Report(paths, config),
                "plan" => await Commands.Plan(paths, config),
                "run" => await Commands.Run(paths, config),
                "help" or "--help" or "-h" => Commands.Help(),
                _ => Unknown(command)
            };
        }
        catch (OrchestratorException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Commands.Help();
        return 2;
    }
}
