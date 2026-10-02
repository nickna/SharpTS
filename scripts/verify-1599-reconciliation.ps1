# Verify the finite historical coverage ledger against a saved `gh issue view 1599
# --json body,comments,url` response. This performs no network access or writes.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IssueJson,
    [switch]$VerifyLocalArtifacts
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$ledgerPath = Join-Path $repository 'docs/plans/archive/1599-historical-reconciliation.json'
$reportPath = Join-Path $repository 'docs/plans/archive/1599-historical-reconciliation.md'
$ledger = Get-Content -Raw -LiteralPath $ledgerPath | ConvertFrom-Json
$issue = Get-Content -Raw -LiteralPath $IssueJson | ConvertFrom-Json
$report = Get-Content -Raw -LiteralPath $reportPath

function Get-Utf8Hash([string]$Text) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}
function Assert-Equal($Expected, $Actual, [string]$Description) {
    if ($Expected -cne $Actual) { throw "$Description differs: expected '$Expected', got '$Actual'." }
}

Assert-Equal $ledger.frozenBody.sha256 (Get-Utf8Hash $issue.body) 'Frozen body hash'
Assert-Equal $ledger.frozenBody.utf16CodeUnits $issue.body.Length 'Frozen body UTF-16 length'
Assert-Equal $ledger.frozenBody.url $issue.url 'Frozen issue URL'
$architectureLines = @(git -C $repository show "$($ledger.baselineCommit):ARCHITECTURE.md")
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the frozen architecture commit.' }
Assert-Equal $ledger.architecture.sha256 (Get-Utf8Hash (($architectureLines -join "`n") + "`n")) 'Frozen architecture hash'
$sourceLines = $issue.body.Split("`n")
Assert-Equal $ledger.frozenBody.lines $sourceLines.Count 'Frozen line count'
$nonemptyLines = @(for ($index = 0; $index -lt $sourceLines.Count; $index++) {
    if (-not [string]::IsNullOrWhiteSpace($sourceLines[$index])) { $index + 1 }
})
Assert-Equal $ledger.frozenBody.nonemptyLines $nonemptyLines.Count 'Nonempty line count'
Assert-Equal ($nonemptyLines -join ',') (($ledger.bodyRows.line | Sort-Object) -join ',') 'Exact line coverage'

$reportIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($match in [regex]::Matches($report, '(?m)^\| ((?:[SRFBACM]\d{2}|E\d+)) \|')) {
    if (-not $reportIds.Add($match.Groups[1].Value)) { throw "Duplicate report row $($match.Groups[1].Value)." }
}
function Assert-ReportRows($Row) {
    if ($Row.reportRows.Count -eq 0) { throw "Missing disposition for $($Row.id)." }
    foreach ($id in $Row.reportRows) {
        if (-not $reportIds.Contains($id)) { throw "Unknown report row '$id' in $($Row.id)." }
    }
}
foreach ($row in $ledger.bodyRows) {
    Assert-Equal ('L{0:000}' -f $row.line) $row.id 'Source row identity'
    Assert-Equal $row.sha256 (Get-Utf8Hash $sourceLines[$row.line - 1]) "Source row $($row.id) hash"
    Assert-ReportRows $row
}
Assert-Equal 18 $ledger.comments.Count 'Original comment count'
if ($issue.comments.Count -lt 18) { throw 'The saved response does not contain all original comments.' }
for ($index = 0; $index -lt 18; $index++) {
    $row = $ledger.comments[$index]
    $comment = $issue.comments[$index]
    Assert-Equal ('C{0:00}' -f ($index + 1)) $row.id 'Comment identity'
    Assert-Equal $row.url $comment.url "Comment $($row.id) URL/order"
    Assert-Equal $row.sha256 (Get-Utf8Hash $comment.body) "Comment $($row.id) hash"
    Assert-ReportRows $row
}
Assert-Equal 63 $ledger.semanticDestinations.Count 'Existing semantic destination count'
foreach ($number in $ledger.semanticDestinations) {
    if (-not $reportIds.Contains("E$number") -or "E$number" -notin $ledger.bodyRows.reportRows) {
        throw "Missing historical source/destination for semantic issue #$number."
    }
}
Assert-Equal 26 $ledger.supersessionCandidates.Count 'Named supersession candidate count'
foreach ($number in $ledger.supersessionCandidates) {
    if ($report -notmatch "https://github.com/nickna/SharpTS/pull/$number\)") {
        throw "Missing supersession citation #$number."
    }
}
$standaloneTests = Get-Content -Raw -LiteralPath (Join-Path $repository 'tests/SharpTS.Tests/CompilerTests/StandaloneDllTests.cs')
Assert-Equal 3 $ledger.issue1805OriginalPrograms.Count '#1805 original program count'
foreach ($program in $ledger.issue1805OriginalPrograms) {
    Assert-Equal $program.sourceSha256 (Get-Utf8Hash $program.source) "#1805 $($program.name) source hash"
    $pattern = '\[InlineData\("' + [regex]::Escape($program.name) + '", ("(?:[^"\\]|\\.)*")\)\]'
    $regressionMatches = [regex]::Matches($standaloneTests, $pattern)
    Assert-Equal 1 $regressionMatches.Count "#1805 $($program.name) regression count"
    Assert-Equal $program.source ($regressionMatches[0].Groups[1].Value | ConvertFrom-Json) "#1805 $($program.name) regression source"
}
if ($VerifyLocalArtifacts) {
    foreach ($artifact in $ledger.recoveredArtifacts) {
        $path = Join-Path $repository $artifact.path
        Assert-Equal $artifact.sha256 ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()) "Recovered $($artifact.path)"
    }
}
Write-Output "Verified $($ledger.bodyRows.Count) frozen source lines, 18 original comments, 63 semantic destinations, 26 supersession candidates and three exact #1805 regression sources."
