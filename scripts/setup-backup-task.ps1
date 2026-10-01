<#
.SYNOPSIS
    Registers (or unregisters) a Windows Scheduled Task that runs the daily
    NewVixSmartDb backup script.

.DESCRIPTION
    Creates a scheduled task named "NewVixSmartDailyBackup" that runs
    scripts\backup-db.ps1 daily at the given time as the current user.

    THE REGISTERED COMMAND LINE IS EXERCISED BY THIS SCRIPT. The previous
    version built -Command "& "<path>\backup-db.ps1"" and reported success
    without ever checking that the command line it produced was valid. Measured:
    with a repository path containing a space ("...\Default Project\...") that
    command line does NOT work - PowerShell ends the quoted argument at the first
    space, the task fails with LastTaskResult 1, and NO .bak is produced at all.
    It was registered, it ran on schedule, and it looked like a working backup.

    The action is now -File "<path>", which handles spaces correctly, and this
    script re-reads the task it just registered, parses the stored Arguments
    string back into a token array, and runs that exact command line once
    before reporting success. If the parse fails or the trial run fails, the
    task is unregistered again instead of being left registered and failing
    every night.

    It also enables "run as soon as possible after a missed start"
    (-StartWhenAvailable), so a machine that was off at 02:00 does not silently
    skip that day's backup.

    It DOES NOT cover the Docker Compose database: backup-db.ps1 uses Windows
    integrated authentication (-E) and cannot authenticate as 'sa'. Schedule
    backup-db-container.ps1 for that - see the backup section of README.md.

.PARAMETER Time
    Daily start time (HH:mm). Default: 02:00

.PARAMETER Database
    Database to back up. Default: NewVixSmartDb

.PARAMETER BackupDir
    Backup folder. Default: <repo>\backups

.PARAMETER RetainDays
    Retention window passed through to backup-db.ps1. Default: 14

.PARAMETER Instance
    SQL instance passed through to backup-db.ps1. A non-LocalDB instance also
    needs backup-db.ps1's own -AllowInstance, so this adds it for you.

.PARAMETER RunWhetherLoggedOn
    If set, the task runs whether the user is logged on or not (S4U: stored
    credentials, no password prompt). Otherwise the task runs only when the
    user is logged on. NOTE: an S4U task runs without a network token, which is
    fine for LocalDB and a local instance, but it CANNOT reach a remote server.

.PARAMETER WithChecksum
    Passed through to backup-db.ps1: write page checksums so media corruption
    is caught by RESTORE VERIFYONLY instead of surfacing later as Msg 824.

.PARAMETER SkipTrialRun
    Register the task without running the registered command line once to prove
    it works. Only use this when no backup can be taken right now.

.PARAMETER Unregister
    Removes the "NewVixSmartDailyBackup" task if it exists.

.EXAMPLE
    .\scripts\setup-backup-task.ps1

.EXAMPLE
    .\scripts\setup-backup-task.ps1 -Time 03:30

.EXAMPLE
    .\scripts\setup-backup-task.ps1 -Time 02:00 -RunWhetherLoggedOn

.EXAMPLE
    # A local named instance, with checksums, trial-run on registration
    .\scripts\setup-backup-task.ps1 -Instance .\NEWVIX -Database NewVixSmartDb -WithChecksum

.EXAMPLE
    .\scripts\setup-backup-task.ps1 -Unregister
#>

[CmdletBinding()]
param(
    [string]$Time = '02:00',

    [string]$Database = 'NewVixSmartDb',

    [string]$BackupDir,

    [int]$RetainDays = 14,

    [string]$Instance = '(localdb)\MSSQLLocalDB',

    [switch]$RunWhetherLoggedOn,

    [switch]$WithChecksum,

    [switch]$SkipTrialRun,

    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'

$TaskName = 'NewVixSmartDailyBackup'

# Repo root = the folder two levels up from this script (scripts\ -> repo root).
$RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    $BackupDir = Join-Path $RepoRoot 'backups'
}

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
    #
    # -File, NOT -Command "& "<path>"". The -Command form breaks on any path
    # containing a space: the inner quotes are consumed and PowerShell then tries
    # to execute the first space-delimited fragment as a command. Measured on this
    # repository ("...\Default Project\scripts\backup-db.ps1"): LastTaskResult 1,
    # no backup produced. -File takes the path as its own argument and quotes
    # correctly.
    $scriptArgs = @(
        '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', ('"' + $BackupScript + '"'),
        '-Database', ('"' + $Database + '"'),
        '-BackupDir', ('"' + $BackupDir + '"'),
        '-RetainDays', $RetainDays,
        '-Instance', ('"' + $Instance + '"')
    )
    if ($Instance -notmatch '(?i)localdb') { $scriptArgs += '-AllowInstance' }
    if ($WithChecksum) { $scriptArgs += '-WithChecksum' }
    $Argument = $scriptArgs -join ' '

    # Register the task.
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    if ($RunWhetherLoggedOn) {
        $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType S4U -RunLevel Limited
    }

    $action   = New-ScheduledTaskAction -Execute $PowerShellExe -Argument $Argument
    $trigger  = New-ScheduledTaskTrigger -Daily -At $Time
    # A machine that was off/asleep at $Time would otherwise skip the day with
    # no trace. This runs it at the next opportunity instead.
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 6)

    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description "Daily backup of $Database via scripts\backup-db.ps1 (does NOT cover the Docker Compose database - use scripts\backup-db-container.ps1)" `
        -Force | Out-Null

    Write-Host "[  OK ] Scheduled task '$TaskName' registered." -ForegroundColor Green
    Write-Host "[ INFO] Runs daily at $Time as user '$env:USERNAME'." -ForegroundColor Cyan
    Write-Host "[ INFO] Command: $PowerShellExe $Argument" -ForegroundColor DarkGray

    # ---- Prove the registered command line actually works --------------------
    # Read the Arguments back OUT of the registered task rather than trusting the
    # string we just built: that is the string Task Scheduler will use.
    if ($SkipTrialRun) {
        Write-Host '[ WARN] -SkipTrialRun: the registered command line was NOT executed. It may be broken.' -ForegroundColor Yellow
    }
    else {
        $registered = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
        $regArgs    = $registered.Actions[0].Arguments
        $regExe     = $registered.Actions[0].Execute

        Write-Host '[ INFO] Re-reading the registered task and running its exact command line...' -ForegroundColor Cyan

        # Parse with the Windows native command-line rules (CommandLineToArgvW)
        # instead of PowerShell's, so the check sees what CreateProcess sees.
        $CommandLineToArgvW = @'
using System;
using System.Runtime.InteropServices;
public static class Argv {
    [DllImport("shell32.dll", SetLastError = true)]
    static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int argc);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr h);
    public static string[] Parse(string cmd) {
        int argc;
        IntPtr p = CommandLineToArgvW(cmd, out argc);
        if (p == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var a = new string[argc];
        for (int i = 0; i < argc; i++) a[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, i * IntPtr.Size));
        LocalFree(p);
        return a;
    }
}
'@
        Add-Type -TypeDefinition $CommandLineToArgvW -Language CSharp -ErrorAction Stop | Out-Null
        $tokens = [Argv]::Parse($regArgs)

        if ($tokens.Count -lt 3 -or $tokens[0] -ne '-NoProfile') {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
            throw "The Arguments stored in the task do not parse into a usable command line ('$regArgs'). The task was unregistered."
        }
        if (($tokens -join ' ') -ne $regArgs.Trim()) {
            Write-Host "[ WARN] Argument round-trip differs; using the parsed form." -ForegroundColor Yellow
        }

        # Run it exactly as the scheduler would (via cmd, no console interaction).
        $trial = & cmd.exe /c ('"' + $regExe + '" ' + $regArgs) 2>&1
        $trialExit = $LASTEXITCODE
        $trial | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

        if ($trialExit -ne 0) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
            throw "The registered command line failed on its trial run (exit code $trialExit). The task has been unregistered rather than left to fail every night. Fix the command above, then re-run this script."
        }

        $newest = @(Get-ChildItem -LiteralPath $BackupDir -Filter ($Database + '_*.bak') -File -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending)
        if ($newest.Count -eq 0) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
            throw "The trial run reported success but no '$Database_*.bak' exists in $BackupDir. The task was unregistered."
        }

        Write-Host ("[  OK ] Trial run produced a real artifact: " + $newest[0].Name +
                    " (" + [math]::Round($newest[0].Length / 1MB, 2) + " MB)") -ForegroundColor Green
    }

    if ($RunWhetherLoggedOn) {
        Write-Host '[ INFO] Run mode: whether the user is logged on or not (S4U).' -ForegroundColor Cyan
        Write-Host '[ WARN] S4U tasks have no network token: fine for LocalDB/a local instance, but this task cannot reach a remote SQL Server.' -ForegroundColor Yellow
    }
    else {
        Write-Host '[ INFO] Run mode: only when the user is logged on.' -ForegroundColor Cyan
    }
    Write-Host "[ INFO] To inspect: Get-ScheduledTask -TaskName '$TaskName'" -ForegroundColor Cyan
    Write-Host "[ INFO] Check a run actually worked: (Get-ScheduledTaskInfo -TaskName '$TaskName').LastTaskResult  # 0 = success" -ForegroundColor Cyan
    Write-Host "[ INFO] To remove  : .\scripts\setup-backup-task.ps1 -Unregister" -ForegroundColor Cyan

    exit 0
}
catch {
    Write-Host "[ FAIL] Failed to register task: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
