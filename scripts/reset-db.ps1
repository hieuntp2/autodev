<#
.SYNOPSIS
    Resets the AutoDevRunner PostgreSQL database.

.DESCRIPTION
    Reads the "Postgres" connection string from src/AutoDevRunner/appsettings.json
    (and appsettings.Local.json if present, which overrides), then either:

      -Mode Drop      Drop and recreate the whole database (full reset). Default.
      -Mode Truncate  Keep the database; wipe all rows and reset identity counters.

    The schema is recreated automatically the next time the app starts, because
    Program.cs calls db.Database.EnsureCreatedAsync().

    NOTE: This only clears the SQL database (Projects, Runs, ProviderStates).
    Per-project run history under each target repo's .ai-runner/ folder is NOT
    touched. Pass -WipeAiRunner <repoPath> to also clear that folder.

.EXAMPLE
    ./scripts/reset-db.ps1                 # full reset (asks for confirmation)
    ./scripts/reset-db.ps1 -Mode Truncate  # wipe rows only
    ./scripts/reset-db.ps1 -Yes            # skip the confirmation prompt
#>
[CmdletBinding()]
param(
    [ValidateSet('Drop', 'Truncate')]
    [string]$Mode = 'Drop',
    [string]$WipeAiRunner,
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appDir   = Join-Path $repoRoot 'src/AutoDevRunner'

function Get-ConnString {
    $cs = $null
    foreach ($file in @('appsettings.json', 'appsettings.Local.json')) {
        $path = Join-Path $appDir $file
        if (Test-Path $path) {
            $json = Get-Content $path -Raw | ConvertFrom-Json
            if ($json.ConnectionStrings -and $json.ConnectionStrings.Postgres) {
                $cs = $json.ConnectionStrings.Postgres  # later file overrides
            }
        }
    }
    if (-not $cs) { throw "No ConnectionStrings:Postgres found in appsettings(.Local).json" }
    return $cs
}

function Parse-ConnString([string]$cs) {
    $map = @{}
    foreach ($part in $cs.Split(';')) {
        if ($part -match '^\s*([^=]+)=(.*)$') {
            $map[$matches[1].Trim().ToLower()] = $matches[2].Trim()
        }
    }
    [pscustomobject]@{
        Host     = if ($map['host']) { $map['host'] } else { 'localhost' }
        Port     = if ($map['port']) { $map['port'] } else { '5432' }
        Database = $map['database']
        Username = if ($map['username']) { $map['username'] } else { $map['user id'] }
        Password = $map['password']
    }
}

function Find-Psql {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending
    if ($candidates) { return $candidates[0].FullName }
    throw "psql.exe not found. Install PostgreSQL client tools or add psql to PATH."
}

$conn = Parse-ConnString (Get-ConnString)
$psql = Find-Psql
$env:PGPASSWORD = $conn.Password

Write-Host "Target: $($conn.Username)@$($conn.Host):$($conn.Port)/$($conn.Database)  (mode: $Mode)" -ForegroundColor Cyan

if (-not $Yes) {
    $answer = Read-Host "This will ERASE all AutoDev data. Type 'yes' to continue"
    if ($answer -ne 'yes') { Write-Host 'Aborted.'; return }
}

function Invoke-Psql([string]$db, [string]$sql) {
    & $psql -h $conn.Host -p $conn.Port -U $conn.Username -d $db -v ON_ERROR_STOP=1 -c $sql
    if ($LASTEXITCODE -ne 0) { throw "psql failed (exit $LASTEXITCODE)" }
}

if ($Mode -eq 'Drop') {
    # Connect to the maintenance DB to drop/recreate the target.
    $db = $conn.Database
    $terminate = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$db' AND pid <> pg_backend_pid();"
    Invoke-Psql 'postgres' $terminate
    Invoke-Psql 'postgres' "DROP DATABASE IF EXISTS ""$db"";"
    Invoke-Psql 'postgres' "CREATE DATABASE ""$db"";"
    Write-Host "Database '$db' dropped and recreated (empty)." -ForegroundColor Green
}
else {
    # Wipe rows but keep the schema; reset identity sequences.
    $sql = 'TRUNCATE TABLE "Runs", "Projects", "ProviderStates" RESTART IDENTITY CASCADE;'
    Invoke-Psql $conn.Database $sql
    Write-Host "All rows cleared and identities reset." -ForegroundColor Green
}

if ($WipeAiRunner) {
    $aiDir = Join-Path $WipeAiRunner '.ai-runner'
    if (Test-Path $aiDir) {
        Remove-Item $aiDir -Recurse -Force
        Write-Host "Removed $aiDir" -ForegroundColor Green
    }
    else {
        Write-Host "No .ai-runner folder at $aiDir (skipped)." -ForegroundColor Yellow
    }
}

Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
Write-Host "Done. The schema is re-created on next app start (EF EnsureCreated)." -ForegroundColor Cyan
