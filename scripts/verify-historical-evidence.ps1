# Verify retained observations as evidence, without treating defect outputs as conformance.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskArchive = Join-Path $taskRepository 'docs/plans/archive'
$taskLedger = Get-Content -Raw (Join-Path $taskArchive '1599-historical-reconciliation.json') | ConvertFrom-Json
function Assert-TaskEqual($expected, $actual, [string] $description) {
    if ($expected -cne $actual) { throw "$description differs" }
}
function Get-TaskHash([byte[]] $bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
$taskPrograms = 0
$taskIlChecks = 0
foreach ($taskIssue in 1914..1918) {
    $taskDirectory = Join-Path $taskArchive "issue-$taskIssue"
    $taskCases = @(Get-Content -Raw (Join-Path $taskDirectory 'cases.json') | ConvertFrom-Json)
    $taskReport = Get-Content -Raw (Join-Path $taskDirectory 'results.json') | ConvertFrom-Json
    $taskHistorical = Get-Content -Raw (Join-Path $taskDirectory 'historical.json') | ConvertFrom-Json
    Assert-TaskEqual $taskLedger.frozenBody.sha256 $taskHistorical.frozenBodySha256 'Frozen body identity'
    foreach ($taskExcerpt in $taskHistorical.excerpts) {
        Assert-TaskEqual $taskExcerpt.sha256 (Get-TaskHash ([Text.Encoding]::UTF8.GetBytes($taskExcerpt.text))) 'Excerpt hash'
        $taskLine = @($taskLedger.bodyRows | Where-Object line -eq $taskExcerpt.line)
        if ($taskLine.Count -ne 1) { throw 'Missing frozen ledger line' }
        Assert-TaskEqual $taskLine[0].sha256 $taskExcerpt.sha256 'Frozen ledger/excerpt binding'
    }
    Assert-TaskEqual 30 $taskReport.deadlineSeconds 'Collection deadline'
    Assert-TaskEqual $taskCases.Count @($taskReport.results).Count 'Case count'
    foreach ($taskCase in $taskCases) {
        $taskResult = @($taskReport.results | Where-Object File -eq $taskCase.File)
        if ($taskResult.Count -ne 1) { throw 'Case missing or duplicated' }
        $taskResult = $taskResult[0]
        Assert-TaskEqual $taskCase.Provenance $taskResult.Provenance 'Source provenance'
        Assert-TaskEqual $taskCase.ExpectedStdout $taskResult.ExpectedStdout 'Reference expectation'
        Assert-TaskEqual $taskResult.sourceSha256 (Get-TaskHash ([IO.File]::ReadAllBytes((Join-Path $taskDirectory $taskCase.File)))) 'Fixture hash'
        $taskNode = $taskResult.observations | Where-Object mode -eq 'node-reference'
        if ($taskIssue -eq 1917 -and $taskCase.File -eq 'nonfinite.ts') {
            if ($taskNode.result.ExitCode -eq 0 -or $taskNode.result.StandardError -notmatch 'sum is not a function') {
                throw 'Unavailable Node sumPrecise evidence changed'
            }
        } else {
            Assert-TaskEqual 0 $taskNode.result.ExitCode 'Node exit'
            Assert-TaskEqual '' $taskNode.result.StandardError 'Node stderr'
            Assert-TaskEqual $taskCase.ExpectedStdout $taskNode.result.StandardOutput 'Node reference stdout'
        }
        foreach ($taskMode in @('standalone','hosted')) {
            $taskCompile = $taskResult.observations | Where-Object mode -eq "$taskMode-cli-compilation"
            Assert-TaskEqual 0 $taskCompile.result.ExitCode 'Builtin-typed compilation exit'
            if ($taskCompile.result.StandardOutput -notmatch 'IL verification passed') { throw 'Missing IL verification' }
            $taskMetadata = $taskResult.observations | Where-Object mode -eq "$taskMode-metadata"
            if ($taskMetadata.sharpTsDllPresent -or $taskMetadata.references -contains 'SharpTS') { throw 'Unexpected runtime deployment' }
            if ($taskMode -eq 'hosted' -and $taskMetadata.references -notcontains 'SharpTS.Hosting.Abstractions') { throw 'Missing hosted ABI reference' }
            $taskIlChecks++
        }
        $taskDefault = $taskResult.observations | Where-Object mode -eq 'default-cli-compilation'
        if ($taskDefault.result.ExitCode -eq 0) {
            if ($taskDefault.result.StandardOutput -notmatch 'IL verification passed') { throw 'Missing default-path IL check' }
            $taskIlChecks++
        }
        $taskModes = @($taskResult.observations.mode)
        foreach ($taskMode in @('interpreted-api','compiled-api','interpreted-cli-default','interpreted-cli-noLib','interpreted-cli-esnext','standalone-execution','hosted-runtime-initialization')) {
            if (@($taskModes | Where-Object { $_ -eq $taskMode }).Count -ne 1) { throw "Missing or duplicated $taskMode" }
        }
        $taskPrograms++
    }
}
$taskConsole = Get-Content -Raw (Join-Path $taskArchive 'issue-1912/results.json') | ConvertFrom-Json
if (-not $taskConsole.observations[0].failure -or $taskConsole.observations[0].sameOut -or $taskConsole.observations[0].sameErr) { throw 'Original first-call failure missing' }
if ($taskConsole.observations[1].failure -or -not $taskConsole.observations[1].sameOut -or -not $taskConsole.observations[1].sameErr) { throw 'Repeated-call control failed' }
foreach ($taskControl in $taskConsole.observations[2..3]) {
    if (-not $taskControl.Success -or -not $taskControl.runSuccess -or -not $taskControl.sameOut -or -not $taskControl.sameErr) { throw 'Public-writer control failed' }
    Assert-TaskEqual "x`n" $taskControl.output 'Public-writer output'
}
$taskNumeric = Get-Content -Raw (Join-Path $taskArchive 'issue-1913/numeric-original.json') | ConvertFrom-Json
Assert-TaskEqual 1000 $taskNumeric.warmupIterations 'Original numeric warmup'
Assert-TaskEqual 1000000 $taskNumeric.iterations 'Original numeric workload'
Assert-TaskEqual 4096 $taskNumeric.budgetBytes 'Original numeric budget'
Assert-TaskEqual 232 $taskNumeric.warmup 'Numeric warmup result'
Assert-TaskEqual 64 $taskNumeric.result 'Numeric measured result'
if ($taskNumeric.allocated -gt $taskNumeric.budgetBytes) { throw 'Numeric allocation control exceeds budget' }
Write-Output "Verified $taskPrograms source-bound programs, $taskIlChecks recorded IL checks, frozen excerpt hashes, console controls and original numeric workload. Defect outputs remain observations, not passing conformance assertions."
