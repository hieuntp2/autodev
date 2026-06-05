using System.Diagnostics;
using System.Text;

namespace AutoDev.Verification;

public sealed class CommandRunner
{
    public async Task<CommandResult> RunShellAsync(
        string command,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var isWindows = OperatingSystem.IsWindows();
        var fileName = isWindows ? "cmd.exe" : "/bin/sh";
        var arguments = isWindows ? new[] { "/c", command } : ["-c", command];
        return await RunProcessAsync(fileName, arguments, workingDirectory, null, command, cancellationToken);
    }

    public async Task<CommandResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? standardInput = null,
        string? displayCommand = null,
        CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                error.AppendLine(args.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException($"{fileName} is not installed or not available in PATH.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);
        stopwatch.Stop();

        return new CommandResult
        {
            Command = displayCommand ?? BuildDisplayCommand(fileName, arguments),
            ExitCode = process.ExitCode,
            StandardOutput = output.ToString(),
            StandardError = error.ToString(),
            Duration = stopwatch.Elapsed
        };
    }

    private static string BuildDisplayCommand(string fileName, IEnumerable<string> arguments)
    {
        return string.Join(" ", new[] { fileName }.Concat(arguments));
    }
}
