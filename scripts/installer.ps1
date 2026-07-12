<#
.SYNOPSIS
    Builds AutoDev Runner and installs Windows Scheduled Tasks for it.

.DESCRIPTION
    Creates two scheduled tasks (run as the current user, only when logged on,
    so the Codex/Claude CLIs use your existing login session):

      AutoDevRunner-Run        -> runs "AutoDevRunner.exe --run-due" every N hours.
      AutoDevRunner-Dashboard  -> starts the web dashboard/API at logon (continuous).

    Run this from an elevated (Administrator) PowerShell prompt.

.PARAMETER IntervalHours
    How often the run task fires. Default 2.

.PARAMETER SkipBuild
    Reuse an existing .\publish folder instead of rebuilding.

.PARAMETER NoDashboard
    Do not create the dashboard task (only the periodic run task).

.EXAMPLE
    .\scripts\installer.ps1
    .\scripts\installer.ps1 -IntervalHours 8
    .\scripts\installer.ps1 -SkipBuild -NoDashboard
#>
[CmdletBinding()]
param(
    [double]$IntervalHours = 2,
    [string]$PublishDir = "$PSScriptRoot\..\publish",
    [switch]$SkipBuild,
    [switch]$NoDashboard
)

$ErrorActionPreference = "Stop"

$RunTaskName  = "AutoDevRunner-Run"
$DashTaskName = "AutoDevRunner-Dashboard"
$proj = Join-Path $PSScriptRoot "..\src\AutoDevRunner\AutoDevRunner.csproj"

# --- 0. Force-stop any running instance so publish files / DB aren't locked ---
#     The dashboard task keeps AutoDevRunner.exe running out of the publish folder;
#     without this, `dotnet publish` fails with a file-in-use error.
function Stop-AutoDev {
    foreach ($name in @($RunTaskName, $DashTaskName)) {
        if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
            try { Stop-ScheduledTask -TaskName $name -ErrorAction Stop } catch {}
        }
    }
    $procs = Get-Process -Name "AutoDevRunner" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Host "Force-stopping running AutoDevRunner process(es)..." -ForegroundColor Yellow
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        # Wait (bounded) for file handles to release before we overwrite the exe.
        for ($i = 0; $i -lt 20 -and (Get-Process -Name "AutoDevRunner" -ErrorAction SilentlyContinue); $i++) {
            Start-Sleep -Milliseconds 250
        }
    }
}
Stop-AutoDev

# --- 1. Build / publish (framework-dependent; requires .NET 8 runtime present) ---
if (-not $SkipBuild) {
    Write-Host "Publishing AutoDev Runner (Release, win-x64)..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -r win-x64 --self-contained false -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

$exe = Join-Path (Resolve-Path $PublishDir) "AutoDevRunner.exe"
if (-not (Test-Path $exe)) { throw "Executable not found: $exe. Run without -SkipBuild first." }
$workDir = Split-Path $exe -Parent

# --- 2. Task settings + principal (current user, run only when logged on) ---
$user = "$env:USERDOMAIN\$env:USERNAME"
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
# Run task: bounded (a stuck run must not block the next firing forever).
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
             -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 6)
# Dashboard task: long-lived. ExecutionTimeLimit 0 = no limit (a 6h limit here
# would make Task Scheduler kill the dashboard 6h after logon); restart it
# automatically if it ever crashes.
$dashSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Seconds 0) `
                -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

function Register-Task($name, $action, $trigger, $desc, $taskSettings) {
    if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
        Write-Host "Removing existing task '$name'..." -ForegroundColor Yellow
        Unregister-ScheduledTask -TaskName $name -Confirm:$false
    }
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger `
        -Principal $principal -Settings $taskSettings -Description $desc | Out-Null
    Write-Host "Installed scheduled task '$name'." -ForegroundColor Green
}

# --- 3. Periodic run task ---
$runAction = New-ScheduledTaskAction -Execute $exe -Argument "--run-due" -WorkingDirectory $workDir
$runTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) `
              -RepetitionInterval (New-TimeSpan -Hours $IntervalHours)
Register-Task $RunTaskName $runAction $runTrigger `
    "AutoDev Runner: run all due projects via Codex/Claude every $IntervalHours hour(s)." $settings

# --- 4. Dashboard task (optional) ---
if (-not $NoDashboard) {
    $dashAction = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $workDir
    $dashTrigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    Register-Task $DashTaskName $dashAction $dashTrigger `
        "AutoDev Runner: local web dashboard/API (http://localhost:5099)." $dashSettings
    Write-Host "`nStarting dashboard now..." -ForegroundColor Cyan
    Start-ScheduledTask -TaskName $DashTaskName
}

Write-Host "`nDone." -ForegroundColor Green
Write-Host "Run task fires every $IntervalHours h. Dashboard: http://localhost:5099" -ForegroundColor Green
Write-Host "Trigger a run now:  Start-ScheduledTask -TaskName $RunTaskName" -ForegroundColor DarkGray
