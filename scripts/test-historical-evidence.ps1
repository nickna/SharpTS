# Mutation checks for the evidence verifier; never alter the committed reports.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskArchive = Join-Path $taskRepository 'docs/plans/archive'
$taskFixture = Join-Path $taskRepository ('artifacts/historical-evidence-verifier-' + [guid]::NewGuid().ToString('N'))
$taskFixtureArchive = Join-Path $taskFixture 'docs/plans/archive'
New-Item -ItemType Directory -Force $taskFixtureArchive, (Join-Path $taskFixture 'scripts') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'verify-historical-evidence.ps1') (Join-Path $taskFixture 'scripts')
Get-ChildItem -LiteralPath $taskArchive | Where-Object {
    $_.Name -match '^issue-191[2-8]$|^1599-historical-reconciliation.json$|^issue-1912-1918-observations.json$'
} | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskFixtureArchive -Recurse }
$taskVerifier = Join-Path $taskFixture 'scripts/verify-historical-evidence.ps1'
function Assert-TaskVerification([string] $name, [bool] $shouldPass, [string] $diagnostic = '') {
    $taskOutput = & pwsh -NoProfile -File $taskVerifier 2>&1 | Out-String
    $taskExit = $LASTEXITCODE
    $taskOutput | Set-Content -LiteralPath (Join-Path $taskFixture "$name.log")
    if (($taskExit -eq 0) -ne $shouldPass) { throw "$name had unexpected verifier exit ${taskExit}: $taskOutput" }
    if (-not $shouldPass -and -not $taskOutput.Contains($diagnostic)) { throw "$name failed for an unrelated reason: $taskOutput" }
    Write-Output "PASS: $name"
}
function Test-TaskMutation([string] $name, [int] $issue, [string] $file, [scriptblock] $mutate, [string] $diagnostic) {
    $taskPath = Join-Path $taskFixtureArchive "issue-$issue/results.json"
    $taskOriginal = Get-Content -Raw -LiteralPath $taskPath
    try {
        $taskReport = $taskOriginal | ConvertFrom-Json
        $taskCase = $taskReport.results | Where-Object File -eq $file
        & $mutate $taskCase
        $taskReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $taskPath
        Assert-TaskVerification $name $false $diagnostic
    } finally { [IO.File]::WriteAllText($taskPath, $taskOriginal) }
}
Assert-TaskVerification 'original-evidence' $true
Test-TaskMutation 'unexpected-default-rejection' 1914 'raw.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'default-cli-compilation').result.ExitCode = 1
} 'Required default-path compilation exit differs'
Test-TaskMutation 'expected-rejection-disappears' 1917 'identity.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'default-cli-compilation').result.ExitCode = 0
} 'Expected default declaration rejection exit differs'
Test-TaskMutation 'default-il-proof-missing' 1914 'raw.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'default-cli-compilation').result.StandardOutput = 'Compiled without verification'
} 'Missing default-path IL check'
Test-TaskMutation 'interpreted-result-altered' 1914 'computed-tag.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'interpreted-api').result = 'changed observation'
} 'Retained interpreted API output differs'
Test-TaskMutation 'compiled-failure-erased' 1914 'computed-tag.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'compiled-api').result = $case.ExpectedStdout
} 'Retained compiled API output differs'
Test-TaskMutation 'hosted-failure-erased' 1918 'async-static.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'hosted-runtime-initialization').result = $case.ExpectedStdout
} 'Retained hosted output differs'
Test-TaskMutation 'standalone-result-altered' 1916 'radix.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'standalone-execution').result.StandardOutput = $case.ExpectedStdout
} 'standalone-execution retained stdout differs'
Test-TaskMutation 'standalone-exit-altered' 1914 'raw.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'standalone-execution').result.ExitCode = 1
} 'standalone-execution exit differs'
Test-TaskMutation 'default-execution-altered' 1916 'radix.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'default-standalone-execution').result.StandardOutput = $case.ExpectedStdout
} 'default-standalone-execution retained stdout differs'
Test-TaskMutation 'cli-stderr-altered' 1914 'raw.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'interpreted-cli-noLib').result.StandardError = 'unexpected error'
} 'interpreted-cli-noLib stderr differs'
Test-TaskMutation 'declaration-diagnostic-altered' 1918 'typedarray.ts' {
    param($case)
    ($case.observations | Where-Object mode -eq 'interpreted-cli-esnext').result.StandardOutput = 'different failure'
} 'Interpreted declaration diagnostic changed'
Assert-TaskVerification 'restored-evidence' $true
Write-Output "Evidence verifier mutation checks passed. Logs: $taskFixture"
