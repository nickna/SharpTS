# Verify retained observations as evidence, without treating defect outputs as conformance.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskArchive = Join-Path $taskRepository 'docs/plans/archive'
$taskLedger = Get-Content -Raw (Join-Path $taskArchive '1599-historical-reconciliation.json') | ConvertFrom-Json
$taskRetained = Get-Content -Raw (Join-Path $taskArchive 'issue-1912-1918-observations.json') | ConvertFrom-Json
function Assert-TaskEqual($expected, $actual, [string] $description) {
    if ($expected -cne $actual) { throw "$description differs" }
}
function Get-TaskHash([byte[]] $bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
function Get-TaskObservation($result, [string] $mode) {
    $taskMatches = @($result.observations | Where-Object mode -eq $mode)
    if ($taskMatches.Count -ne 1) { throw "Missing or duplicated $mode" }
    return $taskMatches[0]
}
function Assert-TaskProcess($observation, [string] $expectedStdout) {
    Assert-TaskEqual 0 $observation.result.ExitCode "$($observation.mode) exit"
    Assert-TaskEqual $expectedStdout $observation.result.StandardOutput "$($observation.mode) retained stdout"
    Assert-TaskEqual '' $observation.result.StandardError "$($observation.mode) stderr"
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
        $taskExpected = @($taskRetained.cases | Where-Object { $_.issue -eq $taskIssue -and $_.file -eq $taskCase.File })
        if ($taskExpected.Count -ne 1) { throw 'Retained observation missing or duplicated' }
        $taskExpected = $taskExpected[0]
        Assert-TaskEqual $taskExpected.sourceSha256 $taskResult.sourceSha256 'Retained observation/source binding'
        Assert-TaskEqual $taskExpected.referenceStdout $taskCase.ExpectedStdout 'Frozen reference expectation'
        Assert-TaskEqual $taskCase.Provenance $taskResult.Provenance 'Source provenance'
        Assert-TaskEqual $taskCase.ExpectedStdout $taskResult.ExpectedStdout 'Reference expectation'
        Assert-TaskEqual $taskResult.sourceSha256 (Get-TaskHash ([IO.File]::ReadAllBytes((Join-Path $taskDirectory $taskCase.File)))) 'Fixture hash'
        $taskNode = Get-TaskObservation $taskResult 'node-reference'
        if ($taskIssue -eq 1917 -and $taskCase.File -eq 'nonfinite.ts') {
            Assert-TaskEqual 1 $taskNode.result.ExitCode 'Unavailable Node sumPrecise exit'
            Assert-TaskEqual '' $taskNode.result.StandardOutput 'Unavailable Node sumPrecise stdout'
            if ($taskNode.result.StandardError -notmatch 'sum is not a function') {
                throw 'Unavailable Node sumPrecise evidence changed'
            }
        } else {
            Assert-TaskEqual 0 $taskNode.result.ExitCode 'Node exit'
            Assert-TaskEqual '' $taskNode.result.StandardError 'Node stderr'
            Assert-TaskEqual $taskCase.ExpectedStdout $taskNode.result.StandardOutput 'Node reference stdout'
        }
        foreach ($taskMode in @('standalone','hosted')) {
            $taskCompile = Get-TaskObservation $taskResult "$taskMode-cli-compilation"
            Assert-TaskEqual 0 $taskCompile.result.ExitCode 'Builtin-typed compilation exit'
            if ($taskCompile.result.StandardOutput -notmatch 'IL verification passed') { throw 'Missing IL verification' }
            $taskMetadata = Get-TaskObservation $taskResult "$taskMode-metadata"
            if ($taskMetadata.sharpTsDllPresent -or $taskMetadata.references -contains 'SharpTS') { throw 'Unexpected runtime deployment' }
            if ($taskMode -eq 'hosted' -and $taskMetadata.references -notcontains 'SharpTS.Hosting.Abstractions') { throw 'Missing hosted ABI reference' }
            $taskIlChecks++
        }
        $taskDefault = Get-TaskObservation $taskResult 'default-cli-compilation'
        if ($null -eq $taskExpected.defaultDeclarationError) {
            Assert-TaskEqual 0 $taskDefault.result.ExitCode 'Required default-path compilation exit'
            Assert-TaskEqual '' $taskDefault.result.StandardError 'Default-path compilation stderr'
            if ($taskDefault.result.StandardOutput -notmatch 'IL verification passed') { throw 'Missing default-path IL check' }
            Assert-TaskProcess (Get-TaskObservation $taskResult 'default-standalone-execution') $taskExpected.compiledStdout
            $taskIlChecks++
        } else {
            Assert-TaskEqual 1 $taskDefault.result.ExitCode 'Expected default declaration rejection exit'
            Assert-TaskEqual '' $taskDefault.result.StandardOutput 'Rejected default compilation stdout'
            if (-not $taskDefault.result.StandardError.Contains($taskExpected.defaultDeclarationError)) { throw 'Expected default declaration diagnostic changed' }
            if (@($taskResult.observations | Where-Object mode -eq 'default-standalone-execution').Count -ne 0) { throw 'Rejected default path unexpectedly executed' }
        }
        Assert-TaskEqual $taskExpected.interpretedStdout (Get-TaskObservation $taskResult 'interpreted-api').result 'Retained interpreted API output'
        Assert-TaskEqual $taskExpected.compiledStdout (Get-TaskObservation $taskResult 'compiled-api').result 'Retained compiled API output'
        Assert-TaskEqual $taskExpected.compiledStdout (Get-TaskObservation $taskResult 'hosted-runtime-initialization').result 'Retained hosted output'
        Assert-TaskProcess (Get-TaskObservation $taskResult 'standalone-execution') $taskExpected.compiledStdout
        Assert-TaskProcess (Get-TaskObservation $taskResult 'interpreted-cli-noLib') $taskExpected.interpretedStdout
        foreach ($taskMode in @('interpreted-cli-default','interpreted-cli-esnext')) {
            $taskObservation = Get-TaskObservation $taskResult $taskMode
            if ($null -eq $taskExpected.defaultDeclarationError) {
                Assert-TaskProcess $taskObservation $taskExpected.interpretedStdout
            } else {
                Assert-TaskEqual 1 $taskObservation.result.ExitCode 'Expected interpreted declaration rejection exit'
                Assert-TaskEqual '' $taskObservation.result.StandardError 'Rejected interpreted CLI stderr'
                if (-not $taskObservation.result.StandardOutput.Contains($taskExpected.defaultDeclarationError)) { throw 'Interpreted declaration diagnostic changed' }
            }
        }
        $taskPrograms++
    }
}
Assert-TaskEqual $taskPrograms @($taskRetained.cases).Count 'Retained observation count'
Assert-TaskEqual 54 $taskIlChecks 'Exact recorded IL check count'
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
