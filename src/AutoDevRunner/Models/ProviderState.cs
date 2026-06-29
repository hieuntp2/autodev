namespace AutoDevRunner.Models;

/// <summary>Global status for each provider, shown on the Providers dashboard.</summary>
public class ProviderState
{
    public int Id { get; set; }

    public ProviderKind Provider { get; set; }
    public bool Enabled { get; set; } = true;

    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastAuthErrorAt { get; set; }
    public DateTime? LastQuotaLimitAt { get; set; }
    public string? LastQuotaResetHint { get; set; }
    public string? LastKnownUsage { get; set; }
}
