<#
.SYNOPSIS
    Removes the AutoDev Runner scheduled tasks.

.DESCRIPTION
    Stops and unregisters AutoDevRunner-Run and AutoDevRunner-Dashboard.
    Run from an elevated (Administrator) PowerShell prompt.
    The ./publish folder and the PostgreSQL database are left untouched.

.EXAMPLE
    .\scripts\uninstaller.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$tasks = @("AutoDevRunner-Run", "AutoDevRunner-Dashboard")

foreach ($name in $tasks) {
    $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    if (-not $task) {
        Write-Host "Task '$name' not found." -ForegroundColor Yellow
        continue
    }
    try { Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue } catch {}
    Unregister-ScheduledTask -TaskName $name -Confirm:$false
    Write-Host "Removed scheduled task '$name'." -ForegroundColor Green
}

# Best-effort: stop a running dashboard process.
Get-Process -Name "AutoDevRunner" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping running AutoDevRunner process (PID $($_.Id))..." -ForegroundColor Yellow
    try { Stop-Process -Id $_.Id -Force } catch {}
}

Write-Host "Done." -ForegroundColor Green
