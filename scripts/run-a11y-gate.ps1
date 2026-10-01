<#
.SYNOPSIS
    Runs the accessibility gate against a throwaway database, from nothing to a report.

.DESCRIPTION
    Creates a database that only this script owns, starts the built web app against it, seeds
    it through the UI, scans every route in both themes, then drops the database again. The
    database name is refused unless it carries the NewVixSmart_A11yGate prefix, so a mistyped
    -Database can never point the gate at real data, and the database is dropped in a finally
    block even when the scan fails.

    Run scripts/check-text-hygiene.ps1 and the build before this, it reuses their Release output.

.PARAMETER SkipSeed
    Skips the UI seeding. Used to prove the gate actually fails: with no documents to render,
    the id-keyed routes cannot resolve and the gate must exit non-zero.

.EXAMPLE
    pwsh -NoProfile -File scripts/run-a11y-gate.ps1
    pwsh -NoProfile -File scripts/run-a11y-gate.ps1 -SkipSeed
#>
[CmdletBinding()]
param(
    [string]$SqlServer = '.\NEWVIX',
    [string]$Database = 'NewVixSmart_A11yGate',
    [int]$Port = 5165,
    [string]$User = 'admin',
    [string]$Configuration = 'Release',
    [switch]$SkipSeed
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$appExe = Join-Path $repoRoot "src/NewVixSmart.Web/bin/$Configuration/net10.0/NewVixSmart.Web.exe"
$workDir = Join-Path $env:TEMP "opencode/a11y-gate"
$logDir = Join-Path $workDir (Get-Date -Format 'yyyyMMdd-HHmmss')
$baseUrl = "http://127.0.0.1:$Port"
$nodeCmd = Get-Command node.exe -ErrorAction SilentlyContinue
$node = if ($nodeCmd) { $nodeCmd.Source } else { 'C:\Program Files\nodejs\node.exe' }

# The seed passwords and key exist only inside this process and only for the throwaway database.
$seedPassword = 'A11yGate-Admin-' + [Guid]::NewGuid().ToString('N').Substring(0, 12) + '!'

function Invoke-Sql {
    param([string]$Query)
    $args = @('-S', $SqlServer, '-E', '-C', '-b', '-Q', $Query)
    & sqlcmd @args | Out-String | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with $LASTEXITCODE" }
}

function Remove-GateDatabase {
    if (-not (Test-Path 'env:TEMP')) { return }
    Invoke-Sql "IF DB_ID('$Database') IS NOT NULL BEGIN ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database]; END"
}

function Wait-Healthy {
    param([System.Diagnostics.Process]$Process, [int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "the app exited with $($Process.ExitCode) before becoming healthy; see $logDir"
        }
        try {
            $response = Invoke-WebRequest -UseBasicParsing "$baseUrl/healthz" -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        } catch {
            # still booting
        }
        Start-Sleep -Seconds 2
    }
    throw "the app did not answer /healthz within $TimeoutSeconds seconds; see $logDir"
}

function Invoke-Node {
    param([string]$Script, [hashtable]$Environment)
    $previous = @{}
    foreach ($key in $Environment.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $Environment[$key])
    }
    try {
        & $node $Script
        $code = $LASTEXITCODE
    } finally {
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previous[$key])
        }
    }
    if ($code -ne 0) { throw "$Script exited with $code" }
}

function Invoke-SqlScalar {
    param([string]$Query)
    $result = & sqlcmd -S $SqlServer -d $Database -E -C -b -h -1 -W -Q $Query
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed with $LASTEXITCODE" }
    return ($result | Select-Object -First 1).Trim()
}

# /healthz only answers after Migrate and the development seed finish, but the seeder also
# refuses to run against a half seeded database. Waiting for the row counts makes the failure
# mode explicit instead of surfacing as a confusing timeout inside the seed.
function Wait-DemoData {
    param([int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $counts = Invoke-SqlScalar "SELECT CAST((SELECT COUNT(*) FROM Items) AS varchar(9)) + '/' + CAST((SELECT COUNT(*) FROM Customers) AS varchar(9))"
        if ($counts -and $counts -notmatch '^0+/0+$') {
            Write-Host "== demo data present (items/customers $counts)"
            return
        }
        Start-Sleep -Seconds 2
    }
    throw "the development seed did not populate the throwaway database within $TimeoutSeconds seconds"
}

if ($Database -notmatch '^NewVixSmart_(A11yGate|Test)') {
    throw "refusing to run: '$Database' is not a throwaway gate database. Use a NewVixSmart_A11yGate* name."
}
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "the built app is missing: $appExe. Run: dotnet build NewVixSmart.slnx -c $Configuration"
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is not on PATH, the runner cannot manage the throwaway database'
}

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$appOut = Join-Path $logDir 'app.out.log'
$appErr = Join-Path $logDir 'app.err.log'
$app = $null
$exitCode = 1

try {
    Write-Host "== dropping any leftover $Database"
    Remove-GateDatabase

    Write-Host "== starting the app on $baseUrl against $Database"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $baseUrl
    $env:ConnectionStrings__DefaultConnection = "Server=$SqlServer;Database=$Database;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
    $env:Jwt__Key = "A11yGate-Throwaway-$([Guid]::NewGuid().ToString('N'))-Key-0123456789"
    $env:Seed__AdminPassword = $seedPassword
    $env:Seed__AccountantPassword = 'A11yGate-Accountant-' + [Guid]::NewGuid().ToString('N').Substring(0, 12) + '!'
    $env:Seed__WarehousePassword = 'A11yGate-Warehouse-' + [Guid]::NewGuid().ToString('N').Substring(0, 12) + '!'

    $app = Start-Process -FilePath $appExe -WorkingDirectory (Split-Path -Parent $appExe) `
        -RedirectStandardOutput $appOut -RedirectStandardError $appErr -PassThru -NoNewWindow
    Wait-Healthy -Process $app
    Write-Host "== healthy (pid $($app.Id))"
    Wait-DemoData

    $gateEnv = @{
        BASE_URL   = $baseUrl
        VIX_USER   = $User
        VIX_PASS   = $seedPassword
        ASPNETCORE_ENVIRONMENT = 'Development'
    }

    if ($SkipSeed) {
        Write-Host '== skipping the seed on purpose: the gate is expected to fail on empty documents'
    } else {
        Write-Host '== seeding through the UI'
        Invoke-Node -Script (Join-Path $repoRoot 'e2e/seed-a11y-data.cjs') -Environment $gateEnv
    }

    Write-Host '== scanning'
    try {
        Invoke-Node -Script (Join-Path $repoRoot 'e2e/a11y-gate.cjs') -Environment $gateEnv
        $exitCode = 0
    } catch {
        Write-Host $_.Exception.Message
        $exitCode = 1
    }
} finally {
    if ($app) {
        Write-Host "== stopping the app (pid $($app.Id))"
        if (-not $app.HasExited) { $app.Kill() }
        $app.WaitForExit(30000) | Out-Null
    }
    try {
        Remove-GateDatabase
        Write-Host "== dropped $Database"
    } catch {
        Write-Warning "could not drop ${Database}: $($_.Exception.Message)"
    }
    Remove-Item Env:\ASPNETCORE_ENVIRONMENT, Env:\ASPNETCORE_URLS, Env:\ConnectionStrings__DefaultConnection, `
        Env:\Jwt__Key, Env:\Seed__AdminPassword, Env:\Seed__AccountantPassword, Env:\Seed__WarehousePassword -ErrorAction SilentlyContinue
    Write-Host "== logs: $logDir"
}

exit $exitCode
