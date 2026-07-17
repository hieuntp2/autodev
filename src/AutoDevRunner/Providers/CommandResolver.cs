using System.Runtime.InteropServices;

namespace AutoDevRunner.Providers;

/// <summary>
/// Resolves a configured provider command (e.g. "codex") into something
/// <see cref="System.Diagnostics.Process"/> can actually launch with
/// <c>UseShellExecute=false</c>.
///
/// On Windows this matters: npm-installed CLIs (codex, and many others) ship
/// only as a ".cmd" batch shim plus an extension-less shell script — there is
/// no "codex.exe". <c>Process.Start("codex")</c> auto-appends only ".exe", so
/// it fails with "The system cannot find the file specified" (exit -1). Claude
/// happened to work only because it is a native ".exe". We fix the whole class
/// of provider by resolving the real file via PATH + PATHEXT and routing batch
/// shims through cmd.exe.
/// </summary>
public static class CommandResolver
{
    /// <summary>
    /// Returns the (FileName, Arguments) pair to hand to
    /// <see cref="System.Diagnostics.ProcessStartInfo"/>. On non-Windows, or
    /// when the command cannot be resolved, the inputs are returned unchanged
    /// so behaviour matches the previous direct launch.
    /// </summary>
    public static (string FileName, string Arguments) Resolve(string command, string arguments)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || string.IsNullOrWhiteSpace(command))
            return (command, arguments);

        var resolved = ResolveWindowsPath(command);
        if (resolved is null)
            return (command, arguments); // let Process.Start fail with its own message

        var ext = Path.GetExtension(resolved);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            // Canonical safe form for launching a batch shim with arguments
            // (as used by Node's cross-spawn / Rust's std): the extra outer
            // quote pair plus /s makes cmd treat everything between as one
            // verbatim command line. /d skips any AutoRun, /c runs then exits.
            var wrapped = $"/d /s /c \"\"{resolved}\" {arguments}\"";
            return (comspec, wrapped);
        }

        // Native executable (.exe/.com) or anything else: launch the resolved
        // path directly. Using the full path avoids a second PATH search.
        return (resolved, arguments);
    }

    private static string? ResolveWindowsPath(string command)
    {
        var pathext = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        IEnumerable<string> Candidates(string dir)
        {
            var basePath = dir.Length == 0 ? command : Path.Combine(dir, command);
            // Honour an explicit extension first (e.g. a configured "codex.cmd").
            if (Path.HasExtension(command))
                yield return basePath;
            foreach (var ext in pathext)
                yield return basePath + ext;
        }

        // Command already carries a directory: resolve relative to it only.
        if (Path.IsPathRooted(command)
            || command.Contains(Path.DirectorySeparatorChar)
            || command.Contains(Path.AltDirectorySeparatorChar))
        {
            foreach (var c in Candidates(string.Empty))
                if (File.Exists(c)) return Path.GetFullPath(c);
            return null;
        }

        // Bare command: search each PATH directory, PATHEXT within each.
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dir in dirs)
            foreach (var c in Candidates(dir))
                if (File.Exists(c)) return c;

        return null;
    }
}
