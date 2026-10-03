# Exercise the integrity/schema verifier on copies; never mutate the retained evidence.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRepository = Split-Path -Parent $PSScriptRoot
$taskArchive = Join-Path $taskRepository 'docs/plans/archive'
$taskFixture = Join-Path $taskRepository ('artifacts/reconciled-evidence-' + [guid]::NewGuid().ToString('N'))
$taskFixtureArchive = Join-Path $taskFixture 'docs/plans/archive'
New-Item -ItemType Directory -Force $taskFixtureArchive, (Join-Path $taskFixture 'scripts') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'verify-reconciled-evidence.ps1') (Join-Path $taskFixture 'scripts')
Copy-Item (Join-Path $taskArchive '1599-historical-reconciliation.json') $taskFixtureArchive
foreach ($taskIssue in 1919..1925) { Copy-Item (Join-Path $taskArchive "issue-$taskIssue") $taskFixtureArchive -Recurse }
$taskVerifier = Join-Path $taskFixture 'scripts/verify-reconciled-evidence.ps1'
function Assert-TaskVerification([string]$name, [bool]$shouldPass, [string]$diagnostic = '') {
    $taskOutput = & pwsh -NoProfile -File $taskVerifier 2>&1 | Out-String
    $taskOutput | Set-Content -LiteralPath (Join-Path $taskFixture "$name.log")
    if (($LASTEXITCODE -eq 0) -ne $shouldPass) { throw "$name had unexpected verifier result: $taskOutput" }
    if (-not $shouldPass -and -not $taskOutput.Contains($diagnostic)) { throw "$name failed for a different reason: $taskOutput" }
    Write-Output "PASS: $name"
}
function Test-TaskMutation([string]$name, [int]$issue, [scriptblock]$mutate, [string]$diagnostic, [bool]$rehash = $false) {
    $taskDirectory = Join-Path $taskFixtureArchive "issue-$issue"
    $taskPath = Join-Path $taskDirectory 'results.json'
    $taskIntegrityPath = Join-Path $taskDirectory 'integrity.json'
    $taskOriginal = [IO.File]::ReadAllBytes($taskPath)
    $taskOriginalIntegrity = [IO.File]::ReadAllBytes($taskIntegrityPath)
    try {
        $taskReport = Get-Content -Raw $taskPath | ConvertFrom-Json
        & $mutate $taskReport
        [IO.File]::WriteAllText($taskPath, ($taskReport | ConvertTo-Json -Depth 20))
        if ($rehash) {
            $taskIntegrity = Get-Content -Raw $taskIntegrityPath | ConvertFrom-Json
            $taskIntegrity.files.'results.json' = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($taskPath))).ToLowerInvariant()
            [IO.File]::WriteAllText($taskIntegrityPath, ($taskIntegrity | ConvertTo-Json -Depth 5))
        }
        Assert-TaskVerification $name $false $diagnostic
    } finally {
        [IO.File]::WriteAllBytes($taskPath, $taskOriginal)
        [IO.File]::WriteAllBytes($taskIntegrityPath, $taskOriginalIntegrity)
    }
}
Assert-TaskVerification 'original-records' $true
Test-TaskMutation 'alias-failure-erased' 1919 {
    param($report)
    $case = $report.results | Where-Object File -eq 'boolean-alias.cjs'
    ($case.observations | Where-Object mode -eq 'compiled-api').result = $case.ExpectedStdout
} 'retained results.json hash differs'
Test-TaskMutation 'descriptor-timeout-erased' 1925 {
    param($report)
    $case = $report.results | Where-Object File -eq 'primitive-string-length.ts'
    $case.observations = @($case.observations | Where-Object mode -ne 'standalone-execution')
} 'retained results.json hash differs'
Test-TaskMutation 'reference-output-altered' 1920 {
    param($report)
    $case = $report.results | Where-Object File -eq 'inherited-setter.ts'
    ($case.observations | Where-Object mode -eq 'node-reference').result.StandardOutput = 'changed'
} 'Node reference stdout differs' $true
Test-TaskMutation 'il-proof-missing' 1921 {
    param($report)
    ($report.results[0].observations | Where-Object mode -eq 'standalone-cli-compilation').result.StandardOutput = 'Compiled without verification'
} 'Missing recorded IL verification' $true
Test-TaskMutation 'hosted-execution-missing' 1922 {
    param($report)
    $report.results[0].observations = @($report.results[0].observations | Where-Object mode -ne 'hosted-runtime-initialization')
} 'Missing or duplicated hosted-runtime-initialization' $true
Test-TaskMutation 'unexpected-runtime-dependency' 1923 {
    param($report)
    ($report.results[0].observations | Where-Object mode -eq 'standalone-metadata').references += 'SharpTS'
} 'Unexpected SharpTS runtime deployment' $true
Test-TaskMutation 'deadline-relaxed' 1924 { param($report) $report.deadlineSeconds = 60 } 'Collection deadline differs' $true
Assert-TaskVerification 'restored-records' $true
Write-Output "Evidence verifier mutation checks passed. Logs: $taskFixture"
