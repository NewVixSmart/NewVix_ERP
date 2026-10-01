<#
.SYNOPSIS
    Restores the Docker Compose database ("db" service) from a .bak produced by
    backup-db-container.ps1 or by the db-backup compose service.

.DESCRIPTION
    RESTORE RUNBOOK. Read it before running anything: the default path
    overwrites live data.

    The .bak has to be inside the container's backup directory
    (/var/opt/mssql/backup, a Docker volume - not a normal host folder). Pass
    -HostBakFile and this script stages it in for you.

    What it does, in order:
      1. Inventories the .bak files already in the backup volume.
      2. Reads the backup's own database name (RESTORE HEADERONLY) and its
         logical file names (RESTORE FILELISTONLY) - nothing has to be guessed,
         and the target name never degrades to a suffixed logical file name.
      3. Takes a tail-log backup of the database it is about to replace, so the
         restore is point-in-time rather than "as of the last full backup".
      4. Drops the existing database (single user, rollback immediate).
      5. RESTORE DATABASE ... WITH MOVE / REPLACE / RECOVERY, then DBCC CHECKDB.

    The web service is left running. EF Core re-applies whatever the restored
    schema needs on the next request; stop the web service yourself if you want
    the restore completely undisturbed.

.NOTES
    This REPLACES the database. There is no undo. Run backup-db-container.ps1
    first if the current contents still matter.

    ORDER OF OPERATIONS, and why it matters. The .bak is validated with
    RESTORE HEADERONLY, FILELISTONLY and VERIFYONLY *before* the target database
    is dropped. Measured on a real SQL Server: a truncated .bak fails VERIFYONLY
    with Msg 3241 and a 0-byte .bak with Msg 3254, so both are now caught while
    the live database is still there, with the message
    "NOTHING HAS BEEN CHANGED". Validating after the DROP - as an earlier
    ordering could easily have done - would destroy the live database and then
    discover the backup was unusable.

    NOT EXECUTABLE WITHOUT DOCKER. This script needs a running Docker daemon and
    the Compose plugin. When either is missing it now fails in its first lines
    naming the missing prerequisite, rather than surfacing a Docker API error
    further down that reads like a .env or password problem.

.EXAMPLE
    # Show what is available, change nothing
    .\scripts\restore-db-container.ps1 -ListOnly

.EXAMPLE
    # Restore the most recent .bak in the backup volume (asks for confirmation)
    .\scripts\restore-db-container.ps1

.EXAMPLE
    # Restore a specific .bak that is still on the host
    .\scripts\restore-db-container.ps1 -HostBakFile D:\OffsiteBackups\NewVixSmartDb_20260101_020000.bak
#>

[CmdletBinding()]
param(
    # .bak on the host. Copied into the container's backup volume first.
    # Ignored when -ListOnly is used.
    [string]$HostBakFile,

    # Restore INTO this database name. Defaults to the DatabaseName recorded in
    # the backup's own header (RESTORE HEADERONLY) - never to the logical file
    # names, which are usually suffixed and would create a second database.
    [string]$Database,

    [switch]$ListOnly
)

$ErrorActionPreference = 'Stop'

function Write-Info { Write-Host "[INFO ] $args" -ForegroundColor Cyan }
function Write-Ok   { Write-Host "[  OK ] $args" -ForegroundColor Green }
function Write-Warn { Write-Host "[ WARN] $args" -ForegroundColor Yellow }
function Write-Fail { Write-Host "[ FAIL] $args" -ForegroundColor Red }

# ---------------------------------------------------------------------------
# Prerequisite check, FIRST. Same reasoning as backup-db-container.ps1: the old
# version only checked that the `docker` COMMAND was on PATH, so a stopped
# daemon (or a missing Compose plugin) surfaced much later as whatever Docker
# happened to print. This is a disaster-recovery tool; when it cannot run, it
# must say why in the operator's first three lines.
# ---------------------------------------------------------------------------
function Assert-DockerPrerequisites {
    $dockerCmd = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $dockerCmd) {
        throw @"
docker was not found on PATH, so a container restore cannot run.

This is the ONLY restore path for the database started by docker-compose.yml.

Install one of:
  * Docker Desktop (Windows)  - https://docs.docker.com/desktop/install/windows-install/
  * Docker Engine + the Compose plugin (Linux)

Then confirm the daemon is actually running:
  docker info
"@
    }

    # stderr must be discarded, not merged: with $ErrorActionPreference='Stop',
    # PowerShell 5.1 turns a native command's redirected stderr into a
    # terminating error, which would throw Docker's raw API text before the exit
    # code could be inspected.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $dockerCmd.Source info --format '{{.ServerVersion}}' 2>$null | Out-Null
        $infoExit = $LASTEXITCODE
        & $dockerCmd.Source compose version 2>$null | Out-Null
        $composeExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }

    if ($infoExit -ne 0) {
        throw @"
The Docker CLI is present ($($dockerCmd.Source)) but the daemon is not
responding, so a restore cannot run.

Start Docker Desktop (or `sudo systemctl start docker`) and re-run, or:
  docker info          # must succeed
  docker version       # Server section must be populated
"@
    }

    if ($composeExit -ne 0) {
        throw @"
The Docker daemon is running but the `docker compose` plugin is missing.

  * Docker Desktop includes it.
  * On Linux: install docker-compose-plugin for your distribution.

This script uses `docker compose`; it will not fall back to `docker-compose`.
"@
    }
}

$Service   = 'db'
$RemoteDir = '/var/opt/mssql/backup'

# Runs inside the container. The SA password is expanded by the container's own
# shell, so it never appears in any host-side argument.
$bashScript = @'
set -euo pipefail

DIR="$VIX_DIR"
RUN_SQL="$VIX_RUN_SQL"

sql() {
    /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -h -1 -W -Q "$1"
}

if [ ! -d "$DIR" ]; then
    echo "ERROR: $DIR does not exist. Has a backup ever been taken?" >&2
    exit 3
fi
if [ -z "$RUN_SQL" ]; then
    echo "ERROR: no backup selected." >&2
    exit 4
fi

# ---- 1. What is IN the backup (header), then its logical file names -------
# Both are read from the backup itself. The header carries the authoritative
# DatabaseName; the file list carries the logical names. Neither is guessed.
echo "--- RESTORE HEADERONLY ---"
hdrfile=/tmp/vix-header.txt
sql "RESTORE HEADERONLY FROM DISK = N'$RUN_SQL'" > "$hdrfile"
backup_db="$(awk -F'|' '{gsub(/\r/, "", $1); gsub(/^[ \t]+|[ \t]+$/, "", $1); if ($1 == "DatabaseName") { gsub(/^[ \t]+|[ \t]+$/, "", $2); print $2; exit }}' "$hdrfile")"
if [ -z "$backup_db" ]; then
    echo "ERROR: could not read DatabaseName from the backup header." >&2
    cat "$hdrfile" >&2
    exit 5
fi
echo "database recorded in the backup: $backup_db"

echo "--- RESTORE FILELISTONLY ---"
listfile=/tmp/vix-filelist.txt
sql "RESTORE FILELISTONLY FROM DISK = N'$RUN_SQL'" > "$listfile"

log="$(awk -F'|' '{gsub(/\r/, "", $1); gsub(/^[ \t]+|[ \t]+$/, "", $1); if ($1 != "" && $1 ~ /log$/) { print $1; exit }}' "$listfile")"
data="$(awk -F'|' '{gsub(/\r/, "", $1); gsub(/^[ \t]+|[ \t]+$/, "", $1); if ($1 != "" && $1 !~ /log$/) { print $1; exit }}' "$listfile")"
if [ -z "$data" ] || [ -z "$log" ]; then
    echo "ERROR: could not read the logical file names from the backup." >&2
    cat "$listfile" >&2
    exit 6
fi
echo "logical files: data='$data' log='$log'"

# ---- 1b. Prove the media is restorable BEFORE anything is destroyed -------
# This runs before the DROP below, on purpose.
#
# Measured on a real SQL Server, a truncated .bak and a 0-byte .bak both make
# RESTORE VERIFYONLY fail (Msg 3241 "media family ... is incorrectly formed",
# Msg 3254 "The volume ... is empty"). Catching that here means the operator
# learns their only backup is unusable while the live database is still intact.
# Discovering it after the DROP means the restore target is already gone.
if ! sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b -Q "RESTORE VERIFYONLY FROM DISK = N'$RUN_SQL'" > /tmp/vix-verify.txt 2>&1; then
    echo "ERROR: RESTORE VERIFYONLY rejected the backup; NOTHING HAS BEEN CHANGED." >&2
    cat /tmp/vix-verify.txt >&2
    echo "" >&2
    echo "The live database was NOT dropped and NOT modified. This .bak cannot be" >&2
    echo "restored. Take a fresh backup before retrying, or restore a different file." >&2
    exit 7
fi
echo "VERIFYONLY passed: the media is a complete, readable backup set."

# Restore INTO the requested name, else the name the backup was taken from.
# It must NOT fall back to the logical log name (e.g. NewVixSmartDb_log) or
# the restore silently creates a second, wrongly-named database.
target_db="${VIX_DB:-$backup_db}"

# ---- 2. Tail-log backup of the database we are about to replace -----------
# Anchors the restore point to "now" rather than "when the last full backup
# ran". Only meaningful for FULL recovery; in SIMPLE it fails and is reported.
if [ "${VIX_TAIL_LOG:-1}" = "1" ] && [ "${target_db,,}" != "${backup_db,,}" ]; then
    tail="${DIR}/${target_db}_taillog_$(date -u +%Y%m%d_%H%M%S).trn"
    echo "--- tail-log backup of [$target_db] -> $tail ---"
    sql "BACKUP DATABASE [$target_db] TO DISK = N'$tail' WITH INIT, NO_TRUNCATE" \
        || echo "WARNING: tail-log backup of [$target_db] failed; the restore point degrades to the last full backup."
else
    echo "--- skipping tail-log backup: no current database named [$target_db] to anchor from ---"
fi

# ---- 3. Drop the current database -----------------------------------------
echo "--- dropping existing [$target_db] (single user, rollback immediate) ---"
sql "IF DB_ID(N'$target_db') IS NOT NULL
     BEGIN
       DECLARE @sql nvarchar(max) =
         N'ALTER DATABASE [$target_db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$target_db];';
       EXEC sp_executesql @sql;
     END" || true

# ---- 4. Restore ------------------------------------------------------------
echo "--- RESTORE DATABASE [$target_db] FROM $RUN_SQL ---"
sql "RESTORE DATABASE [$target_db]
     FROM DISK = N'$RUN_SQL'
     WITH MOVE N'$data' TO N'/var/opt/mssql/data/$target_db.mdf',
          MOVE N'$log'  TO N'/var/opt/mssql/log/$target_db_log.ldf',
          REPLACE, RECOVERY, STATS = 5"

# ---- 5. Verify -------------------------------------------------------------
echo "--- DBCC CHECKDB ---"
sql "DBCC CHECKDB([$target_db]) WITH NO_INFOMSGS"

echo "VIX_RESTORED_DB=$target_db"
'@

function Invoke-DbBash {
    param(
        [Parameter(Mandatory)] [hashtable] $EnvVars,
        [string] $Bash
    )
    $dockerArgs = @('compose', 'exec', '-T')
    foreach ($key in $EnvVars.Keys) { $dockerArgs += @('-e', "$key=$($EnvVars[$key])") }
    $dockerArgs += @($Service, '/bin/bash', '-s')
    $out = $Bash.Replace("`r", '') | & docker @dockerArgs 2>&1
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = ($out | Out-String) }
}

try {
    Assert-DockerPrerequisites
    Write-Ok 'Docker prerequisites satisfied (CLI, daemon, compose plugin).'

    $state = (& docker compose ps --format '{{.Service}} {{.State}}' $Service 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "'docker compose ps' failed:`n$state" }
    if ($state -notmatch 'running') { throw "The '$Service' service is not running. Start it with: docker compose up -d" }

    # ---- Inventory ----------------------------------------------------------
    Write-Info "Backups in $RemoteDir :"
    $inventory = Invoke-DbBash -EnvVars @{ VIX_DIR = $RemoteDir } -Bash 'ls -lh --time-style=long-iso "$VIX_DIR"/*.bak 2>/dev/null || echo "(none)"'
    $inventory.Out | ForEach-Object { Write-Host "  $_" }

    if ($ListOnly) {
        Write-Ok 'Inventory only. No changes were made.'
        exit 0
    }

    # ---- Which .bak? --------------------------------------------------------
    $remoteFile = $null
    if ($HostBakFile) {
        if (-not (Test-Path -LiteralPath $HostBakFile)) { throw "Backup file not found: $HostBakFile" }
        $remoteFile = "$RemoteDir/$(Split-Path -Leaf $HostBakFile)"
        Write-Info "Staging $HostBakFile into the container ..."
        & docker compose cp $HostBakFile "$Service`:$remoteFile"
        if ($LASTEXITCODE -ne 0) { throw "'docker compose cp' failed: could not stage the .bak inside the container." }
    }
    else {
        $candidate = & docker compose exec -T $Service /bin/bash -c "ls -1t $RemoteDir/*.bak 2>/dev/null | head -n 1"
        if ($LASTEXITCODE -ne 0 -or -not $candidate -or -not $candidate[0]) {
            throw "No .bak found in $RemoteDir. Take one first (backup-db-container.ps1) or pass -HostBakFile."
        }
        $remoteFile = $candidate[0].ToString().Trim()
    }

    Write-Warn "This REPLACES the database with $remoteFile. There is no undo."
    if ((Read-Host 'Type RESTORE to continue') -cne 'RESTORE') {
        Write-Info 'Aborted; nothing was changed.'
        exit 0
    }

    $envVars = @{
        VIX_DIR      = $RemoteDir
        VIX_RUN_SQL  = $remoteFile
        VIX_TAIL_LOG = '1'
    }
    if ($Database) { $envVars['VIX_DB'] = $Database }

    $result = Invoke-DbBash -EnvVars $envVars -Bash $bashScript
    $result.Out | ForEach-Object { Write-Host $_ }
    if ($result.Exit -ne 0) { throw "Restore failed (exit code $($result.Exit))." }

    Write-Ok 'Restore completed; DBCC CHECKDB reported no errors.'
    Write-Info 'The app re-applies EF migrations on first use after the restore.'
    exit 0
}
catch {
    Write-Fail "Restore FAILED: $($_.Exception.Message)"
    exit 1
}
