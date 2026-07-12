# Provider Startup and Usage Reporting Design

## Problem

Recent AutoDev runs report `Unknown / provider does not expose usage`, but the
providers do expose usage after a model turn. The affected runs never reached a
model turn:

- Codex rejected `gpt-5.6-sol` because the installed Codex CLI was too old.
- Claude then failed on native Windows because `bypassPermissions` requires a
  secure sandbox, which is unavailable outside macOS, Linux, or WSL2.

The usage message therefore misdiagnoses a pre-execution provider failure as a
provider capability limitation.

## Goals

- Restore unattended Codex and Claude provider startup on this Windows host.
- Preserve the configured `gpt-5.6-sol` Codex model rather than downgrading it.
- Preserve autonomous Claude execution without interactive permission prompts.
- Report missing usage accurately when a provider fails before model execution.
- Add focused regression coverage without restructuring provider orchestration.

## Non-goals

- Do not synthesize token or cost values when a provider reports none.
- Do not weaken AutoDev's repository, risk, deletion, secret, branch, validation,
  commit, or push guardrails.
- Do not add automatic CLI installation or self-update behavior to AutoDev.
- Do not alter historical run records.
- Do not change provider priority, model routing, or burn-token policy.

## Selected Approach

### Codex startup

Use the installed Codex CLI's supported `codex update` command to upgrade it in
place. Keep the existing `gpt-5.6-sol` configuration. Verify the installed
version and that the configured model no longer returns the "requires a newer
version" startup error.

The application will not update external tools automatically. Updating the
host installation is an explicit operational repair performed as part of this
task.

### Claude startup

Replace `--permission-mode bypassPermissions` in every active Claude command
template with the documented unattended flag
`--dangerously-skip-permissions`. Apply the change consistently to the default,
resume, and Light/Standard/Deep tier argument templates.

This avoids Claude Code's secure-sandbox requirement on native Windows while
retaining non-interactive automation. AutoDev's own prompt constraints,
working-directory boundary, risk assessment, guardrails, validation, and git
policies remain unchanged.

### Usage reporting

Centralize the fallback selection used when mapping a `ProviderInvocation` to a
`RunRecord`:

- When usage is present, preserve the provider-reported value exactly.
- When usage is absent and the invocation failed, store
  `No usage reported - provider invocation failed`.
- When usage is absent after a successful invocation, store
  `Unknown - provider did not report usage`.

The fallback is descriptive only. Structured token and cost fields remain
null unless the provider or Codex session fallback supplies real measurements.
Email, API, dashboard, logs, and sidecars continue consuming `RunRecord.Usage`,
so they receive the corrected message without separate presentation fixes.

## Data Flow

1. AutoDev selects a provider and invokes its configured CLI command.
2. The provider adapter parses outcome, output, session, tokens, cost, and model.
3. Codex may enrich missing token metrics from its local session rollout file.
4. Run orchestration copies structured metrics to the run record.
5. A small usage-reporting policy preserves real usage or selects the accurate
   missing-usage message based on invocation outcome.
6. Existing report, API, dashboard, and email paths display the stored value.

## Error Handling

- Provider startup failures remain provider failures and continue falling back
  according to the existing provider priority loop.
- Missing usage never changes run status or provider availability.
- An unavailable or failed Codex update is reported as an operational blocker;
  AutoDev will not silently downgrade the model.
- Claude command-template validation is covered by configuration tests to keep
  the incompatible permission mode from returning.

## Testing and Verification

Follow test-driven development for source changes:

1. Add a failing unit test proving failed invocations without metrics receive
   the invocation-failed message.
2. Add a failing unit test proving successful invocations without metrics use
   the provider-did-not-report message.
3. Preserve coverage showing provider-reported usage wins unchanged.
4. Add configuration assertions for all Claude command templates.
5. Implement the smallest policy/configuration changes needed to pass.
6. Run focused tests, then `dotnet test AutoDevRunner.sln` and
   `dotnet build AutoDevRunner.sln`.
7. Verify the upgraded Codex CLI and perform bounded provider smoke checks that
   confirm neither original startup error recurs and usage is parsed when the
   provider returns it.

## Alternatives Rejected

### Downgrade the Codex model and use Claude `acceptEdits`

This avoids updating Codex, but abandons the explicitly configured model and
can leave unattended Claude runs blocked on command permission prompts.

### Add runtime OS/version compatibility rewriting

AutoDev could inspect CLI versions and rewrite provider arguments dynamically.
That introduces hidden configuration behavior and long-term maintenance for a
host setup issue. Explicitly supported command templates and an operational CLI
update are simpler and easier to diagnose.

## Acceptance Criteria

- The installed Codex CLI accepts the configured `gpt-5.6-sol` invocation.
- Native-Windows Claude runs no longer fail with the secure-sandbox error.
- Provider-reported token/cost usage remains unchanged.
- Pre-model failures no longer claim that the provider cannot expose usage.
- Successful runs with genuinely absent metrics remain clearly marked unknown.
- Focused tests, the full test suite, and the solution build pass.
