using System.Diagnostics;

namespace AutoDevOrchestrator;

public static class GitHelper
{
    /// <summary>Runs a git command in the repo root; returns trimmed stdout, or null if git is unavailable/fails.</summary>
    public static string? Run(RepoPaths paths, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = paths.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }
            return process.ExitCode == 0 ? stdout.TrimEnd() : null;
        }
        catch
        {
            return null;
        }
    }
}
