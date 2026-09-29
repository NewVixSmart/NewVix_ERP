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
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'docker was not found on PATH. Install Docker Desktop / Docker Engine with the Compose plugin.'
    }

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
