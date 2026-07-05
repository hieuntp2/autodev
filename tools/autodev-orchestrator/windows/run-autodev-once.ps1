# Runs the Daily Pixel Garden orchestrator once and appends output to a dated log file.
# Used both manually and by the scheduled task registered via register-daily-pixel-garden-task.ps1.
[CmdletBinding()]
param(
    # Orchestrator command: status | report | plan | run
    [string]$Command = "run"
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$ToolProject = Join-Path $RepoRoot "tools\autodev-orchestrator"
$LogDir = Join-Path $RepoRoot "docs\autodev\logs"
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$LogFile = Join-Path $LogDir ("autodev-" + (Get-Date -Format "yyyy-MM-dd") + ".log")

# Load repo-root .env into the process environment (KEY=VALUE lines, # comments allowed).
# The orchestrator also reads .env itself; this keeps env available to any child process too.
$EnvFile = Join-Path $RepoRoot ".env"
if (Test-Path $EnvFile) {
    foreach ($line in Get-Content $EnvFile) {
        $trimmed = $line.Trim()
        if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }
        $idx = $trimmed.IndexOf("=")
        if ($idx -lt 1) { continue }
        $name = $trimmed.Substring(0, $idx).Trim()
        $value = $trimmed.Substring($idx + 1).Trim().Trim('"').Trim("'")
        [Environment]::SetEnvironmentVariable($name, $value, "Process")
    }
}

"=== autodev-orchestrator $Command @ $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Add-Content $LogFile

Push-Location $RepoRoot
try {
    $output = & dotnet run -c Release --project $ToolProject -- $Command 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

$output | Add-Content $LogFile
"--- exit code: $exitCode ---`n" | Add-Content $LogFile
$output | Write-Output

exit $exitCode
