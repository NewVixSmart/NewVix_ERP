<#
.SYNOPSIS
    The test suite for the R8 line allowlist in scripts/check-text-hygiene.ps1.

.DESCRIPTION
    WHY A SEPARATE SUITE
    RazorHygieneTests.cs already mirrors every RULE of the gate rule-for-rule and asserts that
    the repository comes out clean. What it cannot assert - and what this file exists for - is
    the thing the allowlist adds: that an exception is an ALLOWLIST and not a SUPPRESSION.
    That distinction is behavioural, not textual, and it has exactly one honest proof: change
    the line the exception covers and watch the warnings come back. Both suites are kept. The
    C# one owns the rules; this one owns the honesty of the exceptions.

    This file is PowerShell rather than C# on purpose. It owns no .cs, no .csproj and no .yml,
    so it can be added, reviewed and run without touching the build, the solution, or the CI
    step that already invokes the gate.

    HOW TO RUN
        powershell -NoProfile -ExecutionPolicy Bypass -File tests/HygieneGate.Tests.ps1
    Exit 0 when every case passes, 1 otherwise, so it is usable as a gate itself. CI does not
    run it: the C# suite already runs the gate over the repository, so the pipeline and the
    production step stay exactly as they are.

    WHY THE MUTATION RUNS ON A MIRROR AND NOT ON THE REPOSITORY FILE
    The mutation has to reproduce the real file's repository-relative path for the allowlist
    entry to be consulted at all, and Deep-Audit-Report.md sits at the repository root - so a
    byte-identical copy of it in a temp directory is judged by exactly the same code path. Two
    cases close the loop back to the committed file: EveryCaseLeavesTheRepositoryUntouched
    asserts its bytes are unchanged, and TheEntryNamesTheCommittedLineAndItsExactContent
    asserts the committed line 287 hashes to the sha256 the script's own table prints. So the
    chain is committed file -> table -> mirror copy -> mutation, and nothing is left damaged if
    the process is killed mid-test. Mutating the audit report in place for the length of one
    subprocess would prove the same thing and would risk a file this suite does not own, on a
    machine whose working copy sits under OneDrive sync.

    NON-VACUITY
    Every case here has been seen red. The two mutation cases were proven by editing the
    allowlisted line and by moving it, and observing the warnings return; the rule cases by
    feeding the gate a deliberately broken file; the validation case by pointing it at a
    mistyped entry, with the unmodified script run alongside as a control; the encoding case by
    aiming its predicate at a byte sequence that must fail it. A suite that has only ever been
    green proves nothing about what it guards, so each case that could pass for the wrong
    reason says which reason it rules out.

    THIS FILE IS ASCII-ONLY, ON PURPOSE
    Every Arabic and CJK fixture below is built from [char] codes rather than written as a
    literal, for two reasons that are not stylistic. First, Windows PowerShell 5.1 decodes a
    BOM-less script using the console codepage - 1256 on an Egyptian machine - so a literal
    here would arrive as mojibake on exactly the host that runs the gate. Second, a tool that
    round-trips the file through a codepage can silently rewrite characters it cannot
    represent, which is the very damage class this repository is guarding against. Naming code
    points is unambiguous in both directions.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# The gate's own output is ASCII by construction (U+XXXX escapes), so this only removes any
# doubt about how a child's stdout is decoded on a 1256 console.
try { [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false) } catch { }

# ------------------------------------------------------------------ harness

$script:Passed = 0
$script:Failed = 0

function Test-Case([string]$Name, [scriptblock]$Body) {
    try {
        & $Body
        $script:Passed++
        Write-Output ('PASS  {0}' -f $Name)
    } catch {
        $script:Failed++
        Write-Output ('FAIL  {0}' -f $Name)
        Write-Output ('      {0}' -f $_.Exception.Message)
    }
}

function Fail([string]$Message) { throw $Message }

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { Fail $Message }
}

function Assert-False([bool]$Condition, [string]$Message) {
    if ($Condition) { Fail $Message }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -ne $Actual) { Fail ('{0} (expected [{1}], got [{2}])' -f $Message, $Expected, $Actual) }
}

function Assert-Contains([string]$Haystack, [string]$Needle, [string]$Message) {
    if ($Haystack.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        Fail ('{0} (missing: {1})' -f $Message, $Needle)
    }
}

function Assert-NotContains([string]$Haystack, [string]$Needle, [string]$Message) {
    if ($Haystack.IndexOf($Needle, [System.StringComparison]::Ordinal) -ge 0) {
        Fail ('{0} (unexpectedly present: {1})' -f $Message, $Needle)
    }
}

function New-Tree {
    $root = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'nvs-hygiene-' + [guid]::NewGuid().ToString('n'))
    [void][System.IO.Directory]::CreateDirectory($root)
    return $root
}

function Remove-Tree([string]$Root) {
    if (-not [string]::IsNullOrEmpty($Root) -and [System.IO.Directory]::Exists($Root)) {
        try { [System.IO.Directory]::Delete($Root, $true) } catch { }
    }
}

# ------------------------------------------------------------------ fixtures

$Utf8 = [System.Text.UTF8Encoding]::new($false, $true)

# Arabic and CJK by code point. See the header: a literal would be mangled by the console
# codepage on Windows PowerShell 5.1.
$CjkA = [char]0x7ECF
$CjkB = [char]0x9A8C
$Replacement = [char]0xFFFD
$ArabicSentence = -join @([char]0x062C, [char]0x0645, [char]0x0644, [char]0x0629, [char]0x0627, [char]0x0631, [char]0x0628, [char]0x064A, [char]0x0629)
$ArabicOther = -join @([char]0x0623, [char]0x062D, [char]0x0631, [char]0x0627, [char]0x0633, [char]0x0628, [char]0x0627, [char]0x062C, [char]0x0627, [char]0x0629)
$ForeignPattern = '[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3040-\u30FF\uAC00-\uD7AF\u1100-\u11FF\u0400-\u04FF\u0370-\u03FF\u1F00-\u1FFF\u0590-\u05FF\uFB1D-\uFB4F]'

# The allowlisted line, named here independently of the script's own table: re-pointing the
# entry must fail this suite rather than silently follow it.
$EntryPath = 'Deep-Audit-Report.md'
$EntryLine = 287
$EntryCodes = @([int]0x7ECF, [int]0x9A8C, [int]0x7ECF, [int]0x9A8C)

# ------------------------------------------------------------------ helpers

function Get-RepoRoot {
    for ($dir = [System.IO.Path]::GetFullPath($PSScriptRoot); $null -ne $dir; $dir = [System.IO.Path]::GetDirectoryName($dir)) {
        if ([System.IO.File]::Exists([System.IO.Path]::Combine($dir, 'scripts', 'check-text-hygiene.ps1'))) { return $dir }
    }
    Fail 'Could not find the repository root: no scripts/check-text-hygiene.ps1 above this file.'
}

$RepoRoot = Get-RepoRoot
$GateScript = [System.IO.Path]::Combine($RepoRoot, 'scripts', 'check-text-hygiene.ps1')
$AuditReport = [System.IO.Path]::Combine($RepoRoot, $EntryPath)

function Get-PowerShellHost {
    # The CI host first, the developer host second; neither being installed is a failure and
    # never a skip. Same order as RazorHygieneTests.PowerShellHost.
    foreach ($candidate in @('pwsh', 'powershell', 'powershell.exe')) {
        if ($null -ne (Get-Command $candidate -ErrorAction SilentlyContinue)) { return $candidate }
    }
    Fail 'No PowerShell host (pwsh, powershell, powershell.exe) is on PATH.'
}

$HostExe = Get-PowerShellHost

function Invoke-Script([string]$ScriptPath, [string[]]$ScriptArguments) {
    $arguments = @('-NoProfile')
    if (-not $HostExe.EndsWith('pwsh', [System.StringComparison]::OrdinalIgnoreCase)) {
        # Windows PowerShell only; pwsh on Linux does not accept the switch.
        $arguments += @('-ExecutionPolicy', 'Bypass')
    }
    $arguments += @('-File', $ScriptPath) + $ScriptArguments

    $lines = & $HostExe @arguments 2>&1
    $exitCode = $LASTEXITCODE
    return New-Object psobject -Property @{
        ExitCode = $exitCode
        Output   = (($lines | ForEach-Object { [string]$_ }) -join [Environment]::NewLine)
    }
}

function Invoke-Gate([string]$Root, [string[]]$ExtraArguments) {
    $arguments = @('-Path', $Root)
    if ($null -ne $ExtraArguments) { $arguments += $ExtraArguments }
    return Invoke-Script $GateScript $arguments
}

function Read-Result([string]$Output) {
    $summary = [regex]::Match($Output, 'result\s+blocking=(?<blocking>\d+)\s+warning=(?<warning>\d+)\s+allowlisted=(?<allowlisted>\d+)')
    if (-not $summary.Success) { Fail ('No result summary in the gate output:' + [Environment]::NewLine + $Output) }
    $allowlist = [regex]::Match($Output, 'allowlist\s+(?<entries>\d+)\s+entr(?:y|ies):\s+(?<matched>\d+)\s+matched,\s+(?<stale>\d+)\s+stale,\s+(?<unscanned>\d+)\s+not scanned')
    if (-not $allowlist.Success) { Fail ('No allowlist summary in the gate output:' + [Environment]::NewLine + $Output) }

    # One count per reported FINDING, not per line of output: a GitHub annotation and its
    # finding line describe the same finding, and counting both would make these ambiguous.
    # The leading `:<line>:` is what makes this exact - it is present on a finding line and on
    # neither the `::notice` annotation nor the prose that mentions the marker afterwards.
    return New-Object psobject -Property @{
        Blocking    = [int]$summary.Groups['blocking'].Value
        Warning     = [int]$summary.Groups['warning'].Value
        Allowlisted = [int]$summary.Groups['allowlisted'].Value
        Entries     = [int]$allowlist.Groups['entries'].Value
        Matched     = [int]$allowlist.Groups['matched'].Value
        Stale       = [int]$allowlist.Groups['stale'].Value
        Unscanned   = [int]$allowlist.Groups['unscanned'].Value
        R8          = ([regex]::Matches($Output, '(?m)^[^\r\n]*:\d+: \[R8\] ')).Count
        R8Allowed   = ([regex]::Matches($Output, '(?m)^[^\r\n]*:\d+: \[R8-ALLOWED\] ')).Count
        R8a         = ([regex]::Matches($Output, '(?m)^[^\r\n]*:\d+: \[R8A\] ')).Count
    }
}

function Write-Bytes([string]$Root, [string]$RelativePath, [byte[]]$Bytes) {
    $full = [System.IO.Path]::Combine($Root, $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $directory = [System.IO.Path]::GetDirectoryName($full)
    if (-not [string]::IsNullOrEmpty($directory)) { [void][System.IO.Directory]::CreateDirectory($directory) }
    [System.IO.File]::WriteAllBytes($full, $Bytes)
    return $full
}

function Write-Text([string]$Root, [string]$RelativePath, [string]$Content) {
    return Write-Bytes $Root $RelativePath $Utf8.GetBytes($Content)
}

function Get-Lines([byte[]]$Bytes) { return ($Utf8.GetString($Bytes) -split "`n") }

function Get-ForeignCodes([string]$Line) {
    # No unary comma around the return value. `, $codes` would stop the pipeline from
    # enumerating, so the caller's @() would collect ONE item - the array - and every
    # .Count in this file would read 1 while looking like it counted the characters.
    $codes = @()
    foreach ($hit in [regex]::Matches($Line, $ForeignPattern)) { $codes += [int]$hit.Value[0] }
    return $codes
}

function Get-Sha256Hex([string]$Line) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Utf8.GetBytes($Line))) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-Hex([byte[]]$Bytes) { return [System.BitConverter]::ToString($Bytes) }

function Copy-EntryFileInto([string]$TargetRoot) {
    # A byte-identical copy at the SAME repository-relative path, which is the only thing that
    # makes the allowlist entry apply to the copy at all. The caller asserts the identity.
    if (-not [System.IO.File]::Exists($AuditReport)) { Fail ('{0} is missing from the repository.' -f $EntryPath) }
    return Write-Bytes $TargetRoot $EntryPath ([System.IO.File]::ReadAllBytes($AuditReport))
}

function Get-TreeSnapshot([string]$Root) {
    # Bytes AND last-write time, because "the gate does not write to the tree" is a claim about
    # more than content: a tool that rewrote a file with identical bytes would still be rewriting.
    $snapshot = @{}
    foreach ($file in [System.IO.Directory]::GetFiles($Root, '*', [System.IO.SearchOption]::AllDirectories)) {
        $snapshot[$file.Substring($Root.Length).Replace('\', '/')] =
            '{0}@{1}' -f (Get-Hex ([System.IO.File]::ReadAllBytes($file))), [System.IO.File]::GetLastWriteTimeUtc($file).Ticks
    }
    return $snapshot
}

function Test-ConformingText([byte[]]$Candidate) {
    # The predicate behind TheScriptIsStillAsciiAndLfOnlyWithoutABom, kept as a function so
    # that case can aim it at bytes which must fail - a predicate that has never rejected
    # anything is indistinguishable from one that always says yes.
    if ($Candidate.Length -eq 0) { return $false }
    if ($Candidate[$Candidate.Length - 1] -ne 0x0A) { return $false }
    if ($Candidate.Length -ge 3 -and $Candidate[0] -eq 0xEF -and $Candidate[1] -eq 0xBB -and $Candidate[2] -eq 0xBF) { return $false }
    if ($Candidate -contains [byte]0x0D) { return $false }
    foreach ($b in $Candidate) { if ($b -gt 0x7F) { return $false } }
    return $true
}

# Recorded before any case runs, and asserted after all of them: the mirror-based mutations
# above are a deliberate choice, and this is what holds them to it.
$script:EntryHashAtStart = Get-Hex ([System.IO.File]::ReadAllBytes($AuditReport))
$script:ScriptHashAtStart = Get-Hex ([System.IO.File]::ReadAllBytes($GateScript))

# ------------------------------------------------------------------ the repository is clean

Test-Case 'TheRepositoryIsCleanWithZeroRealWarningsAndOneReportedAllowlistedLine' {
    $run = Invoke-Gate $RepoRoot
    $result = Read-Result $run.Output

    Assert-Equal 0 $run.ExitCode ('The gate must exit 0 on the repository:' + [Environment]::NewLine + $run.Output)
    Assert-Equal 0 $result.Blocking 'The allowlist must not touch blocking violations.'
    # The point of the whole exercise: the four accepted false positives are out of the warning
    # count, and accounted for separately instead of deleted.
    Assert-Equal 0 $result.Warning 'The real warning count must be 0.'
    Assert-Equal 0 $result.R8 'No R8 warning may remain on the repository.'
    Assert-Equal 0 $result.R8a 'The allowlist entry must match, so there is nothing stale.'
    Assert-Equal 1 $result.Allowlisted 'The allowlisted line must be counted as allowlisted.'
    Assert-Equal 1 $result.Entries 'The script must declare exactly one allowlist entry.'
    Assert-Equal 1 $result.Matched 'That entry must match the committed line.'
    Assert-Equal 0 $result.Stale 'A matching entry is not stale.'
    Assert-Equal 0 $result.Unscanned 'The entry file is in scope for a repository-root scan.'
    Assert-Contains $run.Output 'text-hygiene: PASS' 'The verdict line must be present.'
}

Test-Case 'TheAllowlistedLineIsReportedNotSilenced' {
    $run = Invoke-Gate $RepoRoot
    $result = Read-Result $run.Output

    Assert-Equal 1 $result.R8Allowed 'Exactly one line may be reported as allowlisted.'
    # A GitHub `notice`, so the exception rides along in the PR view - and never `warning` or
    # `error`, because it is not an undecided finding and certainly not a defect.
    Assert-Contains $run.Output ('::notice file={0},line={1},title=R8 allowed::' -f $EntryPath, $EntryLine) 'The allowlisted line needs an annotation.'
    Assert-NotContains $run.Output '::error file=' 'Nothing may be annotated as an error.'
    Assert-NotContains $run.Output '::warning file=' 'An accepted line must not be annotated as a warning.'
    # The marker is distinct from R8, so a grep for '[R8] ' cannot mistake an allowlisted line
    # for a real finding, and the evidence is named in the report instead of shown.
    Assert-Contains $run.Output 'U+7ECF U+9A8C U+7ECF U+9A8C' 'The report must name the code points it allowed.'
    Assert-Contains $run.Output 'Adjudicated evidence, not a defect' 'The report must state why the line is allowed.'
    Assert-Contains $run.Output 'allowlisted=1' 'The summary must carry the allowlisted count.'
    Assert-Contains $run.Output 'warning=0' 'The summary must carry the real warning count separately.'
}

Test-Case 'TheEntryNamesTheCommittedLineAndItsExactContent' {
    # Ties the script's own table to reality without trusting the table: the sha256 and the code
    # points it prints must be the ones actually present on the committed line 287.
    $run = Invoke-Gate $RepoRoot @('-ListExclusions')
    Assert-Equal 0 $run.ExitCode ('-ListExclusions must exit 0:' + [Environment]::NewLine + $run.Output)
    Assert-Contains $run.Output 'R8 per-LINE allowlist' 'The help screen must document the allowlist.'
    Assert-Contains $run.Output 'R8A' 'The staleness rule must appear in the rule table.'

    $printed = [regex]::Match($run.Output, [regex]::Escape($EntryPath) + ':(?<line>\d+)\s+sha256=(?<sha>[0-9a-fA-F]{64})')
    Assert-True $printed.Success 'The help screen must print one path:line + sha256 line per entry.'
    Assert-Equal $EntryLine ([int]$printed.Groups['line'].Value) 'The entry must name the same line this suite expects.'

    $lines = Get-Lines ([System.IO.File]::ReadAllBytes($AuditReport))
    Assert-True ($lines.Length -gt $EntryLine) ('{0} must have more than {1} lines, so a line number identifies one line and not the whole file.' -f $EntryPath, $EntryLine)

    $line = $lines[$EntryLine - 1]
    Assert-Equal (Get-Sha256Hex $line) $printed.Groups['sha'].Value.ToLowerInvariant() 'The table sha256 must be the sha256 of the committed line.'

    $codes = @(Get-ForeignCodes $line)
    Assert-Equal 4 $codes.Count 'The quoted line must carry exactly four foreign-script characters.'
    for ($i = 0; $i -lt $EntryCodes.Count; $i++) {
        Assert-Equal $EntryCodes[$i] $codes[$i] ('Foreign code point {0} on the quoted line does not match the adjudicated evidence.' -f $i)
    }

    $printedCodes = @(([regex]::Match($run.Output, 'foreign code points on that line:(?<codes>[^\r\n]*)').Groups['codes'].Value.Trim()) -split '\s+')
    Assert-Equal 4 $printedCodes.Count 'The table must print all four code points of the evidence.'
    Assert-Equal 'U+7ECF' $printedCodes[0] 'The table must print the first code point of the evidence.'
    Assert-Equal 'U+9A8C' $printedCodes[3] 'The table must print the last code point of the evidence.'

    # The entry explains itself where it is used, and says what would retire it.
    Assert-Contains $run.Output 'why: ' 'The entry must state why the foreign script is legitimate there.'
    Assert-Contains $run.Output 'drop the entry when' 'The help screen must say when to retire an entry.'
    Assert-Contains $run.Output 'An entry must never be widened to a file, a directory or a script.' 'The help screen must forbid widening an entry.'
}

# ------------------------------------------------------------------ the allowlist is not a suppression

Test-Case 'AMutationOfTheQuotedLineStopsTheExceptionApplying' {
    # THE critical case. The allowlist keys on path + line + sha256; this proves the third part
    # is real. The four foreign characters are deliberately LEFT IN PLACE, so the warnings that
    # come back are the rule re-arming over the same evidence, not the rule finding nothing.
    $tree = New-Tree
    try {
        $file = Copy-EntryFileInto $tree
        $original = [System.IO.File]::ReadAllBytes($file)
        $committed = [System.IO.File]::ReadAllBytes($AuditReport)
        Assert-Equal (Get-Hex $committed) (Get-Hex $original) 'The mirror must be byte-identical to the committed file, or it proves nothing.'

        $quiet = Read-Result (Invoke-Gate $tree).Output
        Assert-Equal 0 $quiet.Warning 'The mirror of the committed line must be quiet.'
        Assert-Equal 1 $quiet.Allowlisted 'The mirror must hit the allowlist entry.'

        # Mutation: replace exactly one Arabic character on line 287 with a different Arabic
        # character. Same script, same line number, same four foreign characters, new content.
        $lines = Get-Lines $original
        $before = $lines[$EntryLine - 1]
        $chars = $before.ToCharArray()
        $replaced = 0
        for ($i = 0; $i -lt $chars.Length -and $replaced -eq 0; $i++) {
            if ([int]$chars[$i] -ge 0x0600 -and [int]$chars[$i] -le 0x06FF) {
                $chars[$i] = [char]0x0628
                $replaced++
            }
        }
        Assert-Equal 1 $replaced 'The mutation must change exactly one Arabic character, or it is not the mutation it claims to be.'
        $lines[$EntryLine - 1] = -join $chars
        $after = $lines[$EntryLine - 1]

        Assert-True ((Get-Sha256Hex $after) -ne (Get-Sha256Hex $before)) 'The mutation must change the line hash, which is the whole point.'
        Assert-Equal 4 (@(Get-ForeignCodes $after)).Count 'The mutation must leave all four foreign characters in place.'

        [System.IO.File]::WriteAllBytes($file, $Utf8.GetBytes(($lines -join "`n")))
        $red = Invoke-Gate $tree
        $redResult = Read-Result $red.Output

        Assert-Equal 4 $redResult.R8 'All four warnings must come back when the quoted line is edited.'
        Assert-Equal 0 $redResult.Allowlisted 'The exception must stop applying the moment its target changes.'
        Assert-Equal 0 $redResult.Matched 'The entry must report as unmatched after the mutation.'
        Assert-Equal 1 $redResult.Stale 'An exception that matched nothing must be reported as stale.'
        Assert-Equal 1 $redResult.R8a 'Staleness must be a warning a human has to clear, not silence.'
        Assert-Equal 0 $red.ExitCode 'R8 stays a warning: editing evidence must not turn the build red.'

        # Restore byte-exact and the exception applies again - which is what makes this an
        # allowlist with a condition rather than a switch that got stuck on.
        [System.IO.File]::WriteAllBytes($file, $original)
        Assert-Equal (Get-Hex $original) (Get-Hex ([System.IO.File]::ReadAllBytes($file))) 'The restore must be byte-exact.'

        $green = Read-Result (Invoke-Gate $tree).Output
        Assert-Equal 0 $green.Warning 'Restoring the line must make the gate quiet again.'
        Assert-Equal 0 $green.R8 'No real R8 warning may remain.'
        Assert-Equal 1 $green.Allowlisted 'The entry must match again after the byte-exact restore.'
        Assert-Equal 0 $green.Stale 'The restored entry is not stale.'
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'AMovedQuotedLineStopsTheExceptionApplying' {
    # The purest proof of per-LINE keying: the quoted content moves down one line and is
    # byte-for-byte the same string, so a file-keyed or content-keyed exception would still match.
    $tree = New-Tree
    try {
        $file = Copy-EntryFileInto $tree
        $original = [System.IO.File]::ReadAllBytes($file)
        $lines = Get-Lines $original
        Assert-True ($lines.Length -gt $EntryLine) 'The fixture must have room below the quoted line to move into.'

        $shifted = @('')
        foreach ($line in $lines) { $shifted += $line }
        [System.IO.File]::WriteAllBytes($file, $Utf8.GetBytes(($shifted -join "`n")))

        $red = Invoke-Gate $tree
        $result = Read-Result $red.Output
        Assert-Equal 4 $result.R8 'The warnings must come back when the quoted line moves.'
        Assert-Equal 0 $result.Allowlisted 'An exception must not follow its line up or down the file.'
        Assert-Equal 1 $result.Stale 'The moved line makes the entry stale, and staleness is reported.'

        $finding = [regex]::Match($red.Output, [regex]::Escape($EntryPath) + ':(?<line>\d+): \[R8\] ')
        Assert-True $finding.Success 'The findings must name the file and the line.'
        Assert-Equal ($EntryLine + 1) ([int]$finding.Groups['line'].Value) 'The findings must name the line the quoted text actually moved to.'

        [System.IO.File]::WriteAllBytes($file, $original)
        $green = Read-Result (Invoke-Gate $tree).Output
        Assert-Equal 0 $green.Warning 'Moving the line back must make the gate quiet again.'
        Assert-Equal 1 $green.Allowlisted 'The entry must match again once the line is back.'
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'TheExceptionDoesNotCoverTheRestOfTheFile' {
    # The other half of narrowness: the entry names ONE line, so a foreign-script hit elsewhere
    # in the very same file is still a warning. A file-keyed exception would silence this.
    $tree = New-Tree
    try {
        $file = Copy-EntryFileInto $tree
        $original = [System.IO.File]::ReadAllBytes($file)
        $lines = Get-Lines $original

        # Append one new final line carrying the same two code points inside an Arabic sentence.
        # The last element is the empty string after the final LF, so insert before it.
        $grown = @()
        foreach ($line in $lines[0..($lines.Length - 2)]) { $grown += $line }
        $grown += ($ArabicSentence + ' ' + $CjkA + $CjkB + '.')
        $grown += ''
        [System.IO.File]::WriteAllBytes($file, $Utf8.GetBytes(($grown -join "`n")))

        $run = Invoke-Gate $tree
        $result = Read-Result $run.Output
        Assert-Equal 1 $result.Allowlisted 'The quoted line must still be allowlisted.'
        Assert-Equal 0 $result.Stale 'The entry must still match; nothing about it changed.'
        Assert-Equal 2 $result.R8 'The new line must produce its own two warnings, in the same file.'
        Assert-Equal 2 $result.Warning 'Two warnings are the whole of it: no other rule fires.'
        Assert-Equal 0 $run.ExitCode 'R8 remains a warning.'
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'TheExceptionDoesNotCoverADifferentFileOrDirectory' {
    # The path key, guarded. The quoted line is copied byte-for-byte into a file that shares its
    # name but not its directory, and into a file that shares nothing at all. Neither may be
    # allowlisted: an entry that keyed on the basename, or on the content alone, would swallow
    # both, and the next genuine corruption in either file would go unreported forever.
    $tree = New-Tree
    try {
        $original = [System.IO.File]::ReadAllBytes($AuditReport)
        $null = Write-Bytes $tree ('docs/' + $EntryPath) $original
        $null = Write-Bytes $tree 'Renamed.md' $original

        $run = Invoke-Gate $tree
        $result = Read-Result $run.Output

        Assert-Equal 0 $result.Allowlisted 'The entry is keyed on a repository-relative path, not on a name or a content.'
        Assert-Equal 0 $result.Matched 'No entry may match a different file.'
        Assert-Equal 8 $result.R8 'Both copies carry the quoted line, so both must warn: four characters each.'
        Assert-Equal 0 $run.ExitCode 'R8 remains a warning.'
        # A subtree run cannot tell a missing entry file from a renamed one, so it must say
        # "not scanned" rather than accuse anyone.
        Assert-Equal 1 $result.Unscanned 'The entry file is absent from this subtree.'
        Assert-Equal 0 $result.Stale 'A subtree scan cannot judge the entry, so it must not call it stale.'
        Assert-Equal 0 $result.R8a 'A subtree scan must not raise a staleness warning.'
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'ARenamedEntryFileIsReportedAsStale' {
    # The same hole closed from the other side: with the WHOLE tree scanned, the entry's file
    # being absent means it was renamed, moved or deleted, and the exception now covers nothing.
    # A run rooted at a temp copy of the tree is a whole-tree scan - the script derives its
    # default root from its own location - so it is the honest way to exercise this branch.
    $tree = New-Tree
    try {
        $null = Write-Text $tree 'scripts/check-text-hygiene.ps1' ([System.IO.File]::ReadAllText($GateScript))
        $null = Write-Bytes $tree 'Renamed.md' ([System.IO.File]::ReadAllBytes($AuditReport))

        $run = Invoke-Script ([System.IO.Path]::Combine($tree, 'scripts', 'check-text-hygiene.ps1')) @('-Path', $tree)
        $result = Read-Result $run.Output

        Assert-Equal 0 $run.ExitCode 'A stale entry is a warning, not a block.'
        Assert-Equal 0 $result.Allowlisted 'Nothing may be allowlisted once the file is gone.'
        Assert-Equal 4 $result.R8 'The renamed copy must warn.'
        Assert-Equal 1 $result.Stale 'A whole-tree scan must report an absent entry file as stale.'
        Assert-Equal 1 $result.R8a 'Staleness must be a warning a human has to clear.'
        Assert-Equal 0 $result.Unscanned 'This run saw the whole tree, so nothing is merely unscanned.'
        Assert-Contains $run.Output 'renamed, moved or deleted' 'The warning must name the likely cause.'
    } finally {
        Remove-Tree $tree
    }
}

# ------------------------------------------------------------------ the rules were not weakened

Test-Case 'TheDetectorStillFiresOnASyntheticForeignScriptViolation' {
    # A temp tree outside the repository, so nothing about the real audit report is involved.
    $tree = New-Tree
    try {
        $null = Write-Text $tree 'mixed.md' ('# ' + $ArabicSentence + "`n`n" + $ArabicSentence + ' ' + $CjkA + $CjkB + "." + "`n")
        $run = Invoke-Gate $tree
        $result = Read-Result $run.Output

        Assert-Equal 0 $run.ExitCode 'R8 is a warning, so a synthetic violation does not block.'
        Assert-Equal 2 $result.R8 'Both CJK characters must still be reported.'
        Assert-Equal 0 $result.R8Allowed 'Nothing outside the committed line may be allowlisted.'
        Assert-Equal 0 $result.R8a 'An entry whose file was not scanned is not stale, so a subtree scan does not cry wolf.'
        Assert-Equal 1 $result.Unscanned 'The entry must be counted as not scanned on this run.'
        Assert-Contains $run.Output 'mixed.md:3: [R8]' 'The finding must name the file and the line.'
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'TheGateStillBlocksTheBlockingRules' {
    # One case, four rules: the allowlist touches R8 only, so R2, R3, R4 and R5 must still block
    # and must still be reported. Each fixture is the minimal trigger for its own rule.
    $cases = @(
        @{ Name = 'R2'; File = 'mojibake.md'; Bytes = $Utf8.GetBytes($ArabicSentence + $Replacement + "`n") },
        @{ Name = 'R3'; File = 'bom.md'; Bytes = ([byte[]](0xEF, 0xBB, 0xBF) + $Utf8.GetBytes($ArabicSentence + "`n")) },
        @{ Name = 'R4'; File = 'crlf.md'; Bytes = $Utf8.GetBytes($ArabicSentence + "`r`n" + $ArabicOther + "`n") },
        @{ Name = 'R5'; File = 'no-newline.md'; Bytes = $Utf8.GetBytes($ArabicSentence + "`n" + $ArabicOther) }
    )

    foreach ($case in $cases) {
        $tree = New-Tree
        try {
            $null = Write-Bytes $tree $case.File $case.Bytes
            $run = Invoke-Gate $tree
            Assert-Equal 2 $run.ExitCode ('{0} must still block the gate: {1}' -f $case.Name, $run.Output)
            Assert-Contains $run.Output ('[' + $case.Name + ']') ('{0} must still be reported on {1}.' -f $case.Name, $case.File)
            Assert-Contains $run.Output ('::error file=' + $case.File) ('{0} must still be annotated as an error.' -f $case.Name)
            $result = Read-Result $run.Output
            Assert-Equal 0 $result.R8a ('{0} must not produce a staleness warning on an unrelated tree.' -f $case.Name)
        } finally {
            Remove-Tree $tree
        }
    }
}

Test-Case 'TheGateIsStillReadOnly' {
    $tree = New-Tree
    try {
        $null = Write-Text $tree 'clean.cshtml' ('<p>' + $ArabicSentence + '</p>' + "`n")
        $null = Write-Text $tree 'broken.cshtml' "<p>a</p>   `n"
        $null = Write-Text $tree 'docs/notes.md' ($ArabicSentence + "`n")

        $before = Get-TreeSnapshot $tree
        Assert-Equal 3 $before.Count 'Non-vacuity: three files must be in the snapshot, or an empty comparison would prove nothing.'
        $run = Invoke-Gate $tree
        Assert-Equal 2 $run.ExitCode 'The broken fixture must actually fire, or the read-only check is vacuous.'
        $after = Get-TreeSnapshot $tree

        Assert-Equal $before.Count $after.Count 'The gate must not add or remove files.'
        foreach ($key in $before.Keys) {
            Assert-True $after.ContainsKey($key) ('The gate removed {0}.' -f $key)
            Assert-Equal $before[$key] $after[$key] ('The gate modified {0}: bytes or last-write time changed.' -f $key)
        }
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'TheScriptStillContainsNoWriteApi' {
    # The behavioural proof is TheGateIsStillReadOnly; this one survives a reviewer who never
    # runs the suite, and it covers the allowlist code specifically.
    $source = [System.IO.File]::ReadAllText($GateScript)
    foreach ($forbidden in @('Set-Content', 'Add-Content', 'Out-File', 'New-Item', 'Remove-Item',
                             'Copy-Item', 'Move-Item', 'Rename-Item', 'WriteAllBytes', 'WriteAllText',
                             '::Delete', '::Move', '::Copy')) {
        Assert-NotContains $source $forbidden ('The gate must not contain a write API ({0}): it names problems, it does not fix them.' -f $forbidden)
    }
}

Test-Case 'TheScriptIsStillAsciiAndLfOnlyWithoutABom' {
    $bytes = [System.IO.File]::ReadAllBytes($GateScript)

    # Non-vacuity first: the predicate must reject a non-ASCII byte and a CR, and accept a
    # conforming sample. A predicate that has never rejected anything cannot be evidence.
    Assert-False (Test-ConformingText ($Utf8.GetBytes($CjkA.ToString()))) 'The predicate must reject a non-ASCII byte.'
    Assert-False (Test-ConformingText ([byte[]](0x3C, 0x70, 0x3E, 0x0D, 0x0A))) 'The predicate must reject a CR.'
    Assert-False (Test-ConformingText ([byte[]](0xEF, 0xBB, 0xBF, 0x0A))) 'The predicate must reject a BOM.'
    Assert-True (Test-ConformingText ([byte[]](0x3C, 0x70, 0x3E, 0x0A))) 'The predicate must accept a conforming sample.'

    Assert-True (Test-ConformingText $bytes) ('The gate script must be ASCII, LF-only, BOM-less and end with one LF: {0} non-ASCII byte(s), {1} CR byte(s).' -f (@($bytes | Where-Object { $_ -gt 0x7F }).Count), (@($bytes | Where-Object { $_ -eq 0x0D }).Count))
}

Test-Case 'TheAllowlistRejectsAnEntryForAnyRuleOtherThanR8' {
    # A mistyped Rule must fail loudly rather than produce an entry that silently never matches.
    # Non-vacuity is built in: the unmodified copy is run first, through the same argument shape.
    $tree = New-Tree
    try {
        $source = [System.IO.File]::ReadAllText($GateScript)
        $mistyped = $source.Replace("Rule          = 'R8'", "Rule          = 'R9'")
        Assert-True ($mistyped -cne $source) 'The fixture must actually mistype the Rule value.'
        $copy = Write-Text $tree 'check-text-hygiene.ps1' $mistyped

        $red = Invoke-Script $copy @('-Path', $RepoRoot)
        Assert-Equal 1 $red.ExitCode ('A mistyped allowlist entry must be a usage error:' + [Environment]::NewLine + $red.Output)
        Assert-Contains $red.Output 'bad allowlist entry' 'The failure must say what is wrong.'
        Assert-Contains $red.Output "Rule must be 'R8'" 'The failure must name the rule that may not be allowlisted.'

        $control = Invoke-Script (Write-Text $tree 'control.ps1' $source) @('-Path', $RepoRoot)
        Assert-Equal 0 $control.ExitCode ('The unmodified script must pass, or the case above proves nothing:' + [Environment]::NewLine + $control.Output)
    } finally {
        Remove-Tree $tree
    }
}

Test-Case 'AMissingPathIsAUsageErrorNotAViolation' {
    $missing = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'nvs-hygiene-absent-' + [guid]::NewGuid().ToString('n'))
    $run = Invoke-Gate $missing
    Assert-Equal 1 $run.ExitCode 'A bad -Path is a usage error, not a text-file verdict.'
    Assert-NotContains $run.Output 'text-hygiene: PASS' 'A usage error must not print a pass.'
}

# ------------------------------------------------------------------ the suite itself

Test-Case 'EveryCaseLeavesTheRepositoryUntouched' {
    $entryHashNow = Get-Hex ([System.IO.File]::ReadAllBytes($AuditReport))
    $scriptHashNow = Get-Hex ([System.IO.File]::ReadAllBytes($GateScript))

    Assert-True ($entryHashNow -cne '') 'The audit report must still be readable.'
    Assert-True ($scriptHashNow -cne '') 'The gate script must still be readable.'
    # The two cases above are tautologies on their own; these two are the assertion. The
    # mutation cases deliberately work on temp mirrors, and this is what holds them to it.
    Assert-Equal $script:EntryHashAtStart $entryHashNow 'The audit report must be byte-identical to the state this suite found it in.'
    Assert-Equal $script:ScriptHashAtStart $scriptHashNow 'The gate script must be byte-identical to the state this suite found it in.'
}

# ------------------------------------------------------------------ summary

Write-Output ''
Write-Output ('hygiene-gate: host      {0}' -f $HostExe)
Write-Output ('hygiene-gate: root      {0}' -f $RepoRoot)
Write-Output ('hygiene-gate: result    {0} passed, {1} failed' -f $script:Passed, $script:Failed)

if ($script:Failed -gt 0) {
    Write-Output 'hygiene-gate: FAIL'
    exit 1
}

Write-Output 'hygiene-gate: PASS'
exit 0
