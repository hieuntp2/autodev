namespace AutoDev.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int UnhandledException = 1;
    public const int UsageError = 2;
    public const int InvalidConfig = 3;
    public const int Blocked = 4;
    public const int FailedBuild = 5;
    public const int UnsafeWriteBlocked = 6;
}
