# GPT-5.6 Sol and Two-Hour Schedule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Configure AutoDev Runner to use GPT-5.6 Sol for high-capability Codex and planner work, GPT-5.5 for Light Codex work, and a two-hour default scheduler cadence.

**Architecture:** Keep the existing provider and scheduler architecture unchanged. Lock the intended shipped defaults with tests that read the real JSON and PowerShell files, then make narrow value-only edits while preserving unrelated local configuration.

**Tech Stack:** .NET 8, xUnit, `System.Text.Json`, PowerShell configuration.

---

### Task 1: Lock the shipped provider and scheduler defaults

**Files:**
- Create: `src/AutoDevRunner.Tests/ShippedConfigurationTests.cs`
- Test: `src/AutoDevRunner.Tests/ShippedConfigurationTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Xunit;

namespace AutoDevRunner.Tests;

public class ShippedConfigurationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Appsettings_uses_requested_OpenAI_models()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot, "src", "AutoDevRunner", "appsettings.json")));
        var autoDev = doc.RootElement.GetProperty("AutoDev");
        var codex = autoDev.GetProperty("Providers").GetProperty("Codex");
        var tiers = codex.GetProperty("Tiers");

        Assert.Contains("-m gpt-5.6-sol", codex.GetProperty("Arguments").GetString());
        Assert.Contains("-m gpt-5.6-sol", codex.GetProperty("ResumeArguments").GetString());
        Assert.Contains("-m gpt-5.5", tiers.GetProperty("Light").GetString());
        Assert.Contains("-m gpt-5.6-sol", tiers.GetProperty("Standard").GetString());
        Assert.Contains("-m gpt-5.6-sol", tiers.GetProperty("Deep").GetString());
        Assert.Equal("gpt-5.6-sol", autoDev.GetProperty("Planner").GetProperty("Model").GetString());
    }

    [Fact]
    public void Appsettings_scheduler_interval_is_two_hours()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot, "src", "AutoDevRunner", "appsettings.json")));

        Assert.Equal(2, doc.RootElement.GetProperty("AutoDev")
            .GetProperty("Scheduler").GetProperty("IntervalHours").GetDouble());
    }

    [Fact]
    public void Installer_defaults_to_two_hour_interval()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "installer.ps1"));

        Assert.Contains("How often the run task fires. Default 2.", script);
        Assert.Contains("[double]$IntervalHours = 2", script);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AutoDevRunner.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the AutoDev Runner repository root.");
    }
}
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `dotnet test AutoDevRunner.sln --filter FullyQualifiedName~ShippedConfigurationTests`

Expected: all three tests fail because the shipped defaults still contain GPT-5.5/GPT-5.4-mini and a five-hour interval.

- [ ] **Step 3: Commit the failing tests**

```powershell
git add src/AutoDevRunner.Tests/ShippedConfigurationTests.cs
git commit -m "test: lock provider and scheduler defaults"
```

### Task 2: Update model and scheduler configuration

**Files:**
- Modify: `src/AutoDevRunner/appsettings.json`
- Modify: `scripts/installer.ps1`
- Test: `src/AutoDevRunner.Tests/ShippedConfigurationTests.cs`

- [ ] **Step 1: Apply the minimal configuration edits**

In `src/AutoDevRunner/appsettings.json`, set `"IntervalHours": 2` and use:

```json
"Arguments": "exec --skip-git-repo-check --sandbox danger-full-access --json -m gpt-5.6-sol",
"ResumeArguments": "exec resume {SESSION_ID} --skip-git-repo-check --sandbox danger-full-access --json -m gpt-5.6-sol",
"Light": "exec --skip-git-repo-check --sandbox danger-full-access --json -m gpt-5.5 -c model_reasoning_effort=low",
"Standard": "exec --skip-git-repo-check --sandbox danger-full-access --json -m gpt-5.6-sol -c model_reasoning_effort=medium",
"Deep": "exec --skip-git-repo-check --sandbox danger-full-access --json -m gpt-5.6-sol -c model_reasoning_effort=high"
```

Set the planner model to `"Model": "gpt-5.6-sol"`.

In `scripts/installer.ps1`, set:

```powershell
.PARAMETER IntervalHours
    How often the run task fires. Default 2.

[double]$IntervalHours = 2,
```

- [ ] **Step 2: Run the focused tests and verify GREEN**

Run: `dotnet test AutoDevRunner.sln --filter FullyQualifiedName~ShippedConfigurationTests`

Expected: 3 passed, 0 failed.

- [ ] **Step 3: Inspect the diff for scope and secret safety**

Run:

```powershell
git diff -- src/AutoDevRunner/appsettings.json scripts/installer.ps1 src/AutoDevRunner.Tests/ShippedConfigurationTests.cs
git diff --check
```

Expected: only the approved model/interval values, installer documentation, and focused tests are new; the user's unrelated PostgreSQL and local source changes remain untouched.

- [ ] **Step 4: Commit only the implementation files**

```powershell
git add src/AutoDevRunner.Tests/ShippedConfigurationTests.cs src/AutoDevRunner/appsettings.json scripts/installer.ps1
git commit -m "feat: use GPT-5.6 Sol and two-hour schedule"
```

### Task 3: Verify the complete solution

**Files:**
- Verify: `AutoDevRunner.sln`

- [ ] **Step 1: Run the complete test suite**

Run: `dotnet test AutoDevRunner.sln`

Expected: all tests pass with 0 failures.

- [ ] **Step 2: Build the complete solution**

Run: `dotnet build AutoDevRunner.sln --no-restore`

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Confirm final working-tree scope**

Run:

```powershell
git status --short
git show --stat --oneline HEAD
```

Expected: the implementation commit contains only the tests, approved configuration changes, and installer changes; pre-existing user changes outside that commit remain visible and untouched.
