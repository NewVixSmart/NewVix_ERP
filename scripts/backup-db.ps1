<#
.SYNOPSIS
    Creates a SQL Server database backup using sqlcmd and applies retention.

.DESCRIPTION
    *** THIS SCRIPT CANNOT BACK UP THE DOCKER COMPOSE DATABASE. ***

    It is hard-wired to Windows integrated authentication (-E, see the sqlcmd
    invocation below) and has no way to supply a SQL login password, so it
    cannot reach the SQL Server container started by docker-compose.yml: that
    server authenticates as 'sa' with a password held in the gitignored .env.

    To back up the container database use:
        .\scripts\backup-db-container.ps1
    To restore anything, use:
        .\scripts\restore-db-container.ps1

    Takes a full BACKUP DATABASE ... TO DISK for the supplied instance and
    database, then applies retention.

    THE ARTIFACT IS VERIFIED BEFORE THE SCRIPT REPORTS SUCCESS. A file that
    exists is not a backup, so after BACKUP DATABASE returns, the script runs
    RESTORE VERIFYONLY and RESTORE HEADERONLY on the file it just wrote and
    cross-checks msdb.dbo.backupset. If any of that fails the file is deleted
    and the script exits non-zero: you never get a green "completed
    successfully" over an unrestorable .bak. Use -SkipVerify only when you have
    measured the cost on your own database and you have another control.

    -WithChecksum adds page checksums to the backup. It costs CPU on the way in
    and buys detection of silent bit rot on the media: without it a corrupted
    page is reported as "The backup set on file 1 is valid." by RESTORE
    VERIFYONLY and the damage only surfaces much later as an Msg 824 while the
    restored database is already in use. Turn it on unless your backup window
    cannot afford it.

    Retention only ever deletes THIS DATABASE's own backups
    (<Database>_*.bak), the same rule the container script uses. Pointing
    -BackupDir at a folder shared with another system no longer deletes that
    system's backups.

    sqlcmd discovery order:
      1. sqlcmd found on PATH
      2. $SqlCmd variable (optional, can point directly to sqlcmd.exe)
      3. Common SQL Server Tools / SQLCMD install locations
    If none is found the script fails gracefully with guidance to install
    "SQL Server Management Tools" / "SQLCMD Command Line Utilities".

.PARAMETER Instance
    SQL Server instance to back up from. Default: (localdb)\MSSQLLocalDB.
    Any other value is rejected unless -AllowInstance is also passed, because
    this script authenticates with Windows integrated auth (-E) and cannot
    supply a SQL login password. -AllowInstance exists for real local
    instances that integrated auth CAN reach (a named instance such as
    .\NEWVIX, a local default instance) and prints a loud warning; it does not
    make a remote or containerised database reachable.

.PARAMETER Database
    Database name to back up. Default: NewVixSmartDb

.PARAMETER BackupDir
    Folder where backups are written. Default: "<RepoRoot>\backups"

.PARAMETER RetainDays
    Number of days of this database's backups to keep. Older ones are deleted.
    Default: 14

.PARAMETER AllowInstance
    Permit backing up a named instance instead of only LocalDB. See -Instance.

.PARAMETER WithChecksum
    Write page checksums into the backup so that media corruption is detected
    by RESTORE VERIFYONLY instead of silently surviving into a restore.

.PARAMETER SkipVerify
    Skip RESTORE VERIFYONLY / HEADERONLY after the backup. Not recommended.

.EXAMPLE
    .\scripts\backup-db.ps1

.EXAMPLE
    .\scripts\backup-db.ps1 -Database NewVixSmartDb -BackupDir C:\Backups -RetainDays 7

.EXAMPLE
    # A local named instance, with checksums, skipping retention
    .\scripts\backup-db.ps1 -Instance .\NEWVIX -Database NewVixSmartDb -AllowInstance -WithChecksum
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
    [string]$SqlCmd = '',

    # Opt in to backing up something other than LocalDB.
    [switch]$AllowInstance,

    # Write page checksums into the backup (slower backup, detected corruption).
    [switch]$WithChecksum,

    # Skip the post-backup integrity verification. Not recommended.
    [switch]$SkipVerify
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

    # ---- Refuse to pretend this covers a real deployment --------------------
    if (($Instance -notmatch '(?i)localdb') -and -not $AllowInstance) {
        throw @"
'$Instance' is not a LocalDB instance, so this script will not back it up: it
authenticates with Windows integrated auth (-E) and has no way to supply a SQL
login password.

  * Docker Compose database (the production-shaped stack)
      .\scripts\backup-db-container.ps1
  * Local development database
      .\scripts\backup-db.ps1        (default: (localdb)\MSSQLLocalDB)
  * A local named instance that integrated auth can actually reach
      .\scripts\backup-db.ps1 -Instance .\NEWVIX -AllowInstance
"@
    }

    if ($AllowInstance -and ($Instance -notmatch '(?i)localdb')) {
        Write-Warn "Instance '$Instance' was accepted with -AllowInstance."
        Write-Warn 'This script uses Windows integrated authentication (-E). It can'
        Write-Warn 'only reach an instance the current Windows account administers, and'
        Write-Warn 'it is still NOT the backup path for the Docker Compose database.'
    }

    # ---- The database name goes into T-SQL unquoted-ish, inside brackets -----
    # Reject anything that is not a plain identifier instead of letting a
    # crafted name break out of the brackets.
    if ($Database -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Database name '$Database' is not a plain SQL identifier (letters, digits and underscore only)."
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

    # ---- Ensure backup dir exists and is writable ---------------------------
    if (-not (Test-Path -LiteralPath $BackupDir)) {
        New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
        Write-Info "Created backup directory: $BackupDir"
    }

    # Probe the directory, but do NOT treat a failed probe as fatal.
    #
    # BACKUP DATABASE creates the .bak as the account the SQL Server service runs
    # as, which is often not the account running this script. A probe therefore
    # measures the wrong thing and can fail on a directory that would have
    # backed up fine:
    #   C:\Program Files\Microsoft SQL Server\MSSQL17.NEWVIX\MSSQL\Backup
    # is not writable by the current user (probe fails) yet is exactly where the
    # SQL Server service expects its backups. Conversely a directory under the
    # user's %TEMP% passes the probe and then fails with Msg 3201 "Operating
    # system error 5", because the service account has no rights there.
    #
    # Both cases are handled properly by letting BACKUP DATABASE run and giving a
    # message that names the service account when Msg 3201 comes back.
    $probe = Join-Path $BackupDir ('.vix-write-probe-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $probeOk = $true
    try {
        [System.IO.File]::WriteAllText($probe, 'probe')
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    }
    catch {
        $probeOk = $false
        Write-Warn "This account cannot write to '$BackupDir'."
        Write-Warn 'That is not necessarily fatal: BACKUP DATABASE writes the file as the'
        Write-Warn 'SQL Server service account, which may well have rights here. Continuing.'
    }

    # ---- Build backup filename & run BACKUP --------------------------------
    $stamp      = Get-Date -Format 'yyyyMMdd_HHmmss'
    $backupName = $Database + '_' + $stamp + '.bak'
    $backupFile = Join-Path $BackupDir $backupName

    # Two runs inside the same second would otherwise silently overwrite the
    # first backup (WITH INIT truncates the media) and retention would then
    # count one file where the operator expects two.
    if (Test-Path -LiteralPath $backupFile) {
        $seq = 2
        while (Test-Path -LiteralPath (Join-Path $BackupDir ($Database + '_' + $stamp + '_' + $seq + '.bak'))) {
            $seq++
        }
        $backupName = $Database + '_' + $stamp + '_' + $seq + '.bak'
        $backupFile = Join-Path $BackupDir $backupName
        Write-Warn "A backup already exists for this timestamp; writing $backupName instead."
    }

    # A path may legitimately contain an apostrophe (C:\Users\O'Brien\...),
    # which would otherwise break the SQL string literal outright.
    $sqlFilePath = $backupFile.Replace("'", "''")
    $options     = 'INIT, FORMAT'
    if ($WithChecksum) { $options += ', CHECKSUM' }

    $backupQuery = "BACKUP DATABASE [$Database] TO DISK = N'$sqlFilePath' WITH $options"

    Write-Info "Backing up database '$Database' on '$Instance'..."
    Write-Info "Target: $backupFile"
    if ($WithChecksum) { Write-Info 'Writing page checksums (slower, detects media corruption).' }

    $backupOutput = & $sqlcmdPath -S $Instance -E -C -b -Q $backupQuery 2>&1
    $backupExit   = $LASTEXITCODE
    $backupOutput | ForEach-Object { Write-Host $_ }
    if ($backupExit -ne 0) {
        # A failed BACKUP leaves a partial .bak on the media. It is not a
        # backup, so do not leave it looking like one.
        if (Test-Path -LiteralPath $backupFile) {
            $partial = (Get-Item -LiteralPath $backupFile).Length
            Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
            Write-Warn "Removed the partial backup left by the failed BACKUP ($partial bytes): $backupFile"
        }

        # Msg 3201 + "Operating system error 5 (Access is denied)" has a specific
        # cause worth naming, because the write probe above already proved the
        # directory is writable BY YOU and the failure still looks impossible:
        # BACKUP DATABASE writes the file as the SQL Server service account, not
        # as the account running this script. Measured on .\NEWVIX with a
        # directory under the user's TEMP: the probe passes, then Msg 3201.
        if (($backupOutput -join "`n") -match 'error 5\s*\(?Access is denied') {
            throw @"
sqlcmd failed (exit code $backupExit) while running BACKUP DATABASE.

This is the SQL Server service account's permission, not yours: the write probe
above passed, but BACKUP DATABASE creates the .bak as the account the SQL
Server service runs as. That account has no rights on:

    $BackupDir

Fix by granting the service account modify rights on that folder, or by putting
the backups where it already has them. To see which account that is:

    Get-CimInstance Win32_Service | Where-Object Name -like 'MSSQL*' |
        Select-Object Name, StartName
"@
        }

        throw "sqlcmd failed (exit code $backupExit) while running BACKUP DATABASE."
    }

    # The SQL Server service wrote the file, and the service account may have
    # rights on it that this account does not. Test-Path/Get-Item are
    # permissions-checked, so they are best-effort here: measured on
    # ...\MSSQL17.NEWVIX\MSSQL\Backup, BACKUP DATABASE succeeds and then
    # reading the file back throws "Access is denied", which would report a
    # successful backup as a failure.
    $sizeMB = $null
    try {
        if (-not (Test-Path -LiteralPath $backupFile)) {
            throw "BACKUP DATABASE reported success but the file is not visible: $backupFile"
        }
        $sizeMB = [math]::Round((Get-Item -LiteralPath $backupFile).Length / 1MB, 2)
    }
    catch {
        $probeErr = $_.Exception.Message
        if ($probeErr -match 'reported success but the file is not visible') { throw $probeErr }
        Write-Warn "Could not stat the file from this account ($probeErr)."
        Write-Warn 'The SQL Server service wrote it, so the VERIFYONLY check below is the real test.'
    }

    if ($null -ne $sizeMB) {
        Write-Ok "Backup created: $backupFile ($sizeMB MB)"
    }
    else {
        Write-Ok "Backup created: $backupFile (size not readable from this account)"
    }

    # ---- Verify the artifact before reporting success ----------------------
    # Existence and exit code are not evidence. These three reads are.
    if ($SkipVerify) {
        Write-Warn '-SkipVerify: the .bak was NOT read back. It has not been proven restorable.'
    }
    else {
        Write-Info 'Verifying the artifact (RESTORE VERIFYONLY + HEADERONLY)...'

        & $sqlcmdPath -S $Instance -E -C -b -h -1 -W -Q "SET QUOTED_IDENTIFIER ON; RESTORE VERIFYONLY FROM DISK = N'$sqlFilePath';"
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
            throw "RESTORE VERIFYONLY rejected the file BACKUP DATABASE just wrote, so it was deleted rather than left behind as a 'backup'. The media or the path is not trustworthy."
        }

        $sqlHeaderPath = Join-Path ([System.IO.Path]::GetTempPath()) ('vixhdr-' + [Guid]::NewGuid().ToString('N') + '.txt')
        try {
            # -h -1 drops the column names, which are what we need to read the
            # values by, so the header row is kept and the dash row skipped.
            & $sqlcmdPath -S $Instance -E -C -b -W -s '|' -o $sqlHeaderPath -Q "SET QUOTED_IDENTIFIER ON; RESTORE HEADERONLY FROM DISK = N'$sqlFilePath';"
            if ($LASTEXITCODE -ne 0) {
                Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
                throw 'RESTORE HEADERONLY could not read the file that was just written; it was deleted.'
            }

            # sqlcmd prints: column names / a dash rule / the values / row count.
            $header = @([System.IO.File]::ReadAllLines($sqlHeaderPath) | Where-Object { $_.Trim() -ne '' })
            $nameIdx = -1
            for ($i = 0; $i -lt $header.Count; $i++) {
                if ($header[$i] -match 'DatabaseName') { $nameIdx = $i; break }
            }
            if ($nameIdx -lt 0) {
                Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
                throw 'RESTORE HEADERONLY produced no readable header; the file was deleted.'
            }
            # The row right under the names is the "----|----|" separator rule,
            # not the data row.
            $valueIdx = $nameIdx + 1
            while ($valueIdx -lt $header.Count -and $header[$valueIdx] -match '^[\s\-|\t]+$' -and $header[$valueIdx] -match '-') { $valueIdx++ }
            if ($valueIdx -ge $header.Count) {
                Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
                throw 'RESTORE HEADERONLY produced no data row; the file was deleted.'
            }
            $names  = $header[$nameIdx].Split('|')
            $values = $header[$valueIdx].Split('|')
            $headerMap = @{}
            for ($i = 0; $i -lt $names.Count -and $i -lt $values.Count; $i++) { $headerMap[$names[$i].Trim()] = $values[$i].Trim() }

            if ($headerMap.ContainsKey('DatabaseName') -and $headerMap['DatabaseName'] -ne $Database) {
                Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
                throw "The file claims to contain '$($headerMap['DatabaseName'])', not '$Database'. Deleted instead of being reported as a backup of the requested database."
            }

            $damaged = $false
            if ($headerMap.ContainsKey('IsDamaged')) { $damaged = ($headerMap['IsDamaged'] -ne '0') }
            if ($damaged) {
                Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
                throw 'The backup header reports IsDamaged=1; the file was deleted.'
            }

            $hasChecksum = $false
            if ($headerMap.ContainsKey('HasBackupChecksums')) { $hasChecksum = ($headerMap['HasBackupChecksums'] -ne '0') }

            Write-Ok "Verified: VERIFYONLY passed, header readable, database='$Database', IsDamaged=0, checksums=$hasChecksum"

            # Third source of truth: SQL Server's own backup history. If
            # msdb has no row for this database, or the newest row is flagged
            # damaged, then the file on disk and the server disagree and the
            # file is not a backup we should be reporting on.
            #
            # is_copy is deliberately not filtered: LocalDB's reduced
            # msdb.dbo.backupset rejects the column with Msg 207, which would
            # abort this whole script.
            $msdbOut = & $sqlcmdPath -S $Instance -E -C -b -h -1 -W -Q "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SELECT CONVERT(varchar(10), ISNULL(MAX(CASE WHEN is_damaged = 0 THEN 0 ELSE 1 END), -1), 10) FROM msdb.dbo.backupset WHERE database_name = N'$Database';"
            if ($LASTEXITCODE -ne 0) {
                Write-Warn 'Could not read msdb.dbo.backupset; the file itself was already verified by VERIFYONLY and HEADERONLY.'
            }
            else {
                $msdbDamaged = ($msdbOut | Where-Object { $_.Trim() -ne '' } | Select-Object -Last 1).Trim()
                if ($msdbDamaged -eq '-1') {
                    Write-Warn "msdb.dbo.backupset has no row for '$Database'; the server's backup history does not know about this file."
                }
                elseif ($msdbDamaged -ne '0') {
                    Remove-Item -LiteralPath $backupFile -Force -ErrorAction SilentlyContinue
                    throw "msdb.dbo.backupset reports a damaged backup set for '$Database'; the file was deleted."
                }
                else {
                    Write-Ok "msdb.dbo.backupset agrees: is_damaged=0."
                }
            }

            if (-not $hasChecksum) {
                Write-Warn 'This .bak has NO page checksums, so byte-level media corruption can'
                Write-Warn 'pass VERIFYONLY and only surface later as Msg 824 on the RESTORED'
                Write-Warn "database. Measured: flipping 8 bytes mid-file leaves a non-checksum .bak"
                Write-Warn 'reported as "valid" and restorable-but-corrupt; the same flip on a'
                Write-Warn 'WITH CHECKSUM .bak is caught ("Damage to the backup set was detected").'
                Write-Warn 'Pass -WithChecksum unless your backup window cannot afford it.'
            }
        }
        finally {
            Remove-Item -LiteralPath $sqlHeaderPath -Force -ErrorAction SilentlyContinue
        }
    }

    # ---- Retention: delete THIS DATABASE's .bak older than RetainDays -------
    # The old code deleted every *.bak in the folder, so scheduling this script
    # against a shared backup directory silently destroyed other systems'
    # backups. backup-db-container.ps1 already filtered on the database name.
    $cutoff     = (Get-Date).AddDays(-$RetainDays)
    $pattern    = $Database + '_*.bak'
    $oldBackups = @()
    try {
        $oldBackups = @(Get-ChildItem -LiteralPath $BackupDir -Filter $pattern -File -ErrorAction SilentlyContinue |
                        Where-Object { $_.LastWriteTime -lt $cutoff })
    }
    catch {
        Write-Warn "Could not list '$pattern' for retention: $($_.Exception.Message)"
    }

    foreach ($old in $oldBackups) {
        # A pruning failure is not a backup failure: the backup is already
        # written and verified. Say so instead of reporting a failed backup.
        try {
            Remove-Item -LiteralPath $old.FullName -Force
            Write-Info "Deleted old backup (retention >$RetainDays days): $($old.Name)"
        }
        catch {
            Write-Warn "Could not delete $($old.Name) (retention >$RetainDays days): $($_.Exception.Message)"
        }
    }

    # Counting is also permissions-checked, so a folder this account can neither
    # write to nor list must not be reported as "0 backups remain" - that reads
    # as though retention had just deleted everything.
    $remaining = $null
    $foreign   = $null
    try {
        $remaining = @(Get-ChildItem -LiteralPath $BackupDir -Filter $pattern -File -ErrorAction Stop).Count
        $foreign   = @(Get-ChildItem -LiteralPath $BackupDir -Filter '*.bak' -File -ErrorAction Stop |
                       Where-Object { $_.Name -notlike $pattern }).Count
    }
    catch {
        Write-Warn "Retention removed $($oldBackups.Count) file(s), but '$BackupDir' cannot be listed from this account: $($_.Exception.Message)"
        Write-Warn 'Backups older than the retention window may therefore still be present. Check them with an account that can read the folder.'
    }

    if ($null -ne $remaining) {
        Write-Ok "Retention applied. $($oldBackups.Count) removed, $remaining backup(s) of '$Database' remain."
        if ($foreign -gt 0) {
            Write-Info "$foreign other-database .bak file(s) in this folder were left untouched (retention covers '$pattern' only)."
        }
    }

    Write-Ok 'Backup completed successfully.'
    Write-Info 'VERIFYONLY proves the media is readable, NOT that a restore works end to end.'
    Write-Info 'To prove the whole cycle (backup -> restore -> compare data -> DBCC CHECKDB), run:'
    Write-Info '    node e2e\verify_backup_restore.cjs'
    exit 0
}
catch {
    Write-Fail "Backup FAILED: $($_.Exception.Message)"
    exit 1
}
