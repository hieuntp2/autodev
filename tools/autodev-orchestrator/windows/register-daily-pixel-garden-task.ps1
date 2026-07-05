# Registers (or updates) a Windows Task Scheduler task that runs the Daily Pixel Garden
# creative loop once per day. Runs as the current user, interactive logon only (no admin,
# no stored password) — the same pattern the main AutoDevRunner installer uses, so the
# implementer CLIs (Claude/Codex) can reuse your logged-in sessions.
[CmdletBinding()]
param(
    [string]$TaskName = "DailyPixelGarden-AutoDev",
    # Daily trigger time, e.g. "09:00"
    [string]$DailyTime = "09:00",
    [switch]$Unregister
)

$ErrorActionPreference = "Stop"

if ($Unregister) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Scheduled task '$TaskName' removed (if it existed)."
    return
}

$RunScript = Join-Path $PSScriptRoot "run-autodev-once.ps1"
if (-not (Test-Path $RunScript)) { throw "run-autodev-once.ps1 not found next to this script." }
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path

$Action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$RunScript`" -Command run" `
    -WorkingDirectory $RepoRoot

$Trigger = New-ScheduledTaskTrigger -Daily -At $DailyTime

$Settings = New-ScheduledTaskSettingsSet -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
    -MultipleInstances IgnoreNew

$Principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger `
    -Settings $Settings -Principal $Principal -Force | Out-Null

Write-Host "Scheduled task '$TaskName' registered: daily at $DailyTime, working dir '$RepoRoot'."
Write-Host "Logs: docs\autodev\logs\autodev-YYYY-MM-DD.log"
Write-Host "Run it now with: Start-ScheduledTask -TaskName $TaskName"
