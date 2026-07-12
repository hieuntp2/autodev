# Provider Startup and Usage Reporting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore native-Windows Codex/Claude startup and replace the misleading missing-usage message with outcome-aware reporting.

**Architecture:** Add one small pure formatting policy between `ProviderInvocation` and `RunRecord`, leaving all structured metrics and downstream reports unchanged. Repair Claude's shipped command templates explicitly, update the host Codex CLI operationally, and verify each provider with bounded one-turn smoke calls.

**Tech Stack:** .NET 8, C#, xUnit, ASP.NET Core configuration JSON, Codex CLI, Claude Code CLI, PowerShell

---

## File Structure

- Create `src/AutoDevRunner/Services/RunUsageFormatter.cs`: pure outcome-aware fallback policy for `RunRecord.Usage`.
- Create `src/AutoDevRunner.Tests/RunUsageFormatterTests.cs`: regression coverage for failure, success, and reported-usage paths.
- Modify `src/AutoDevRunner/Services/RunOrchestrator.cs`: delegate the existing usage assignment to the policy.
- Modify `src/AutoDevRunner/Services/EmailService.cs`: use the same truthful unknown-usage fallback for legacy/null run records.
- Create `src/AutoDevRunner.Tests/EmailServiceTests.cs`: pin the email report's null-usage fallback.
- Modify `src/AutoDevRunner.Tests/ShippedConfigurationTests.cs`: pin every shipped Claude command template to the native-Windows unattended flag.
- Modify `src/AutoDevRunner/appsettings.json`: repair default, resume, and tiered Claude arguments.

### Task 1: Outcome-aware usage fallback

**Files:**
- Create: `src/AutoDevRunner/Services/RunUsageFormatter.cs`
- Create: `src/AutoDevRunner.Tests/RunUsageFormatterTests.cs`
- Create: `src/AutoDevRunner.Tests/EmailServiceTests.cs`
- Modify: `src/AutoDevRunner/Services/RunOrchestrator.cs:382`
- Modify: `src/AutoDevRunner/Services/EmailService.cs:48`

- [ ] **Step 1: Write the failing formatter tests**

Create `src/AutoDevRunner.Tests/RunUsageFormatterTests.cs`:

```csharp
using AutoDevRunner.Providers;
using AutoDevRunner.Services;
using Xunit;

namespace AutoDevRunner.Tests;

public class RunUsageFormatterTests
{
    [Theory]
    [InlineData(ProviderOutcome.QuotaLimit)]
    [InlineData(ProviderOutcome.AuthError)]
    [InlineData(ProviderOutcome.Error)]
    [InlineData(ProviderOutcome.Timeout)]
    public void Failed_invocation_without_metrics_reports_failure(ProviderOutcome outcome)
    {
        var invocation = new ProviderInvocation(outcome, "", null, "failed", null);

        var result = RunUsageFormatter.Format(invocation);

        Assert.Equal("No usage reported - provider invocation failed", result);
    }

    [Fact]
    public void Successful_invocation_without_metrics_reports_missing_provider_data()
    {
        var invocation = new ProviderInvocation(ProviderOutcome.Success, "", null, null, null);

        var result = RunUsageFormatter.Format(invocation);

        Assert.Equal("Unknown - provider did not report usage", result);
    }

    [Fact]
    public void Provider_reported_usage_is_preserved()
    {
        var invocation = new ProviderInvocation(
            ProviderOutcome.Success, "", "in 1,234 / out 56", null, null, 1234, 56);

        var result = RunUsageFormatter.Format(invocation);

        Assert.Equal("in 1,234 / out 56", result);
    }
}
```

Create `src/AutoDevRunner.Tests/EmailServiceTests.cs`:

```csharp
using AutoDevRunner.Config;
using AutoDevRunner.Models;
using AutoDevRunner.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDevRunner.Tests;

public class EmailServiceTests
{
    [Fact]
    public void Report_with_null_usage_uses_truthful_fallback()
    {
        var service = new EmailService(
            Options.Create(new AutoDevOptions()),
            new UnusedHttpClientFactory(),
            NullLogger<EmailService>.Instance);

        var report = service.BuildReport(new Project { Name = "demo" }, new RunRecord(), null);

        Assert.Contains($"Usage    : {RunUsageFormatter.MissingProviderData}", report);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test AutoDevRunner.sln --filter FullyQualifiedName~RunUsageFormatterTests
dotnet test AutoDevRunner.sln --filter FullyQualifiedName~EmailServiceTests
```

Expected: both commands FAIL to compile because `RunUsageFormatter` does not exist.

- [ ] **Step 3: Implement the minimal pure policy**

Create `src/AutoDevRunner/Services/RunUsageFormatter.cs`:

```csharp
using AutoDevRunner.Providers;

namespace AutoDevRunner.Services;

public static class RunUsageFormatter
{
    public const string FailedInvocation = "No usage reported - provider invocation failed";
    public const string MissingProviderData = "Unknown - provider did not report usage";

    public static string Format(ProviderInvocation invocation)
    {
        if (!string.IsNullOrWhiteSpace(invocation.Usage))
            return invocation.Usage;

        return invocation.Outcome is ProviderOutcome.Success
            ? MissingProviderData
            : FailedInvocation;
    }
}
```

In `src/AutoDevRunner/Services/RunOrchestrator.cs`, replace:

```csharp
run.Usage = invocation.Usage ?? "Unknown / provider does not expose usage";
```

with:

```csharp
run.Usage = RunUsageFormatter.Format(invocation);
```

In `src/AutoDevRunner/Services/EmailService.cs`, replace:

```csharp
sb.AppendLine($"Usage    : {run.Usage ?? "Unknown / provider does not expose usage"}");
```

with:

```csharp
sb.AppendLine($"Usage    : {run.Usage ?? RunUsageFormatter.MissingProviderData}");
```

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run:

```powershell
dotnet test AutoDevRunner.sln --filter FullyQualifiedName~RunUsageFormatterTests
dotnet test AutoDevRunner.sln --filter FullyQualifiedName~EmailServiceTests
```

Expected: PASS, 6 formatter test cases and 1 email test successful.

- [ ] **Step 5: Commit the usage policy**

```powershell
git add -- src/AutoDevRunner/Services/RunUsageFormatter.cs src/AutoDevRunner/Services/RunOrchestrator.cs src/AutoDevRunner/Services/EmailService.cs src/AutoDevRunner.Tests/RunUsageFormatterTests.cs src/AutoDevRunner.Tests/EmailServiceTests.cs
git commit -m "fix: report missing provider usage accurately"
```

### Task 2: Native-Windows Claude command templates

**Files:**
- Modify: `src/AutoDevRunner.Tests/ShippedConfigurationTests.cs`
- Modify: `src/AutoDevRunner/appsettings.json:39-44`

- [ ] **Step 1: Write the failing shipped-configuration test**

Add this test to `ShippedConfigurationTests`:

```csharp
[Fact]
public void Claude_commands_use_native_Windows_unattended_permissions()
{
    using var doc = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot, "src", "AutoDevRunner", "appsettings.json")));
    var claude = doc.RootElement.GetProperty("AutoDev")
        .GetProperty("Providers").GetProperty("Claude");
    var tiers = claude.GetProperty("Tiers");
    var arguments = new[]
    {
        claude.GetProperty("Arguments").GetString(),
        claude.GetProperty("ResumeArguments").GetString(),
        tiers.GetProperty("Light").GetString(),
        tiers.GetProperty("Standard").GetString(),
        tiers.GetProperty("Deep").GetString()
    };

    Assert.All(arguments, value =>
    {
        Assert.Contains("--dangerously-skip-permissions", value);
        Assert.DoesNotContain("--permission-mode bypassPermissions", value);
    });
}
```

- [ ] **Step 2: Run the configuration test and verify RED**

Run:

```powershell
dotnet test AutoDevRunner.sln --filter FullyQualifiedName~Claude_commands_use_native_Windows_unattended_permissions
```

Expected: FAIL because all five templates still contain `--permission-mode bypassPermissions`.

- [ ] **Step 3: Repair every Claude template**

In `src/AutoDevRunner/appsettings.json`, set the Claude provider block to:

```json
"Claude": {
  "Enabled": false,
  "Command": "claude",
  "Arguments": "-p --dangerously-skip-permissions --output-format json",
  "ResumeArguments": "-p --resume {SESSION_ID} --dangerously-skip-permissions --output-format json",
  "Tiers": {
    "Light": "-p --dangerously-skip-permissions --output-format json --model claude-haiku-4-5",
    "Standard": "-p --dangerously-skip-permissions --output-format json --model claude-sonnet-5",
    "Deep": "-p --dangerously-skip-permissions --output-format json --model claude-opus-4-8"
  }
}
```

- [ ] **Step 4: Run the configuration tests and verify GREEN**

Run:

```powershell
dotnet test AutoDevRunner.sln --filter FullyQualifiedName~ShippedConfigurationTests
```

Expected: PASS, including the Codex model, scheduler, installer, and Claude argument assertions.

- [ ] **Step 5: Commit the Claude repair**

```powershell
git add -- src/AutoDevRunner/appsettings.json src/AutoDevRunner.Tests/ShippedConfigurationTests.cs
git commit -m "fix: run Claude unattended on native Windows"
```

### Task 3: Host Codex update and provider smoke verification

**Files:**
- No repository files.

- [ ] **Step 1: Record the current Codex version**

Run:

```powershell
codex --version
```

Expected before repair: `codex-cli 0.142.5`.

- [ ] **Step 2: Update Codex through its supported updater**

Run:

```powershell
codex update
```

Expected: the updater reports a successful installation of a newer Codex build. If the active desktop process prevents replacement, report the exact updater output and stop rather than changing the configured model.

- [ ] **Step 3: Verify the installed version changed**

Run:

```powershell
codex --version
```

Expected: a version newer than `0.142.5`.

- [ ] **Step 4: Smoke-test the configured Codex model**

Run:

```powershell
codex exec --skip-git-repo-check --sandbox read-only --json -m gpt-5.6-sol "Reply exactly OK and do not use tools."
```

Expected: exit code 0, a `turn.completed` JSONL event, and no "requires a newer version" error. The event should include usage or the local Codex session rollout should contain `last_token_usage` for the turn.

- [ ] **Step 5: Smoke-test Claude's repaired Windows flag**

Run from the repository root using the configured executable path:

```powershell
& 'C:\Users\PC\.local\bin\claude.exe' -p --dangerously-skip-permissions --output-format json --max-turns 1 "Reply exactly OK and do not use tools."
```

Expected: exit code 0, a JSON result envelope with `usage`, and no "sandbox required but unavailable" error.

### Task 4: Full regression verification

**Files:**
- No additional files.

- [ ] **Step 1: Check formatting and scope**

Run:

```powershell
git diff --check HEAD~2
git status --short
```

Expected: no whitespace errors; only intentional commits and no untracked implementation files.

- [ ] **Step 2: Run the full test suite**

Run:

```powershell
dotnet test AutoDevRunner.sln
```

Expected: PASS with zero failed tests.

- [ ] **Step 3: Build the solution**

Run:

```powershell
dotnet build AutoDevRunner.sln
```

Expected: build succeeds with zero errors.

- [ ] **Step 4: Confirm the obsolete message and permission mode are gone from active source**

Run:

```powershell
rg -n -g '!bin/**' -g '!obj/**' -g '!docs/**' 'Unknown / provider does not expose usage|--permission-mode bypassPermissions' src
```

Expected: no matches in active source or shipped configuration.

- [ ] **Step 5: Review the final commit range**

Run:

```powershell
git log -3 --oneline
git status --short
```

Expected: the design commit followed by the focused usage and Claude repair commits; clean working tree.
