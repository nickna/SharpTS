param(
    [string]$BaselineCommit = '2a613147',
    [string]$PackageCache = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/editor-syntax-benchmark' }
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
Invoke-CheckedDotNet (@('build', (Join-Path $baselineRoot 'src/SharpTS/SharpTS.csproj'), '-c', 'Release', '-v', 'minimal') + $restoreOptions)
Invoke-CheckedDotNet (@('build', (Join-Path $repoRoot 'src/SharpTS/SharpTS.csproj'), '-c', 'Release', '-v', 'minimal') + $restoreOptions)
foreach ($implementation in @('baseline', 'current')) {
    $libraryRoot = if ($implementation -eq 'baseline') { $baselineRoot } else { $repoRoot }
    $runnerOptions = @('build', (Join-Path $PSScriptRoot 'EditorSyntax.csproj'), '-c', 'Release', '-v', 'minimal',
        "-p:LibraryDirectory=$libraryRoot/src/SharpTS/bin/Release/net10.0",
        "-p:BaseOutputPath=$outputRoot/bin/$implementation/", "-p:BaseIntermediateOutputPath=$outputRoot/obj/$implementation/")
    if ($implementation -eq 'baseline') { $runnerOptions += '-p:Baseline=true' }
    Invoke-CheckedDotNet ($runnerOptions + $restoreOptions)
    $label = if ($implementation -eq 'baseline') { $BaselineCommit } else { 'editor-syntax-current' }
    $previousBenchmarkTiering = $env:DOTNET_TieredCompilation
    try {
        $env:DOTNET_TieredCompilation = '0'
        Invoke-CheckedDotNet @((Join-Path $outputRoot "bin/$implementation/Release/net10.0/SharpTS.SyntaxBenchmark.dll"),
            (Join-Path $outputRoot "$implementation.json"), $label)
    } finally { $env:DOTNET_TieredCompilation = $previousBenchmarkTiering }
}
$before = Get-Content -LiteralPath (Join-Path $outputRoot 'baseline.json') -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath (Join-Path $outputRoot 'current.json') -Raw | ConvertFrom-Json
foreach ($report in $after.reports) {
    $original = $before.reports | Where-Object fixture -eq $report.fixture
    if ($original.resultFingerprint -ne $report.resultFingerprint) { throw "AST mismatch: $($report.fixture), capture=$($report.editorCapture)" }
}
Write-Output "Matching ASTs. Reports: $outputRoot/baseline.json and current.json"
