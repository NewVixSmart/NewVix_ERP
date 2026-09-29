<#
.SYNOPSIS
    Registers (or unregisters) a Windows Scheduled Task that runs the daily
    NewVixSmartDb backup script.

.DESCRIPTION
    Creates a scheduled task named "NewVixSmartDailyBackup" that runs
    scripts\backup-db.ps1 daily at the given time as the current user.

    LOCAL DEVELOPMENT ONLY. backup-db.ps1 is hard-wired to LocalDB + Windows
    integrated authentication, so this task backs up the developer's local
    database and nothing else. The Docker Compose database is NOT covered by it -
    schedule backup-db-container.ps1 (or run the "backup" compose profile) for
    that, and see the backup section of README.md.

    Use -Unregister to remove the task.

.PARAMETER Time
    Daily start time (HH:mm). Default: 02:00

.PARAMETER RunWhetherLoggedOn
    If set, the task runs whether the user is logged on or not
    (uses stored credentials). Otherwise the task runs only when the
    user is logged on.

.PARAMETER Unregister
    Removes the "NewVixSmartDailyBackup" task if it exists.

.EXAMPLE
    .\scripts\setup-backup-task.ps1

.EXAMPLE
    .\scripts\setup-backup-task.ps1 -Time 03:30

.EXAMPLE
    .\scripts\setup-backup-task.ps1 -Time 02:00 -RunWhetherLoggedOn

.EXAMPLE
    .\scripts\setup-backup-task.ps1 -Unregister
#>

[CmdletBinding()]
param(
    [string]$Time = '02:00',

    [switch]$RunWhetherLoggedOn,

    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'

$TaskName = 'NewVixSmartDailyBackup'

# Repo root = the folder two levels up from this script (scripts\ -> repo root).
$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$BackupScript = Join-Path $RepoRoot 'scripts\backup-db.ps1'
$PowerShellExe = (Get-Command powershell.exe -ErrorAction SilentlyContinue).Source
if (-not $PowerShellExe) {
    $PowerShellExe = (Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
}
if (-not $PowerShellExe) {
    Write-Host '[ FAIL] Could not locate PowerShell (powershell.exe / pwsh.exe).' -ForegroundColor Red
    exit 1
}

function Unregister-BackupTask {
    try {
        $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($existing) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-Host "[  OK ] Scheduled task '$TaskName' unregistered." -ForegroundColor Green
        }
        else {
            Write-Host "[ INFO] Scheduled task '$TaskName' does not exist; nothing to unregister." -ForegroundColor Cyan
        }
        exit 0
    }
    catch {
        Write-Host "[ FAIL] Failed to unregister task: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

if ($Unregister) {
    Unregister-BackupTask
}

try {
    # Validate the time string.
    try {
        $null = [datetime]::ParseExact($Time, 'H:mm', $null)
    }
    catch {
        try { $null = [datetime]::ParseExact($Time, 'HH:mm', $null) }
        catch { throw "Invalid -Time value '$Time'. Use 24-hour HH:mm (e.g. 02:00)." }
    }

    if (-not (Test-Path -LiteralPath $BackupScript)) {
        throw "Backup script not found at: $BackupScript"
    }

    # Build the command that starts PowerShell and runs the backup script.
    $ScriptArgs = '& "' + $BackupScript + '"'
    $Argument = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"$ScriptArgs`""

    # Register the task.
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    if ($RunWhetherLoggedOn) {
        $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType S4U -RunLevel Limited
    }

    $action   = New-ScheduledTaskAction -Execute $PowerShellExe -Argument $Argument
    $trigger  = New-ScheduledTaskTrigger -Daily -At $Time

    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Description "Daily LOCALDB backup of NewVixSmartDb via scripts\backup-db.ps1 (does NOT cover the Docker Compose database - use scripts\backup-db-container.ps1)" `
        -Force | Out-Null

    Write-Host "[  OK ] Scheduled task '$TaskName' registered." -ForegroundColor Green
    Write-Host "[ INFO] Runs daily at $Time as user '$env:USERNAME'." -ForegroundColor Cyan
    if ($RunWhetherLoggedOn) {
        Write-Host '[ INFO] Run mode: whether the user is logged on or not.' -ForegroundColor Cyan
    }
    else {
        Write-Host '[ INFO] Run mode: only when the user is logged on.' -ForegroundColor Cyan
    }
    Write-Host "[ INFO] To inspect: Get-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Cyan
    Write-Host "[ INFO] To remove  : .\scripts\setup-backup-task.ps1 -Unregister" -ForegroundColor Cyan

    exit 0
}
catch {
    Write-Host "[ FAIL] Failed to register task: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
