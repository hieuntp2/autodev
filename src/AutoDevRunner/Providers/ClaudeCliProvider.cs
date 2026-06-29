using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Providers;

/// <summary>Adapter for the Claude CLI.</summary>
public class ClaudeCliProvider : CliProviderBase
{
    public ClaudeCliProvider(ProcessRunner runner, IOptions<AutoDevOptions> options)
        : base(runner, options.Value.Providers.Claude) { }

    public override ProviderKind Kind => ProviderKind.Claude;
}
