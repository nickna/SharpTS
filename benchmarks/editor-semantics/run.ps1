param(
    [string]$BaselineCommit = '1cfcc46a',
    [string]$PackageCache = '',
    [string]$OutputDirectory = '',
    [switch]$SkipLibraryBuilds
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/editor-semantics-benchmark' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$baselineRoot = Join-Path $outputRoot ('baseline-' + $BaselineCommit)
$baselineArchive = Join-Path $outputRoot ('baseline-' + $BaselineCommit + '.zip')
if (!(Test-Path -LiteralPath $baselineRoot)) {
    & git -C $repoRoot archive --format=zip "--output=$baselineArchive" $BaselineCommit
    if ($LASTEXITCODE) { throw 'git archive failed.' }
    Expand-Archive -LiteralPath $baselineArchive -DestinationPath $baselineRoot
}
function Invoke-CheckedDotNet([string[]]$DotNetArguments) {
    & dotnet @DotNetArguments
    if ($LASTEXITCODE) { throw "dotnet failed: $($DotNetArguments[0])" }
}
$restoreOptions = @('-p:NuGetAudit=false', '-p:MinVerVersionOverride=0.0.0-benchmark')
if ($PackageCache) {
    $emptyFeed = Join-Path $outputRoot 'empty-feed'
    New-Item -ItemType Directory -Path $emptyFeed -Force | Out-Null
    $restoreOptions += @("-p:RestorePackagesPath=$([IO.Path]::GetFullPath($PackageCache))", '--source', $emptyFeed)
}
if (!$SkipLibraryBuilds) {
    Invoke-CheckedDotNet (@('build', (Join-Path $baselineRoot 'src/SharpTS/SharpTS.csproj'), '-c', 'Release', '-v', 'minimal') + $restoreOptions)
    Invoke-CheckedDotNet (@('build', (Join-Path $repoRoot 'src/SharpTS/SharpTS.csproj'), '-c', 'Release', '-v', 'minimal') + $restoreOptions)
}
foreach ($implementation in @('baseline', 'current')) {
    $libraryRoot = if ($implementation -eq 'baseline') { $baselineRoot } else { $repoRoot }
    $runnerOptions = @('build', (Join-Path $PSScriptRoot 'EditorSemantics.csproj'), '-c', 'Release', '-v', 'minimal',
        "-p:LibraryDirectory=$libraryRoot/src/SharpTS/bin/Release/net10.0",
        "-p:BaseOutputPath=$outputRoot/bin/$implementation/", "-p:BaseIntermediateOutputPath=$outputRoot/obj/$implementation/")
    if ($implementation -eq 'baseline') { $runnerOptions += '-p:Baseline=true' }
    Invoke-CheckedDotNet ($runnerOptions + $restoreOptions)
    $label = if ($implementation -eq 'baseline') { $BaselineCommit } else { 'editor-semantics-current' }
    $previousBenchmarkTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        Invoke-CheckedDotNet @((Join-Path $outputRoot "bin/$implementation/Release/net10.0/SharpTS.EditorSemanticsBenchmark.dll"),
            (Join-Path $outputRoot "$implementation.json"), $label)
    } finally { $env:DOTNET_TieredCompilation = $previousBenchmarkTiering }
}
$before = Get-Content -LiteralPath (Join-Path $outputRoot 'baseline.json') -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath (Join-Path $outputRoot 'current.json') -Raw | ConvertFrom-Json
foreach ($report in $after.reports) {
    $original = @($before.reports | Where-Object { $_.fixture -eq $report.fixture -and $_.mode -eq 'Ordinary' })
    if ($original.Count -ne 1) { throw "Expected one ordinary baseline for $($report.fixture)." }
    foreach ($category in @('diagnostics', 'types', 'lexical')) {
        if ($original[0].fingerprints.$category -ne $report.fingerprints.$category) {
            throw "$category mismatch: $($report.fixture), mode=$($report.mode)"
        }
    }
}
$fixtures = foreach ($original in $before.reports) {
    $current = @($after.reports | Where-Object fixture -eq $original.fixture)
    $ordinary = $current | Where-Object mode -eq 'Ordinary'
    $members = $current | Where-Object mode -eq 'Members'
    $editor = $current | Where-Object mode -eq 'Editor'
    [ordered]@{
        name = $original.fixture
        allVariantFingerprintsMatch = $true
        fingerprints = $original.fingerprints
        variants = [ordered]@{ baselineOrdinary = $original; currentOrdinary = $ordinary; currentMembers = $members; currentEditor = $editor }
        ordinaryMedianChangePercent = ($ordinary.medianMilliseconds / $original.medianMilliseconds - 1) * 100
        ordinaryAllocatedChangePercent = ($ordinary.medianAllocatedBytes / $original.medianAllocatedBytes - 1) * 100
        editorOverMembersMedianMilliseconds = $editor.medianMilliseconds - $members.medianMilliseconds
        editorOverMembersAllocatedBytes = $editor.medianAllocatedBytes - $members.medianAllocatedBytes
        editorOverMembersRetainedHeapDeltaBytes = $editor.retainedPublishedHeapDeltaBytes - $members.retainedPublishedHeapDeltaBytes
        editorOverMembersMedianRatio = $editor.medianMilliseconds / $members.medianMilliseconds
    }
}
$comparison = [ordered]@{
    baselineCommit = $BaselineCommit
    baselineCommitResolved = (& git -C $repoRoot rev-parse "$BaselineCommit^{commit}").Trim()
    currentBaseCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
    current = 'Uncommitted #1976 implementation on 1cfcc46a.'
    baselineTimestampUtc = $before.timestampUtc
    currentTimestampUtc = $after.timestampUtc
    environment = [ordered]@{ sdk = (& dotnet --version).Trim(); runtime = $after.runtime; os = $after.os; architecture = $after.architecture; processorCount = $after.processorCount; tieredCompilation = $after.tieredCompilation }
    samples = $after.samples
    warmupsPerVariant = 30
    methodology = $after.methodology
    replay = './benchmarks/editor-semantics/run.ps1 -PackageCache <existing NuGet cache>'
    fixtures = @($fixtures)
}
$comparisonPath = Join-Path $outputRoot 'comparison.json'
$comparison | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $comparisonPath -Encoding utf8
Write-Output "Matching diagnostics/types/lexical identities. Reports: $outputRoot/baseline.json, current.json and comparison.json"
