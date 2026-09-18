<#
.SYNOPSIS
    Creates a SQL Server database backup using sqlcmd and applies retention.

.DESCRIPTION
    Takes a full BACKUP DATABASE ... TO DISK for the supplied instance and
    database, then removes .bak files older than the retention window.

    sqlcmd discovery order:
      1. sqlcmd found on PATH
      2. $SqlCmd variable (optional, can point directly to sqlcmd.exe)
      3. Common SQL Server Tools / SQLCMD install locations
    If none is found the script fails gracefully with guidance to install
    "SQL Server Management Tools" / "SQLCMD Command Line Utilities".

.PARAMETER Instance
    SQL Server instance to back up from. Default: (localdb)\MSSQLLocalDB

.PARAMETER Database
    Database name to back up. Default: NewVixSmartDb

.PARAMETER BackupDir
    Folder where backups are written. Default: "<RepoRoot>\backups"

.PARAMETER RetainDays
    Number of days of backups to keep. Older .bak files are deleted. Default: 14

.EXAMPLE
    .\scripts\backup-db.ps1

.EXAMPLE
    .\scripts\backup-db.ps1 -Database NewVixSmartDb -BackupDir C:\Backups -RetainDays 7
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Instance = '(localdb)\MSSQLLocalDB',

    [Parameter(Position = 1)]
    [string]$Database = 'NewVixSmartDb',

    [string]$BackupDir,

    [int]$RetainDays = 14,

    # Explicit path to sqlcmd.exe if you want to bypass auto-detection.
    [string]$SqlCmd = ''
)

$ErrorActionPreference = 'Stop'

function Write-Info  { Write-Host "[INFO ] $args" -ForegroundColor Cyan }
function Write-Ok    { Write-Host "[  OK ] $args" -ForegroundColor Green }
function Write-Fail  { Write-Host "[ FAIL] $args" -ForegroundColor Red }
function Write-Warn  { Write-Host "[ WARN] $args" -ForegroundColor Yellow }

# Repo root = the folder two levels up from this script (scripts\ -> repo root).
$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    $BackupDir = Join-Path $RepoRoot 'backups'
}

try {
    if ($RetainDays -lt 1) {
        throw "RetainDays ($RetainDays) must be at least 1."
    }

    # ---- Locate sqlcmd -----------------------------------------------------
    $sqlcmdPath = $null

    if (-not [string]::IsNullOrWhiteSpace($SqlCmd)) {
        if (Test-Path -LiteralPath $SqlCmd) {
            $sqlcmdPath = $SqlCmd
        }
        else {
            throw "Provided -SqlCmd path does not exist: $SqlCmd"
        }
    }

    if (-not $sqlcmdPath) {
        $cmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
        if ($cmd) {
            $sqlcmdPath = $cmd.Source
        }
    }

    if (-not $sqlcmdPath) {
        # Common install locations for SQL Server Tools / SQLCMD standalone.
        $candidates = @(
            "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\*\",  # expanded below
            "$env:ProgramFiles\Microsoft SQL Server\*\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles(x86)\Microsoft SQL Server\*\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles\Microsoft SQL Server\150\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles\Microsoft SQL Server\160\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles\Microsoft SQL Server\170\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\\Tools\Binn\sqlcmd.exe",
            "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\*.exe"
        )
        foreach ($pattern in $candidates) {
            $hit = Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.Name -ieq 'sqlcmd.exe' } |
                   Select-Object -First 1
            if ($hit) {
                $sqlcmdPath = $hit.FullName
                break
            }
        }
    }

    if (-not $sqlcmdPath) {
        throw @"
sqlcmd.exe was not found on PATH or in common SQL Server Tools locations.

To fix this, install "SQL Server Management Tools" / "SQL Server Command Line
Utilities (sqlcmd)" from:
    https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility
... or point this script at the executable by providing:
    -.\scripts\backup-db.ps1 -SqlCmd "C:\path\to\sqlcmd.exe"
"@
    }

    Write-Info "Using sqlcmd: $sqlcmdPath"

    # ---- Ensure backup dir exists ------------------------------------------
    if (-not (Test-Path -LiteralPath $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
        Write-Info "Created backup directory: $BackupDir"
    }

    # ---- Build backup filename & run BACKUP --------------------------------
    $stamp      = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backupFile = Join-Path $BackupDir ($Database + '_' + $stamp + '.bak')

    $backupQuery = "BACKUP DATABASE [$Database] TO DISK = N'$backupFile' WITH INIT, FORMAT"

    Write-Info "Backing up database '$Database' on '$Instance'..."
    Write-Info "Target: $backupFile"

    & $sqlcmdPath -S $Instance -E -Q $backupQuery -b
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed (exit code $LASTEXITCODE) while running BACKUP DATABASE."
    }

    if (-not (Test-Path -LiteralPath $backupFile)) {
        throw "Backup command reported success but file was not found: $backupFile"
    }

    $sizeMB = [math]::Round((Get-Item -LiteralPath $backupFile).Length / 1MB, 2)
    Write-Ok "Backup created: $backupFile ($sizeMB MB)"

    # ---- Retention: delete .bak older than RetainDays ----------------------
    $cutoff     = (Get-Date).AddDays(-$RetainDays)
    $oldBackups = Get-ChildItem -LiteralPath $BackupDir -Filter '*.bak' -File -ErrorAction SilentlyContinue |
                  Where-Object { $_.LastWriteTime -lt $cutoff }

    foreach ($old in $oldBackups) {
        Remove-Item -LiteralPath $old.FullName -Force
        Write-Info "Deleted old backup (retention >$RetainDays days): $($old.Name)"
    }

    $remaining = (Get-ChildItem -LiteralPath $BackupDir -Filter '*.bak' -File -ErrorAction SilentlyContinue).Count
    Write-Ok "Retention applied. $($oldBackups.Count) removed, $remaining backup(s) remain."

    Write-Ok "Backup completed successfully."
    exit 0
}
catch {
    Write-Fail "Backup FAILED: $($_.Exception.Message)"
    exit 1
}
