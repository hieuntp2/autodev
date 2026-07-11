namespace AutoDevRunner.Services;

public sealed record ValidationProcessLaunch(string FileName, string Arguments);

public static class ValidationCommandLauncher
{
    public static ValidationProcessLaunch Resolve(string command, bool? windows = null)
    {
        command = command.Trim();
        if (windows ?? OperatingSystem.IsWindows())
        {
            // CreateProcess does not resolve relative .bat/.cmd files from the supplied
            // working directory. cmd.exe preserves the configured validation command
            // semantics and resolves wrappers such as gradlew.bat correctly.
            return new ValidationProcessLaunch("cmd.exe", $"/d /s /c \"{command}\"");
        }

        var idx = command.IndexOf(' ');
        return idx < 0
            ? new ValidationProcessLaunch(command, string.Empty)
            : new ValidationProcessLaunch(command[..idx], command[(idx + 1)..]);
    }
}
