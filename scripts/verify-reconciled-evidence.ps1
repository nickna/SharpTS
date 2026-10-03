# Integrity checks for #1919-#1925 records; defect outputs are evidence, not conformance assertions.
[CmdletBinding()]
param([ValidateRange(1919,1925)][int[]]$Issue = (1919..1925))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskArchive = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs/plans/archive'
$taskLedger = Get-Content -Raw (Join-Path $taskArchive '1599-historical-reconciliation.json') | ConvertFrom-Json
function Get-TaskHash([byte[]]$bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}
function Assert-TaskEqual($expected, $actual, [string]$description) {
    if ($expected -cne $actual) { throw "$description differs" }
}
$taskCount = 0
$taskIl = 0
foreach ($taskIssue in $Issue) {
    $taskDirectory = Join-Path $taskArchive "issue-$taskIssue"
    $taskIntegrity = Get-Content -Raw (Join-Path $taskDirectory 'integrity.json') | ConvertFrom-Json
    foreach ($taskFile in $taskIntegrity.files.PSObject.Properties) {
        Assert-TaskEqual $taskFile.Value (Get-TaskHash ([IO.File]::ReadAllBytes((Join-Path $taskDirectory $taskFile.Name)))) "#$taskIssue retained $($taskFile.Name) hash"
    }
    $taskCases = @(Get-Content -Raw (Join-Path $taskDirectory 'cases.json') | ConvertFrom-Json)
    $taskReport = Get-Content -Raw (Join-Path $taskDirectory 'results.json') | ConvertFrom-Json
    $taskHistorical = Get-Content -Raw (Join-Path $taskDirectory 'historical.json') | ConvertFrom-Json
    Assert-TaskEqual $taskLedger.frozenBody.sha256 $taskHistorical.frozenBodySha256 'Frozen body identity'
    foreach ($taskExcerpt in $taskHistorical.excerpts) {
        Assert-TaskEqual $taskExcerpt.sha256 (Get-TaskHash ([Text.Encoding]::UTF8.GetBytes($taskExcerpt.text))) 'Frozen excerpt hash'
        $taskRows = @($taskLedger.bodyRows | Where-Object line -eq $taskExcerpt.line)
        if ($taskRows.Count -ne 1) { throw 'Missing ledger line' }
        Assert-TaskEqual $taskRows[0].sha256 $taskExcerpt.sha256 'Ledger/excerpt binding'
    }
    Assert-TaskEqual 30 $taskReport.deadlineSeconds 'Collection deadline'
    Assert-TaskEqual $taskCases.Count @($taskReport.results).Count 'Collected case count'
    foreach ($taskCase in $taskCases) {
        $taskMatches = @($taskReport.results | Where-Object File -eq $taskCase.File)
        if ($taskMatches.Count -ne 1) { throw 'Missing or duplicated case' }
        $taskResult = $taskMatches[0]
        $taskSourceHash = Get-TaskHash ([IO.File]::ReadAllBytes((Join-Path $taskDirectory $taskCase.File)))
        Assert-TaskEqual $taskCase.SourceSha256 $taskSourceHash 'Manifest/source binding'
        Assert-TaskEqual $taskResult.sourceSha256 $taskSourceHash 'Report/source binding'
        Assert-TaskEqual $taskCase.ExpectedStdout $taskResult.ExpectedStdout 'Reference expectation'
        Assert-TaskEqual $taskCase.Provenance $taskResult.Provenance 'Source provenance'
        $taskNode = @($taskResult.observations | Where-Object mode -eq 'node-reference')
        if ($taskNode.Count -ne 1) { throw 'Missing or duplicated Node reference' }
        if ($taskCase.PSObject.Properties.Name -contains 'ReferenceKind') {
            Assert-TaskEqual $taskCase.ReferenceExitCode $taskNode[0].result.ExitCode 'Reserved-import Node rejection'
            if (-not $taskNode[0].result.StandardError.Contains($taskCase.ReferenceDiagnostic)) { throw 'Reserved-import reference diagnostic changed' }
        } else {
            Assert-TaskEqual 0 $taskNode[0].result.ExitCode 'Node reference exit'
            Assert-TaskEqual '' $taskNode[0].result.StandardError 'Node reference stderr'
            Assert-TaskEqual $taskCase.ExpectedStdout $taskNode[0].result.StandardOutput 'Node reference stdout'
        }
        foreach ($taskMode in @('interpreted-api','compiled-api','interpreted-cli-default','interpreted-cli-noLib','interpreted-cli-esnext','default-cli-compilation','standalone-cli-compilation','hosted-cli-compilation')) {
            if (@($taskResult.observations | Where-Object mode -eq $taskMode).Count -ne 1) { throw "Missing or duplicated $taskMode" }
        }
        foreach ($taskCompile in $taskResult.observations | Where-Object { $_.mode.EndsWith('-cli-compilation') }) {
            if ($taskCompile.result.ExitCode -ne 0) { continue }
            if (-not $taskCompile.result.StandardOutput.Contains('IL verification passed')) { throw 'Missing recorded IL verification' }
            $taskIl++
            $taskPrefix = $taskCompile.mode.Replace('-cli-compilation', '')
            $taskExecution = switch ($taskPrefix) {
                'default' { 'default-standalone-execution' }
                'standalone' { 'standalone-execution' }
                'hosted' { 'hosted-runtime-initialization' }
            }
            if (@($taskResult.observations | Where-Object mode -eq $taskExecution).Count -ne 1) { throw "Missing or duplicated $taskExecution" }
            if ($taskPrefix -eq 'default') { continue }
            $taskMetadata = @($taskResult.observations | Where-Object mode -eq "$taskPrefix-metadata")
            if ($taskMetadata.Count -ne 1) { throw 'Missing or duplicated deployment metadata' }
            if ($taskMetadata[0].sharpTsDllPresent -or $taskMetadata[0].references -contains 'SharpTS') { throw 'Unexpected SharpTS runtime deployment' }
            if ($taskPrefix -eq 'hosted' -and $taskMetadata[0].references -notcontains 'SharpTS.Hosting.Abstractions') { throw 'Missing hosted ABI reference' }
        }
        $taskCount++
    }
}
Write-Output "Verified $taskCount source-bound cases and $taskIl retained successful IL checks, reference results, frozen excerpts and all recorded observation hashes. Runtime failures remain failures."
