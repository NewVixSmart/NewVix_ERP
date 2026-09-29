<#
.SYNOPSIS
    Backs up the Docker Compose database (the "db" service) and applies retention.

.DESCRIPTION
    This is the backup path for the containerised database started by
    docker-compose.yml. It talks to the running db container with the sqlcmd
    client that already ships inside the Microsoft SQL Server image - it needs
    no sqlcmd on the host, and it never reads the SA password: MSSQL_SA_PASSWORD
    is already in the container's environment and the script below expands it
    inside the container, so the secret never crosses the host command line.

    Steps:
      1. Verify the db container is running.
      2. Make sure /var/opt/mssql/backup exists and is owned by the "mssql"
         account (uid 10001). Docker creates a fresh volume mount point owned by
         root and SQL Server cannot write there. Idempotent, runs every time.
      3. BACKUP DATABASE ... WITH INIT, FORMAT, COMPRESSION.
      4. Delete *.bak older than -RetainDays.
      5. Optionally copy the new .bak OFF this host with -CopyTo.

    WHERE DO THE BACKUPS LAND?
    In the "sqlserver-backup" Docker volume, i.e. on this host only. Nothing
    replicates them. If this machine is lost, so is every backup this script
    takes. Use -CopyTo (or a separate sync job) to move them off-box, and
    actually rehearse a restore - see restore-db-container.ps1.

.NOTES
    Companion of backup-db.ps1, which is LocalDB-only: that script uses Windows
    integrated authentication (-E) and can never reach this database.

.EXAMPLE
    .\scripts\backup-db-container.ps1

.EXAMPLE
    .\scripts\backup-db-container.ps1 -RetainDays 30 -CopyTo D:\OffsiteBackups
#>

[CmdletBinding()]
param(
    [string]$Database = 'NewVixSmartDb',

    [int]$RetainDays = 14,

    # Host directory to copy the new .bak into. Strongly recommended: without
    # it the backup only exists in a Docker volume on this machine.
    [string]$CopyTo
)

$ErrorActionPreference = 'Stop'

function Write-Info { Write-Host "[INFO ] $args" -ForegroundColor Cyan }
function Write-Ok   { Write-Host "[  OK ] $args" -ForegroundColor Green }
function Write-Warn { Write-Host "[ WARN] $args" -ForegroundColor Yellow }
function Write-Fail { Write-Host "[ FAIL] $args" -ForegroundColor Red }

$Service    = 'db'
$RemoteDir  = '/var/opt/mssql/backup'
$SqlCmdPath = '/opt/mssql-tools/bin/sqlcmd'

# ---------------------------------------------------------------------------
# The bash program executed inside the container. It is piped over stdin
# (bash -s) rather than passed as a command-line argument, so PowerShell 5.1's
# native-argument quoting cannot mangle the SQL and no secret appears in any
# argument. $MSSQL_SA_PASSWORD is expanded by the container's own shell.
# Permissions are already handled by the host before this runs.
# ---------------------------------------------------------------------------
$bashScript = @'
set -euo pipefail

SQLCMD=/opt/mssql-tools/bin/sqlcmd
DB="$VIX_DB"
DIR="$VIX_DIR"
RETAIN="$VIX_RETAIN"
FILE="${DIR}/${VIX_DB}_${VIX_TS}.bak"

if [ ! -d "$DIR" ] || [ ! -w "$DIR" ]; then
    echo "ERROR: $DIR is not a writable directory for uid $(id -u)." >&2
    exit 3
fi

# Full backup. WITH INIT/FORMAT keeps exactly one clean file per run.
echo "backing up [$DB] -> $FILE"
"$SQLCMD" -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -b \
    -Q "BACKUP DATABASE [$DB] TO DISK = N'$FILE' WITH INIT, FORMAT, COMPRESSION"

if [ ! -s "$FILE" ]; then
    echo "ERROR: BACKUP reported success but $FILE is missing or empty." >&2
    exit 4
fi
echo "OK $FILE $(stat -c %s "$FILE") bytes"

# Retention.
find "$DIR" -maxdepth 1 -type f -name "${DB}_*.bak" -mtime "+$RETAIN" -print -delete || true
echo "retention applied: keeping ${RETAIN}d"
'@

try {
    if ($RetainDays -lt 1) { throw "RetainDays ($RetainDays) must be at least 1." }

    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'docker was not found on PATH. Install Docker Desktop / Docker Engine with the Compose plugin.'
    }

    # ---- Is the db container actually up? ----------------------------------
    $state = (& docker compose ps --format '{{.Service}} {{.State}}' $Service 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "'docker compose ps' failed:`n$state" }
    if ($state -notmatch 'running') {
        throw @"
The '$Service' service is not running, so there is nothing to back up.

  docker compose ps
  docker compose up -d

Reported state:
$state
"@
    }

    $stamp    = (Get-Date).ToUniversalTime().ToString('yyyyMMdd_HHmmss')
    $remoteFile = "$RemoteDir/${Database}_$stamp.bak"

    Write-Info "Container backup: database='$Database' retention=${RetainDays}d"

    # ---- Permissions: the volume mount point is created root-owned, and SQL
    # ---- Server runs as uid 10001 ("mssql"). Done as root, idempotently.
    & docker compose exec -T -u root $Service /bin/bash -c "mkdir -p '$RemoteDir' && chown mssql:mssql '$RemoteDir' && chmod 750 '$RemoteDir'"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not prepare $RemoteDir inside the container (needs root; 'docker compose exec -u root' was used)."
    }

    # ---- Backup + retention -------------------------------------------------
    # CR is stripped because a git checkout on Windows may hand bash CRLF.
    $payload = $bashScript.Replace("`r", '')

    $dockerArgs = @(
        'compose', 'exec', '-T',
        '-e', "VIX_DB=$Database",
        '-e', "VIX_DIR=$RemoteDir",
        '-e', "VIX_TS=$stamp",
        '-e', "VIX_RETAIN=$RetainDays",
        $Service, '/bin/bash', '-s'
    )

    $output = $payload | & docker @dockerArgs 2>&1
    $exit   = $LASTEXITCODE
    $output | ForEach-Object { Write-Host "  $_" }
    if ($exit -ne 0) { throw "Backup failed inside the container (exit code $exit)." }

    # ---- Copy off-box -------------------------------------------------------
    if ($CopyTo) {
        if (-not (Test-Path -LiteralPath $CopyTo)) { New-Item -ItemType Directory -Path $CopyTo -Force | Out-Null }
        & docker compose cp "$Service`:$remoteFile" "$CopyTo"
        if ($LASTEXITCODE -ne 0) { throw "'docker compose cp' failed; the .bak is still in the volume." }
        Write-Ok "Copied off-box to: $CopyTo"
    }
    else {
        Write-Warn "No -CopyTo given: this .bak exists only in the 'sqlserver-backup' Docker volume on this host."
    }

    Write-Ok 'Container backup completed.'
    exit 0
}
catch {
    Write-Fail "Backup FAILED: $($_.Exception.Message)"
    exit 1
}
