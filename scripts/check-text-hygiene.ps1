<#
.SYNOPSIS
    Repository-hygiene gate for the text files `dotnet format` never sees: .cshtml and .md.

.DESCRIPTION
    WHY THIS EXISTS
    `dotnet format NewVixSmart.slnx --verify-no-changes` is a live CI gate, but it inspects
    C# only. Measured, not assumed: a trailing-whitespace violation injected into
    src/NewVixSmart.Web/Views/Shared/_Layout.cshtml exits 0, while the same violation in a .cs
    file exits 2 and names the file. So the file type that carries the entire user-facing
    interface - the Razor views, which is where this repository's Arabic RTL markup and its
    accessibility attributes live - was outside every quality gate. This script is that gate.

    IT IS A GATE, NOT A REPORT
    Exit codes:
        0  no blocking violation (warnings may still have been printed)
        1  usage or I/O error (bad -Path, unreadable file)
        2  at least one BLOCKING violation
    It never writes to the tree. Fixing is the editor's job: .editorconfig already declares
    charset = utf-8, end_of_line = lf, insert_final_newline = true and trim_trailing_whitespace,
    so a save in any conforming editor clears rules R3-R6 by itself. A tool that rewrote 160
    files from inside a CI step would be a much worse thing to trust than one that names them.

    RULES
    ID    Severity  Rule
    ----  --------  ---------------------------------------------------------------
    R1    BLOCK     Strict UTF-8 decodable. A file that cannot be decoded is a hard
                    stop, not a warning: if the bytes are not UTF-8 then every Arabic
                    literal inside them is already unverifiable.
    R2    BLOCK     No U+FFFD REPLACEMENT CHARACTER anywhere. This is the signature of a
                    mojibake incident - a file that was decoded as one codepage and
                    re-encoded as another - and this repository has already had one.
    R3    BLOCK     No UTF-8 BOM. The repo mandates `charset = utf-8`, which means
                    without BOM, and a BOM also defeats byte comparison in review.
    R4    BLOCK     LF line endings only. No CR at all: neither CRLF nor a lone CR.
    R5    BLOCK     File ends with exactly one newline; no trailing blank lines.
    R6    BLOCK     No trailing whitespace on any line. ONE documented exemption: in
                    Markdown, exactly two trailing spaces is the hard line break idiom
                    ("line one  \nline two"), so two-space breaks are not violations.
                    Every other trailing space or tab is.
    R7    BLOCK     No TAB in the leading whitespace of a .cshtml line.
    R8    WARNING   An Arabic-dominant file must not carry characters from an unrelated
                    script (CJK, Hangul, Cyrillic, Greek, Hebrew). Fenced code blocks and
                    inline code spans are excluded, as are <pre>/<code>/Razor-comment
                    regions in .cshtml. WARNING and not BLOCK on purpose: this is a
                    heuristic, and a false positive here is expensive. A commit message
                    quoted verbatim, or an audit report quoting the exact defect it
                    documents, are both legitimate. It fires on real defects too - see
                    docs/AUDIT-DEEP-2026-09-29.md, which carried a Russian word inside an
                    Arabic sentence, and AGENTS.md, which carried a Chinese one.
    R9    BLOCK     No TAB in the leading whitespace of a .md line. Same predicate as R7,
                    split per extension so the report maps one-to-one onto the audit.
    R10   WARNING   A TAB outside leading whitespace - i.e. used as a column separator
                     rather than as indentation. Reported, not blocked: it is a layout
                     choice some Markdown tables make.
    R8A   WARNING   An R8 per-line allowlist entry matched nothing - see
                     THE R8 LINE ALLOWLIST below. Reported as a warning, because a stale
                     exception is a maintenance defect and must not read as a clean run.

    WHY THAT BLOCK/WARN SPLIT
    Every BLOCK rule is a byte-level property with no legitimate counterexample. No valid
    Razor view or Markdown file needs a BOM, a CR, a missing final newline, trailing
    whitespace, or a tab in its indentation, so a BLOCK hit can never be a matter of taste
    and can always be cleared mechanically. R1 and R2 are blocks for the opposite reason:
    they are the only two rules that detect data loss rather than style, and data loss is
    not negotiable. Both WARNING rules can fire on correct text - R8 because a document may
    legitimately quote a foreign string, R10 because a tab may be a deliberate column
    separator - and a gate that can be blocked by a correct file is a gate people learn to
    bypass. Warnings are printed in full, on every run, so they are still impossible to
    miss; they just do not turn the build red for a judgement call.

    THE R8 LINE ALLOWLIST - WHY IT EXISTS, AND WHY IT IS NOT A SUPPRESSION
    R8 is the one rule that can fire on perfectly correct text, because a document may
    legitimately quote a foreign string. After the 2026-09-30 adjudication, four hits
    remained and all four were a false positive on a single line: Deep-Audit-Report.md:287
    is the audit finding ABOUT a mojibake incident and it quotes the damaged header
    verbatim as its own evidence. The two available "fixes" are both wrong. Deleting the
    evidence to quiet the linter would destroy the finding. Weakening R8 globally is not
    an option either, because R8 is the reason four stray CJK characters were caught in
    docs/AUDIT-DEEP-2026-09-29.md in the first place. So the exception is one named,
    hashed, per-LINE entry in $R8LineAllowlist, and it has three properties that separate
    an allowlist from a suppression:
      - It is keyed on the repository-relative path AND the line number AND the SHA-256 of
        that exact line's UTF-8 bytes (no terminator). An entry therefore cannot quietly
        grow into a file-level or directory-level exemption, and the instant the quoted
        line is edited or moves, the entry stops matching, the R8 warnings return, and R8A
        reports the entry as stale.
      - It is REPORTED, never silenced. A match prints an [R8-ALLOWED] finding line, a
        GitHub `notice` annotation, and the entry summary under "text-hygiene: allowlist",
        so an allowlisted line is never mistaken for a clean one and the `warning=` count
        keeps its meaning of "a human still has to decide this".
      - An entry that matches NOTHING is itself reported, as a WARNING under R8A. An entry whose
        file was not scanned is only counted as "not scanned", because a `-Path <subtree>` run
        cannot judge it - but a whole-tree scan that does not contain the file at all reports
        the entry as stale, so renaming or moving the quoted file is loud too. Silence bought
        by a stale exception is the failure mode this is built to prevent.
    R8 is the only rule with an allowlist, and deliberately so: every BLOCK rule is a
    byte-level property with no legitimate counterexample, which is the reason the
    block/warn split exists in the first place. A BLOCK rule that ever needed an exception
    would be evidence that the rule is wrong, not that the file is.

    HEURISTIC REFINEMENT: RECOMMENDED, DELIBERATELY NOT SHIPPED
    The obvious generalisation is to rank a foreign-script hit lower when it sits inside a
    fenced block, an inline code span, or a blockquote that is quoting a finding; the first
    two masked regions already exist in Get-MaskedText. It is not adopted, for a reason
    that is measured rather than aesthetic: the real corruption this rule exists for never
    arrived in any of those contexts. The Chinese pair spliced into AGENTS.md:34, the
    Russian word in docs/AUDIT-DEEP-2026-09-29.md, and the Russian and Chinese words in
    docs/BUILD-PLAN-README.md were all in ORDINARY BODY PROSE and in Markdown table rows.
    A quoting-context refinement would have missed every one of them - all four of the
    adjudications - while opening a new hole, namely a corrupted blockquote quietly
    down-ranked. A heuristic is also not greppable, not reviewable in a diff, and not
    mutation-testable, whereas a hashed per-line entry is all three. Revisit this if the
    same false-positive shape appears a second time, and measure it against the corpus of
    known corruptions above rather than shipping it blind.

    WHAT IS DELIBERATELY *NOT* GATED, AND WHY
    - Migrations/*.Designer.cs (38 files). These carry `// <auto-generated />` and are
      REWRITTEN BY `dotnet ef migrations add`. Their encoding and line endings are the EF
      tool's output, not ours, so normalising them would only produce churn the next
      migration undoes. Do not "fix" them, and do not add them here: the gate would then
      be red after every future `migrations add`.
    - wwwroot/lib. Vendored third-party assets (bootstrap, jquery, fonts). Same reasoning:
      they are re-downloaded by their upstream, so a local edit is not durable.
    - Build and tool output: bin, obj, node_modules, .git, .vs, .playwright-cli,
      TestResults, test-results, artifacts, screenshots, backups.
    - The RazorHygieneFixtures directory: this gate's own test fixtures, which are
      deliberately broken. They live outside the repository when the test suite runs (a
      temp tree), and the directory name is listed below as well, so the gate can never
      fail on its own test data by either route.

.PARAMETER Path
    Directory to scan. Defaults to the repository root (two levels up from this script).

.PARAMETER ListExclusions
    Print the effective exclusion list and the rule table, then exit 0 without scanning.

.PARAMETER Quiet
    Suppress the header and summary lines. Finding lines and GitHub Actions annotations
    are still printed, so this only reduces noise in a transcript.

.EXAMPLE
    pwsh -NoProfile -File scripts/check-text-hygiene.ps1
    The gate. This exact command is the CI step.

.EXAMPLE
    .\scripts\check-text-hygiene.ps1
    The same gate from Windows PowerShell on a developer machine.

.EXAMPLE
    .\scripts\check-text-hygiene.ps1 -ListExclusions
    What is scanned, what is not, and why.
#>

[CmdletBinding()]
param(
    [string]$Path,

    [switch]$ListExclusions,

    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- configuration

# Extensions in scope. These are the two that hold the entire user-facing interface and
# the entire written record of the project, and the two that `dotnet format` never sees.
$ScopedExtensions = @('.cshtml', '.md')

# Directory names pruned at any depth. Build output, tool caches, local artifacts, and
# this gate's own fixture directory. Named explicitly so a reader can see the boundary
# instead of inferring it, and so the CI step can never trip over its own test data.
$ExcludedDirectoryNames = @(
    '.git',
    '.vs',
    '.playwright-cli',
    'RazorHygieneFixtures',   # this gate's own deliberately-broken test fixtures
    'TestResults',
    'artifacts',
    'backups',
    'bin',
    'node_modules',
    'obj',
    'screenshots',
    'test-results'
)

# Repository-relative directory prefixes pruned in full. Only vendored third-party code,
# for the same reason Migrations/*.Designer.cs is out of scope: we do not author it and
# re-downloading it discards any local edit.
$ExcludedRelativePaths = @(
    'src/NewVixSmart.Web/wwwroot/lib'
)

# Rules, in the order they are reported. Severity drives the exit code only.
$Rules = @(
    @{ Id = 'R1';  Severity = 'BLOCK';  Name = 'utf8-undecodable' }
    @{ Id = 'R2';  Severity = 'BLOCK';  Name = 'replacement-character' }
    @{ Id = 'R3';  Severity = 'BLOCK';  Name = 'utf8-bom' }
    @{ Id = 'R4';  Severity = 'BLOCK';  Name = 'carriage-return' }
    @{ Id = 'R5';  Severity = 'BLOCK';  Name = 'final-newline' }
    @{ Id = 'R6';  Severity = 'BLOCK';  Name = 'trailing-whitespace' }
    @{ Id = 'R7';  Severity = 'BLOCK';  Name = 'tab-indentation-cshtml' }
    @{ Id = 'R8';  Severity = 'WARNING'; Name = 'foreign-script' }
    @{ Id = 'R9';  Severity = 'BLOCK';  Name = 'markdown-hard-tab' }
    @{ Id = 'R10'; Severity = 'WARNING'; Name = 'stray-tab' }
    @{ Id = 'R8A'; Severity = 'WARNING'; Name = 'allowlist-stale' }
)

# Per-LINE exceptions for R8, the only rule that can fire on correct text. Read
# "THE R8 LINE ALLOWLIST" in the header above before adding, widening, re-pointing or
# deleting an entry: that section says why this is not a suppression, and this file is the
# only place the reason is recorded.
#
# A match needs ALL THREE of these (see Get-AllowedR8Hit):
#   Path    repository-relative, forward slashes, compared Ordinal - the same string the
#           finding line prints, so the entry is visible in the report next to the evidence.
#   Line    1-based line number in the decoded file, i.e. the number R8 itself reports.
#   Sha256  SHA-256 of the UTF-8 bytes of that one line, excluding its line terminator,
#           computed with [System.Security.Cryptography.SHA256] over $Utf8NoBom bytes.
# ExpectedCodes is a redundant but deliberate second check: the exact ordered sequence of
# foreign code points R8 hits on that line. Verifying it as well keeps an entry
# self-describing, and stops a hand-edited hash from quietly blessing a line that carries a
# different foreign script than the one that was adjudicated.
#
# Only R8 may be named here, and the startup check below rejects any other value with exit 1
# instead of letting a typo produce an entry that silently never matches anything.
$R8LineAllowlist = @(
    # WHAT IS ON THAT LINE, BY CODE POINT. This script is ASCII-only on purpose - Windows
    # PowerShell 5.1 parses a BOM-less script with the console codepage, so a non-ASCII
    # character here would arrive as mojibake on exactly the host a developer uses to run
    # the gate - which is why the evidence is named by number instead of shown:
    #   U+00AB, then the Arabic definite article U+0627 U+0644 U+062A, then the two CJK
    #   ideographs U+7ECF and U+9A8C, then ASCII "lightly", then U+2014 EM DASH, then the
    #   same run a second time, then U+00BB. Four R8 hits, one line: U+7ECF twice and
    #   U+9A8C twice, which is the whole of the 4 warnings this repository used to print.
    #
    # WHY IT IS LEGITIMATE HERE:
    #   That is the verbatim damaged title of docs/DEPLOY-AZURE.md:1, quoted by the audit
    #   report as the evidence for its own finding about a mojibake round-trip. It is the
    #   only place in the repository where those two characters are supposed to appear, and
    #   they are there because a report cannot show what it found without showing it.
    #
    # WHAT WOULD JUSTIFY DELETING THIS ENTRY:
    #   - the finding is closed and its evidence is reworded out of the report, or
    #   - the quoted header is repaired inside the audit report, or
    #   - the entry has gone STALE. That is reported on its own, as an R8A WARNING, so a
    #     stale exception is a warning a human has to clear - never silence.
    # Do NOT widen it to the file, to the directory, or to "any CJK in a .md". R8 is the only
    # reason four stray CJK characters were caught in docs/AUDIT-DEEP-2026-09-29.md.
    @{
        Rule          = 'R8'
        Path          = 'Deep-Audit-Report.md'
        Line          = 287
        Sha256        = 'c7ff53e883f3454249edd4949de96f6660198776eb8d7250ca0b38b9a63f47ce'
        ExpectedCodes = @(0x7ECF, 0x9A8C, 0x7ECF, 0x9A8C)
        Reason        = 'Audit finding about a mojibake incident, quoting the damaged header verbatim as its evidence.'
    }
)

# Per-file cap on repeated findings of one rule, so one generated file cannot bury the
# report. The cap is stated in the output rather than applied silently.
$MaxPerRulePerFile = 5

$Utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

# Script ranges as regex character classes. R8 runs over every character of every file, and a
# PowerShell `foreach ($ch in $text.ToCharArray())` with a function call per character took 110
# seconds for this repository. These are matched by the regex engine in native code instead,
# which is the same answer an order of magnitude faster. The code points are identical to the
# per-character predicates they replaced, ranges and all.
$ArabicPattern = '[\u0600-\u06FF\u0750-\u077F\u08A0-\u08FF\uFB50-\uFDFF\uFE70-\uFEFF]'
$LatinPattern = '[A-Za-z\u00C0-\u024F]'
# CJK, Hiragana/Katakana, Hangul, Cyrillic, Greek, Hebrew - in that order, so the family
# reported for a given code point is stable.
$ForeignPattern = '[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3040-\u30FF\uAC00-\uD7AF' +
                  '\u1100-\u11FF\u0400-\u04FF\u0370-\u03FF\u1F00-\u1FFF\u0590-\u05FF\uFB1D-\uFB4F]'

# Diagnostics carry repository-relative paths and `U+XXXX` escapes only, so they stay
# readable under any console codepage. Still, ask the host for UTF-8 so that a path which
# does contain non-ASCII (two audit reports are named in Arabic) is not mangled on the way
# out. Wrapped: some hosts refuse to change it.
try { [Console]::OutputEncoding = $Utf8NoBom } catch { }

# ---------------------------------------------------------------- helpers

function Write-Line([string]$Text) { Write-Output $Text }

function Get-Note([string]$Text) {
    if ($Quiet) { return }
    Write-Line $Text
}

function Get-RelativePath([string]$Root, [string]$Full) {
    # [System.IO.Path]::GetRelativePath does not exist on .NET Framework, which is what
    # Windows PowerShell 5.1 runs on, so the prefix is stripped by hand.
    $root = $Root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ($Full.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Full.Substring($root.Length + 1).Replace('\', '/')
    }
    return $Full.Replace('\', '/')
}

function Get-LineOfByteOffset([byte[]]$Bytes, [int]$Offset) {
    $line = 1
    for ($i = 0; $i -lt $Offset -and $i -lt $Bytes.Length; $i++) {
        if ($Bytes[$i] -eq 10) { $line++ }
    }
    return $line
}

# ---------------------------------------------------------------- file discovery

function Get-ScopedFiles([string]$Root) {
    # Hand-rolled walk rather than Get-ChildItem -Recurse so the excluded directory names
    # prune the descent instead of filtering results afterwards.
    $excludedNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($n in $ExcludedDirectoryNames) { $null = $excludedNames.Add($n) }

    $extSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($e in $ScopedExtensions) { $null = $extSet.Add($e) }

    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Root)
    $found = New-Object System.Collections.Generic.List[string]

    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        foreach ($child in [System.IO.Directory]::GetDirectories($dir)) {
            $name = [System.IO.Path]::GetFileName($child)
            if ($excludedNames.Contains($name)) { continue }
            $rel = Get-RelativePath $Root $child
            $pruned = $false
            foreach ($prefix in $ExcludedRelativePaths) {
                if ($rel.Equals($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                    $rel.StartsWith($prefix + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $pruned = $true
                    break
                }
            }
            if ($pruned) { continue }
            $stack.Push($child)
        }

        foreach ($file in [System.IO.Directory]::GetFiles($dir)) {
            if ($extSet.Contains([System.IO.Path]::GetExtension($file))) { $found.Add($file) }
        }
    }

    # Ordinal sort so the report is byte-identical run to run regardless of the order the
    # filesystem hands directories back - a gate whose output reorders itself is a gate
    # nobody can read a diff of.
    $sorted = $found.ToArray()
    [Array]::Sort($sorted, [System.StringComparer]::Ordinal)
    return $sorted
}

# ---------------------------------------------------------------- masking

function Get-MaskedText([string]$Text, [string]$Extension) {
    # Blanks out the regions where a foreign script is legitimate. Every masked character
    # becomes a space EXCEPT the line breaks, which are kept: callers count newlines to turn
    # a character offset into a file:line pair, and collapsing them would report a line number
    # from the middle of a code block.
    $blank = {
        param($m)
        $builder = New-Object System.Text.StringBuilder $m.Value.Length
        foreach ($c in $m.Value.ToCharArray()) {
            if ($c -eq "`n" -or $c -eq "`r") { [void]$builder.Append($c) } else { [void]$builder.Append(' ') }
        }
        return $builder.ToString()
    }

    if ($Extension -eq '.md') {
        # Fenced code blocks, and inline code spans, are the two places a Markdown document
        # is expected to contain a foreign string.
        $pattern = '(?ms)^[ \t]*(?:```|~~~).*?^[ \t]*(?:```|~~~)[ \t]*$|`+[^`\r\n]*`+'
        return [regex]::Replace($Text, $pattern, $blank)
    }
    # The Razor analogue of a code fence: literal and rendered code, and Razor comments.
    $pattern = '(?is)<pre\b.*?</pre>|<code\b.*?</code>|@\*.*?\*@'
    return [regex]::Replace($Text, $pattern, $blank)
}

function Get-ScriptFamily([int]$Code) {
    if (($Code -ge 0x4E00 -and $Code -le 0x9FFF) -or ($Code -ge 0x3400 -and $Code -le 0x4DBF) -or
        ($Code -ge 0xF900 -and $Code -le 0xFAFF) -or ($Code -ge 0x3040 -and $Code -le 0x30FF) -or
        ($Code -ge 0xAC00 -and $Code -le 0xD7AF) -or ($Code -ge 0x1100 -and $Code -le 0x11FF)) { return 'CJK/Hangul' }
    if ($Code -ge 0x0400 -and $Code -le 0x04FF) { return 'Cyrillic' }
    if (($Code -ge 0x0370 -and $Code -le 0x03FF) -or ($Code -ge 0x1F00 -and $Code -le 0x1FFF)) { return 'Greek' }
    if (($Code -ge 0x0590 -and $Code -le 0x05FF) -or ($Code -ge 0xFB1D -and $Code -le 0xFB4F)) { return 'Hebrew' }
    return $null
}

# ---------------------------------------------------------------- R8 allowlist matching

function Get-LineSha256([string]$Line) {
    # The line's own bytes, no terminator, no BOM - the same string $lines holds after
    # `$text -split "\n"`. Hashing the line and not the file is what makes the exception
    # per-line: an entry carries the fingerprint of the evidence it was written for.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Utf8NoBom.GetBytes($Line))) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-R8LineSummary([string]$Line) {
    # Diagnostic text only, and deliberately codepoint escapes: the report has to stay
    # readable under any console codepage, and a foreign character in a diagnostic is the
    # very thing the diagnostic is about.
    $codes = @()
    foreach ($hit in [regex]::Matches($Line, $ForeignPattern)) { $codes += ('U+{0:X4}' -f [int]$hit.Value[0]) }
    if ($codes.Count -eq 0) { return 'no foreign-script character' }
    return ('{0} ({1} character(s))' -f ($codes -join ' '), $codes.Count)
}

function Test-AllowedR8Line([string]$Relative, [int]$LineNumber, [string]$Line) {
    # Returns the matching allowlist entry, or $null. All THREE parts must agree; the third
    # is the one that matters, because it is what makes this an allowlist rather than a
    # suppression. If the line is edited, the hash disagrees and the entry stops applying -
    # the R8 warnings come straight back and the entry is then reported as stale (R8A).
    foreach ($entry in $R8LineAllowlist) {
        if ([string]$entry.Path -cne $Relative) { continue }
        if ([int]$entry.Line -ne $LineNumber) { continue }
        if ([string]$entry.Sha256 -ine (Get-LineSha256 $Line)) { continue }

        # Redundant on purpose, and the reason an entry reads as documentation: the recorded
        # code points must be exactly the foreign characters R8 finds on that line, so a
        # hand-edited hash cannot bless a line carrying a different script than the one that
        # was adjudicated.
        $expected = @($entry.ExpectedCodes)
        $found = [regex]::Matches($Line, $ForeignPattern)
        if ($found.Count -ne $expected.Count) { continue }
        $codesAgree = $true
        for ($i = 0; $i -lt $expected.Count; $i++) {
            if ([int]$found[$i].Value[0] -ne [int]$expected[$i]) { $codesAgree = $false; break }
        }
        if (-not $codesAgree) { continue }

        $entry['Matched'] = $true
        return $entry
    }
    return $null
}

# ---------------------------------------------------------------- finding sink

$blocking = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$allowlisted = New-Object System.Collections.Generic.List[string]
# Which repository-relative paths this run actually read. An allowlist entry whose file was
# never scanned cannot be judged stale - the scan was incomplete, not the entry wrong - and
# without this a `-Path <subtree>` run would cry wolf on every entry.
$scannedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$scanned = 0
$ruleCounts = @{}
$rulesFired = New-Object 'System.Collections.Generic.SortedSet[string]' ([System.StringComparer]::Ordinal)

function Add-Finding([string]$Relative, [int]$Line, [string]$RuleId, [string]$Severity, [string]$Message) {
    $rulesFired.Add($RuleId) | Out-Null
    if ($ruleCounts.ContainsKey($RuleId)) { $ruleCounts[$RuleId]++ } else { $ruleCounts[$RuleId] = 1 }
    $text = ('{0}:{1}: [{2}] {3}' -f $Relative, $Line, $RuleId, $Message)
    if ($Severity -eq 'BLOCK') {
        $blocking.Add($text) | Out-Null
        # GitHub Actions annotation, so the finding is attached to the file in the PR view
        # instead of only existing in a log. Contains no `${{ }}`, by design.
        Write-Line ('::error file={0},line={1},title={2}::{3} {4}' -f $Relative, $Line, $RuleId, $RuleId, $Message)
    } else {
        $warnings.Add($text) | Out-Null
        Write-Line ('::warning file={0},line={1},title={2}::{3} {4}' -f $Relative, $Line, $RuleId, $RuleId, $Message)
    }
    Write-Line $text
}

function Add-AllowedFinding([string]$Relative, [int]$Line, $Entry, [string]$Message) {
    # Deliberately NOT Add-Finding. An allowlisted hit is not a finding, so it must never reach
    # $warnings or $ruleCounts: that is what keeps `warning=` meaningful as "a human has not
    # decided this yet". It is still printed in full, on every run, with its own marker, its own
    # GitHub level (`notice`, not `warning` or `error`), and its own summary counter - so an
    # allowlisted line is reported, never silenced, and is never mistaken for a clean one.
    $text = ('{0}:{1}: [R8-ALLOWED] {2}' -f $Relative, $Line, $Message)
    $allowlisted.Add($text) | Out-Null
    Write-Line ('::notice file={0},line={1},title=R8 allowed::[R8-ALLOWED] {2}' -f $Relative, $Line, $Message)
    Write-Line $text
}

function Add-StaleAllowlistFinding($Entry, [string]$Message) {
    # A real WARNING, under its own rule id. An exception that matches nothing is how a blanket
    # suppression hides in plain sight, so it counts as an undecided finding: it is counted in
    # `warning=` and `per rule`, it never blocks, and it cannot be missed in the log.
    $text = ('{0}:{1}: [R8A] {2}' -f $Entry.Path, [int]$Entry.Line, $Message)
    $warnings.Add($text) | Out-Null
    if ($ruleCounts.ContainsKey('R8A')) { $ruleCounts['R8A']++ } else { $ruleCounts['R8A'] = 1 }
    $rulesFired.Add('R8A') | Out-Null
    Write-Line ('::warning file={0},line={1},title=R8A stale allowlist entry::[R8A] {2}' -f $Entry.Path, [int]$Entry.Line, $Message)
    Write-Line $text
}

# ---------------------------------------------------------------- -ListExclusions

if ($ListExclusions) {
    Write-Line 'text-hygiene: rule table'
    foreach ($r in $Rules) {
        Write-Line ('  {0,-4} {1,-8} {2}' -f $r.Id, $r.Severity, $r.Name)
    }
    Write-Line ''
    Write-Line 'text-hygiene: extensions in scope'
    Write-Line ('  ' + ($ScopedExtensions -join ', '))
    Write-Line ''
    Write-Line 'text-hygiene: excluded directory NAMES (pruned at any depth)'
    foreach ($n in $ExcludedDirectoryNames) { Write-Line ('  ' + $n) }
    Write-Line ''
    Write-Line 'text-hygiene: excluded repository-relative PATHS'
    foreach ($p in $ExcludedRelativePaths) { Write-Line ('  ' + $p) }
    Write-Line ''
    Write-Line 'text-hygiene: NOT gated on purpose'
    Write-Line '  src/NewVixSmart.Web/Migrations/*.Designer.cs - EF-generated and rewritten by'
    Write-Line '    `dotnet ef migrations add`. Gating them would go red on the next migration.'
    Write-Line '  src/NewVixSmart.Web/wwwroot/lib - vendored third-party (bootstrap, jquery, fonts).'
    Write-Line ''
    Write-Line 'text-hygiene: R8 per-LINE allowlist (repository-relative path + line + sha256 of that line)'
    if ($R8LineAllowlist.Count -eq 0) {
        Write-Line '  (empty - no R8 false positive has been adjudicated)'
    }
    foreach ($entry in $R8LineAllowlist) {
        $codes = @()
        foreach ($code in @($entry.ExpectedCodes)) { $codes += (' U+{0:X4}' -f [int]$code) }
        Write-Line ('  {0}:{1}   sha256={2}' -f $entry.Path, $entry.Line, $entry.Sha256)
        Write-Line ('    foreign code points on that line:{0}' -f ($codes -join ''))
        Write-Line ('    why: {0}' -f $entry.Reason)
        Write-Line '    drop the entry when the finding is closed, when the evidence is reworded out'
        Write-Line '    of the report, or when it goes stale - a stale entry is an R8A WARNING,'
        Write-Line '    never silence.'
        Write-Line '    an entry whose file was not scanned at all is counted as neither matched nor'
        Write-Line '    stale: a -Path <subtree> run cannot judge it, and must not cry wolf. When the'
        Write-Line '    whole tree IS scanned and the file is absent, that is an R8A WARNING - the file'
        Write-Line '    was renamed, moved or deleted, and the exception now covers nothing.'
    }
    Write-Line '  An entry must never be widened to a file, a directory or a script. R8 is the'
    Write-Line '  only reason four stray CJK characters were caught in docs/AUDIT-DEEP-2026-09-29.md.'
    exit 0
}

# ---------------------------------------------------------------- allowlist validation
#
# A mistyped entry that silently matches nothing is precisely the failure this mechanism
# exists to prevent, so the ways to write one are rejected before any file is read: exit 1,
# the usage-error code, because a broken allowlist is an operator error and not a verdict on
# a text file. Each check below corresponds to a way the three-part match could be faked.
foreach ($entry in $R8LineAllowlist) {
    $why = $null
    if ([string]$entry.Rule -cne 'R8') {
        $why = "Rule must be 'R8', the only rule with an allowlist, got '$($entry.Rule)'"
    } elseif ([string]::IsNullOrEmpty([string]$entry.Path)) {
        $why = 'Path is empty'
    } elseif (-not ($entry.Line -is [int] -or $entry.Line -is [long])) {
        $why = "Line must be an integer, got '$($entry.Line)'"
    } elseif ([int]$entry.Line -lt 1) {
        $why = "Line must be 1 or greater, got '$($entry.Line)'"
    } elseif ([string]$entry.Sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        $why = "Sha256 must be 64 hex digits, got '$($entry.Sha256)'"
    } elseif ($null -eq $entry.ExpectedCodes -or @($entry.ExpectedCodes).Count -eq 0) {
        $why = 'ExpectedCodes is empty'
    } elseif ([string]::IsNullOrEmpty([string]$entry.Reason)) {
        $why = 'Reason is empty; an allowlist entry that does not say why is a suppression'
    }
    if ($why) {
        Write-Output ('text-hygiene: bad allowlist entry for {0}: {1}' -f $entry.Path, $why)
        exit 1
    }
}

# ---------------------------------------------------------------- scan

$RepoRoot = if ($Path) { $Path } else { Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

# The default scan root is the repository root, two levels above this script. When that is what
# was actually scanned, the file list is complete, so an allowlist entry whose file is absent
# means the file was renamed, moved or deleted - a stale entry, not an unjudgeable one. A
# `-Path <subtree>` scan cannot tell the two apart, so there the entry is only counted as
# "not scanned" and never cried at.
$DefaultRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)))
$ScannedWholeTree = [string]::Equals($RepoRoot, $DefaultRoot, [System.StringComparison]::OrdinalIgnoreCase)

if (-not [System.IO.Directory]::Exists($RepoRoot)) {
    Write-Output ('text-hygiene: not a directory: {0}' -f $RepoRoot)
    exit 1
}

$files = Get-ScopedFiles $RepoRoot

Get-Note ('text-hygiene: root      {0}' -f $RepoRoot)
Get-Note ('text-hygiene: scope     {0}' -f ($ScopedExtensions -join ' '))
Get-Note ('text-hygiene: rules     BLOCK R1-R7,R9 | WARNING R8,R8A,R10')
Get-Note 'text-hygiene: exempt    Migrations/*.Designer.cs (EF-generated), wwwroot/lib (vendored) - see -ListExclusions'

foreach ($file in $files) {
    $scanned++
    $relative = Get-RelativePath $RepoRoot $file
    $scannedPaths.Add($relative) | Out-Null
    $extension = [System.IO.Path]::GetExtension($file)

    # The bytes are read once: the strict decode below decides R1, the first three decide R3,
    # and the R4 line number is a byte offset. R3 is checked on the bytes, before the BOM is
    # folded into a U+FEFF.
    $bytes = [System.IO.File]::ReadAllBytes($file)

    # ---- R1: strict UTF-8 decodable. Hard stop: without a decode, nothing below is checkable.
    # System.Text's own strict decoder is the authority here, not a re-implementation of the
    # UTF-8 state machine: a hand-written validator is a second thing that can be wrong about
    # what "valid UTF-8" means, and the earlier version of this script was wrong twice - once by
    # testing prefixes (a prefix that ends mid-character is undecodable even in a healthy file,
    # so it reported valid files as corrupt) and once by disagreeing with the decoder outright.
    # The decoder's own exception carries the index of the first bad byte, so nothing is lost.
    $text = $null
    $badOffset = -1
    try {
        $text = $Utf8Strict.GetString($bytes)
    } catch {
        $inner = $_.Exception
        while ($null -ne $inner -and -not ($inner -is [System.Text.DecoderFallbackException])) { $inner = $inner.InnerException }
        $badOffset = if ($inner -is [System.Text.DecoderFallbackException]) { $inner.Index } else { 0 }
    }
    if ($badOffset -ge 0) {
        Add-Finding $relative (Get-LineOfByteOffset $bytes $badOffset) 'R1' 'BLOCK' `
            ('not strict UTF-8: first invalid byte at offset {0}. The Arabic literals in this file cannot be verified.' -f $badOffset)
        continue
    }

    # ---- R3: no UTF-8 BOM.
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Add-Finding $relative 1 'R3' 'BLOCK' 'has a UTF-8 BOM; the repo mandates charset = utf-8 (no BOM).'
    }
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }

    # ---- R2: no U+FFFD. The signature of a mojibake round-trip, anywhere in the file.
    $fffdSeen = 0
    $index = 0
    while ($true) {
        $index = $text.IndexOf([char]0xFFFD, $index)
        if ($index -lt 0) { break }
        $fffdSeen++
        if ($fffdSeen -le $MaxPerRulePerFile) {
            $line = 1 + ([regex]::Matches($text.Substring(0, $index), "`n")).Count
            Add-Finding $relative $line 'R2' 'BLOCK' 'contains U+FFFD REPLACEMENT CHARACTER: this file has been through a lossy encoding round-trip.'
        }
        $index++
    }
    if ($fffdSeen -gt $MaxPerRulePerFile) {
        # Stating the total is the point: a capped list that does not say it was capped
        # reads as "5 problems" when there may be 5,000.
        Add-Finding $relative 1 'R2' 'BLOCK' ('only the first {0} of {1} U+FFFD occurrences are listed above.' -f $MaxPerRulePerFile, $fffdSeen)
    }

    $lines = $text -split "`n"
    $lastIndex = $lines.Length - 1

    # ---- R4: LF only. Counted on the decoded text, which is lossless: the strict decoder maps
    # U+000D to CR and normalises nothing, so these counts are the CR bytes in the file. A
    # per-byte PowerShell loop over 1.5 MB of repository text was the single slowest thing in
    # this script; a regex does the same count in native code.
    $crlf = [regex]::Matches($text, "`r`n").Count
    if ($crlf -gt 0) {
        $crTotal = [regex]::Matches($text, "`r").Count
        Add-Finding $relative (Get-LineOfByteOffset $bytes $text.IndexOf([char]13)) 'R4' 'BLOCK' ('CRLF line endings ({0} CR characters); the repo is LF-only. Fix by re-saving with end_of_line = lf.' -f $crTotal)
    } else {
        $loneCr = [regex]::Matches($text, "`r").Count
        if ($loneCr -gt 0) {
            Add-Finding $relative (Get-LineOfByteOffset $bytes $text.IndexOf([char]13)) 'R4' 'BLOCK' ('lone CR characters ({0}); the repo is LF-only.' -f $loneCr)
        }
    }

    # ---- R5/R6/R7/R9/R10: per line.
    for ($n = 0; $n -lt $lines.Length; $n++) {
        $line = $lines[$n]
        $lineNumber = $n + 1
        $isLast = ($n -eq $lastIndex)

        # A single trailing LF yields a final empty element; it is the newline, not a line.
        if ($isLast -and $line.Length -eq 0) { continue }

        if ($line.Length -gt 0) {
            $last = $line[$line.Length - 1]
            if ($last -eq ' ' -or $last -eq "`t") {
                # R6, with the one documented exemption. Two trailing spaces in Markdown is the
                # hard line break, and .editorconfig sets trim_trailing_whitespace = false for
                # *.md for exactly that reason. Anything else is a violation - including a line
                # that is nothing but spaces, which is trailing whitespace and not a hard break.
                $isHardBreak = $false
                if ($extension -eq '.md' -and $line.Trim().Length -gt 0 -and $line.Length -ge 2 -and
                    $line[$line.Length - 1] -eq ' ' -and $line[$line.Length - 2] -eq ' ' -and
                    -not ($line.Length -ge 3 -and $line[$line.Length - 3] -eq ' ')) {
                    $isHardBreak = $true
                }
                if (-not $isHardBreak) {
                    Add-Finding $relative $lineNumber 'R6' 'BLOCK' 'trailing whitespace.'
                }
            }
        }

        $leading = 0
        while ($leading -lt $line.Length -and ($line[$leading] -eq ' ' -or $line[$leading] -eq "`t")) { $leading++ }
        if ($leading -lt $line.Length -and $line.Substring(0, $leading).IndexOf("`t") -ge 0) {
            $id = if ($extension -eq '.md') { 'R9' } else { 'R7' }
            Add-Finding $relative $lineNumber $id 'BLOCK' 'TAB in leading whitespace; indent with spaces (indent_style = space).'
        }

        $tabAt = $line.IndexOf("`t", [math]::Min($leading, $line.Length))
        if ($tabAt -ge 0) {
            Add-Finding $relative $lineNumber 'R10' 'WARNING' 'TAB outside leading whitespace (used as a column separator, not as indentation).'
        }
    }

    # ---- R5: exactly one final newline, no trailing blank lines.
    if ($bytes.Length -gt 0) {
        if ($bytes[$bytes.Length - 1] -ne 10) {
            # $lastIndex is 0-based, so the last line is $lastIndex + 1. A one-line file has
            # $lastIndex 0; reporting 0 would name a line that does not exist.
            Add-Finding $relative ($lastIndex + 1) 'R5' 'BLOCK' 'no newline at end of file; insert_final_newline = true.'
        } else {
            $firstTrailingBlank = 0
            for ($n = $lastIndex - 1; $n -ge 0; $n--) {
                if ($lines[$n].Trim().Length -ne 0) { break }
                # Walking backwards, so the LAST assignment wins and leaves the SMALLEST
                # line number - the first of the trailing blanks, which is the one to fix.
                $firstTrailingBlank = $n + 1
            }
            if ($firstTrailingBlank -gt 0) {
                Add-Finding $relative $firstTrailingBlank 'R5' 'BLOCK' ('{0} trailing blank line(s); the file must end with exactly one newline.' -f ($lastIndex - $firstTrailingBlank + 1))
            }
        }
    }

    # ---- R8: foreign script in an Arabic-dominant file. Warning only.
    $masked = Get-MaskedText $text $extension
    $arabic = [regex]::Matches($masked, $ArabicPattern).Count
    $latin = [regex]::Matches($masked, $LatinPattern).Count

    if ($arabic -gt $latin -and $arabic -gt 0) {
        # Line numbers come from a single native scan for newlines plus a cursor that only ever
        # moves forward, so the whole pass is linear. Iterating characters in PowerShell here
        # was the other half of the 110 seconds this script used to take.
        $newlines = [regex]::Matches($masked, "`n")
        $cursor = 0
        $reported = 0
        $overflowAt = 0
        # One report per allowlisted LINE, not per character: the four characters on
        # Deep-Audit-Report.md:287 are one adjudicated finding, and four identical
        # [R8-ALLOWED] lines would read as four separate decisions that were each made.
        $allowedLines = New-Object 'System.Collections.Generic.HashSet[int]'
        foreach ($hit in [regex]::Matches($masked, $ForeignPattern)) {
            while ($cursor -lt $newlines.Count -and $newlines[$cursor].Index -lt $hit.Index) { $cursor++ }
            $lineNumber = $cursor + 1

            # The mask preserves line breaks, so masked line N is real line N and $lines holds
            # exactly the text the entry's hash was taken from.
            $lineText = if ($lineNumber -le $lines.Length) { $lines[$lineNumber - 1] } else { $null }
            $allowed = if ($null -ne $lineText) { Test-AllowedR8Line $relative $lineNumber $lineText } else { $null }
            if ($null -ne $allowed) {
                if ($allowedLines.Add($lineNumber)) {
                    Add-AllowedFinding $relative $lineNumber $allowed (
                        'line carries {0}. Adjudicated evidence, not a defect: {1}' -f (Get-R8LineSummary $lineText), $allowed.Reason)
                }
                continue
            }

            $code = [int]$hit.Value[0]
            $family = Get-ScriptFamily $code
            $reported++
            if ($reported -le $MaxPerRulePerFile) {
                Add-Finding $relative $lineNumber 'R8' 'WARNING' ('U+{0:X4} is {1}, which does not belong in an Arabic-dominant file. Confirm it is a real defect and not a quoted foreign string.' -f $code, $family)
            } elseif ($overflowAt -eq 0) {
                $overflowAt = $lineNumber
            }
        }
        if ($overflowAt -gt 0) {
            Add-Finding $relative $overflowAt 'R8' 'WARNING' ('only the first {0} of {1} foreign-script characters in this file are listed above.' -f $MaxPerRulePerFile, $reported)
        }
    }
}

# ---------------------------------------------------------------- verdict

# Stale allowlist entries are settled FIRST, because they are warnings and every count printed
# below has to include them. An entry that matched nothing is reported here rather than ignored:
# it is either a line that was edited or moved, or a finding that has been closed, and both are
# decisions only a person can make.
$matchedEntries = 0
$staleEntries = 0
$unevaluatedEntries = 0
foreach ($entry in $R8LineAllowlist) {
    if ($entry['Matched']) { $matchedEntries++; continue }
    if (-not $scannedPaths.Contains([string]$entry.Path)) {
        if ($ScannedWholeTree) {
            # The whole tree was scanned and the file is gone: renamed, moved or deleted. The
            # exception now covers nothing, which is exactly the state a reader must be told
            # about rather than left to infer from a missing line in the report.
            $staleEntries++
            Add-StaleAllowlistFinding $entry (
                'the file it names is not in this tree, and the whole tree was scanned. It was renamed, moved or ' +
                'deleted, so the exception now covers nothing - re-point the entry at the new path and line, or delete it.')
        } else {
            $unevaluatedEntries++
        }
        continue
    }
    $staleEntries++
    Add-StaleAllowlistFinding $entry (
        'allowlist entry matched nothing. The line it names was edited or moved, or the finding it covers is gone. ' +
        'Re-point the entry (recompute the sha256) or delete it - and do not widen it to the file or the directory. ' +
        'Until then this line is back under R8.')
}

Get-Note ('text-hygiene: scanned   {0} file(s)' -f $scanned)
Get-Note ('text-hygiene: result    blocking={0} warning={1} allowlisted={2}' -f $blocking.Count, $warnings.Count, $allowlisted.Count)
if ($ruleCounts.Count -gt 0) {
    $perRule = @()
    foreach ($id in ($ruleCounts.Keys | Sort-Object)) { $perRule += ('{0}={1}' -f $id, $ruleCounts[$id]) }
    Get-Note ('text-hygiene: per rule  {0}' -f ($perRule -join ' '))
}
if ($rulesFired.Count -gt 0) {
    Get-Note ('text-hygiene: rules fired {0}' -f (($rulesFired) -join ' '))
}
# Always printed when the allowlist is non-empty, matched or not, so the exceptions in force
# are visible in the same run as the verdict. `warning=` above means "undecided"; this line is
# where the decisions already taken are accounted for.
if ($R8LineAllowlist.Count -gt 0) {
    $noun = if ($R8LineAllowlist.Count -eq 1) { 'entry' } else { 'entries' }
    Get-Note ('text-hygiene: allowlist {0} {1}: {2} matched, {3} stale, {4} not scanned' -f $R8LineAllowlist.Count, $noun, $matchedEntries, $staleEntries, $unevaluatedEntries)
}

if ($blocking.Count -gt 0) {
    Write-Line ''
    Write-Line ('text-hygiene: FAIL - {0} blocking violation(s) in {1} rule(s): {2}' -f $blocking.Count, $rulesFired.Count, (($rulesFired) -join ' '))
    Write-Line 'text-hygiene: encoding, line endings, the final newline and trailing whitespace are mechanical -'
    Write-Line 'text-hygiene: re-save the file with the .editorconfig contract (charset = utf-8, end_of_line = lf,'
    Write-Line 'text-hygiene: insert_final_newline = true, trim_trailing_whitespace) and the finding clears itself.'
    exit 2
}

if ($warnings.Count -gt 0) {
    Write-Line ''
    Write-Line ('text-hygiene: PASS with {0} warning(s). Warnings are judgement calls and do not fail the gate;' -f $warnings.Count)
    Write-Line 'text-hygiene: each one still needs a human decision - keep it, fix it, or route it to the owner.'
}

if ($allowlisted.Count -gt 0) {
    Write-Line ''
    Write-Line ('text-hygiene: {0} line(s) reported as [R8-ALLOWED] above. They are counted in allowlisted=, not in' -f $allowlisted.Count)
    Write-Line 'text-hygiene: warning=; that is a decision already taken and written down, not a line that passed the rule.'
}

Get-Note 'text-hygiene: PASS'
exit 0
