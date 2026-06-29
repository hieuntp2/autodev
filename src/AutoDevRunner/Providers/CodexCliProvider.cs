using AutoDevRunner.Config;
using AutoDevRunner.Models;
using Microsoft.Extensions.Options;

namespace AutoDevRunner.Providers;

/// <summary>Adapter for the Codex CLI (the primary provider).</summary>
public class CodexCliProvider : CliProviderBase
{
    public CodexCliProvider(ProcessRunner runner, IOptions<AutoDevOptions> options)
        : base(runner, options.Value.Providers.Codex) { }

    public override ProviderKind Kind => ProviderKind.Codex;
}
