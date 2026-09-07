[CmdletBinding()]
param(
    [ValidateSet('smoke', 'full')]
    [string]$Profile = 'smoke',
    [string]$ResultsDirectory = 'artifacts/typescript-conformance',
    [switch]$NoBuild,
    [switch]$NoAcquire
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = 'tests/conformance/SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj'
$results = [IO.Path]::GetFullPath($ResultsDirectory, $repositoryRoot)
# A unique run directory prevents a stale summary/TRX from passing a zero-test invocation.
$runDirectory = Join-Path $results ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$environmentNames = @('SHARPTS_TSCONFORMANCE_GATE_PROFILE', 'SHARPTS_TSCONFORMANCE_REPORT_DIR')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }

Push-Location $repositoryRoot
try {
    if ($env:SHARPTS_TSCONFORMANCE_UPDATE_BASELINE -in @('1', 'true')) {
        throw 'Baseline updates are forbidden in this gate. Unset SHARPTS_TSCONFORMANCE_UPDATE_BASELINE.'
    }
    $gitlink = & git ls-tree HEAD external/typescript
    if ($LASTEXITCODE -ne 0 -or $gitlink -notmatch '^160000 commit ([0-9a-f]{40})\s+external/typescript$') {
        throw 'Cannot resolve the pinned TypeScript corpus gitlink.'
    }
    $revision = $Matches[1]
    if (-not $NoAcquire) {
        & git -c core.longpaths=true submodule update --init --depth 1 external/typescript
        if ($LASTEXITCODE -ne 0) { throw 'Cannot acquire the pinned TypeScript corpus (network/submodule failure).' }
    }
    $corpus = Join-Path $repositoryRoot 'external/typescript'
    foreach ($required in @('src/compiler/checker.ts', 'tests/cases/conformance', 'tests/baselines/reference', 'tests/lib', 'package.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $corpus $required))) {
            throw "TypeScript corpus is unavailable/incomplete: missing $required. Run without -NoAcquire."
        }
    }
    $actualRevision = & git -C $corpus rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualRevision -cne $revision) {
        throw "TypeScript corpus revision mismatch: expected $revision; got $actualRevision."
    }
    & git -C $corpus diff --quiet HEAD --
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript corpus has modified/missing tracked files; use a clean pinned checkout.' }
    $untracked = & git -C $corpus ls-files --others --exclude-standard
    if ($LASTEXITCODE -ne 0 -or $untracked) { throw 'TypeScript corpus must not contain untracked reference inputs.' }
    $version = (Get-Content -LiteralPath (Join-Path $corpus 'package.json') -Raw | ConvertFrom-Json).version
    if ($version -cne '6.0.3') { throw "Expected TypeScript reference version 6.0.3, got $version. Update the pin deliberately." }
    @{
        profile = $Profile; corpus = $revision; referenceVersion = $version
        reference = 'Checked-in tsc diagnostics at tests/baselines/reference; no live tsc invocation'
        dotnet = (& dotnet --version)
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'inputs.json')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the .NET SDK from global.json.' }

    $env:SHARPTS_TSCONFORMANCE_GATE_PROFILE = $Profile
    $env:SHARPTS_TSCONFORMANCE_REPORT_DIR = $runDirectory
    $arguments = @('test', $project, '--configuration', 'Release', '-m:1',
        '--filter', 'FullyQualifiedName=SharpTS.TypeScriptConformance.TypeScriptConformanceTests.InterpretedBaseline|FullyQualifiedName~SharpTS.TypeScriptConformance.TypeScriptConformanceGateTests',
        '--logger', 'trx;LogFileName=conformance.trx', '--results-directory', $runDirectory,
        '--blame-hang-timeout', $(if ($Profile -eq 'smoke') { '3m' } else { '12m' }),
        '--blame-hang-dump-type', 'none')
    if ($NoBuild) { $arguments += '--no-build' }
    & dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'test.log') | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "TypeScript $Profile baseline gate failed. See $runDirectory." }
    $summaryPath = Join-Path $runDirectory 'summary.json'
    if (-not (Test-Path -LiteralPath $summaryPath)) { throw 'Baseline fact did not complete; no summary was produced.' }
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    [xml]$trx = Get-Content -LiteralPath (Join-Path $runDirectory 'conformance.trx') -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if (-not $summary.completed -or -not $summary.passed -or $summary.count -le 0 -or
        [int]$counters.executed -le 0 -or [int]$counters.failed -ne 0) {
        throw 'TypeScript gate did not execute a successful, non-empty baseline comparison.'
    }
    Write-Host "TypeScript $Profile gate passed: $($summary.count) corpus cases in $($summary.seconds.ToString('F1'))s. Artifacts: $runDirectory"
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runDirectory 'failure.txt')
    throw
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Pop-Location
}
