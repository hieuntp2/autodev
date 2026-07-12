# GPT-5.6 Sol and Two-Hour Schedule Design

## Goal

Upgrade the high-capability Codex routes and the OpenAI creative planner to GPT-5.6 Sol, keep the lightweight Codex route on GPT-5.5, and reduce the default scheduler interval from five hours to two hours.

## Configuration Changes

- Set the Codex default, resume, Standard, and Deep command templates to model slug `gpt-5.6-sol`.
- Set the Codex Light command template to `gpt-5.5` and retain `model_reasoning_effort=low`.
- Preserve the existing Standard and Deep reasoning efforts (`medium` and `high`).
- Set `AutoDev:Planner:Model` to `gpt-5.6-sol`. The planner continues to use the Responses API without adding Pro mode or Max reasoning.
- Set `AutoDev:Scheduler:IntervalHours` to `2`.
- Set the PowerShell installer's default `IntervalHours` to `2` and update its parameter documentation accordingly.
- Preserve all unrelated local configuration and source changes.

## Runtime Behavior

Model routing remains unchanged. Only the configured model slugs change: lightweight maintenance uses GPT-5.5, while ordinary and difficult Codex work uses GPT-5.6 Sol. Planner calls use GPT-5.6 Sol. Both the in-process scheduler and newly installed Windows Scheduled Tasks default to a two-hour cadence.

## Validation

Add focused tests that load the application configuration and assert the Codex command templates, planner model, and scheduler interval. Verify the installer default and its documentation. Run the focused tests, then the complete solution test suite.

## Non-Goals

- Do not change Claude models or provider behavior.
- Do not enable planner Pro mode, Max reasoning, multi-agent behavior, or new API parameters.
- Do not change per-project `MaxRunMinutes`, Windows task execution limits, quota windows, or existing installed tasks automatically.
- Do not modify the OpenAI planner prompt or response parsing.
