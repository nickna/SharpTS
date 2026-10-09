param(
    [string]$BaselineCommit = 'df4589b7',
    [string]$PackageCache = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/analysis-benchmark' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$baselineRoot = Join-Path $outputRoot ('baseline-' + $BaselineCommit)
$baselineArchive = Join-Path $outputRoot ('baseline-' + $BaselineCommit + '.zip')
if (!(Test-Path -LiteralPath $baselineRoot)) {
    & git -C $repoRoot archive --format=zip "--output=$baselineArchive" $BaselineCommit
    if ($LASTEXITCODE) { throw 'git archive failed.' }
    Expand-Archive -LiteralPath $baselineArchive -DestinationPath $baselineRoot
}

# The archived baseline's only semantic-source change is a counter at CheckModules entry.
$checkerPath = Join-Path $baselineRoot 'src/SharpTS/TypeSystem/TypeChecker.cs'
$checker = [IO.File]::ReadAllText($checkerPath)
if (!$checker.Contains('internal static long BenchmarkChecks;')) {
    $signature = 'public TypeMap CheckModules(List<ParsedModule> modules, ModuleResolver resolver)'
    if (($checker.Split($signature).Length - 1) -ne 1) { throw 'Expected exactly one baseline CheckModules.' }
    $checker = $checker.Replace($signature, 'internal static long BenchmarkChecks;' + [Environment]::NewLine + '    ' + $signature)
    $checker = [regex]::Replace($checker,
        '(public TypeMap CheckModules\(List<ParsedModule> modules, ModuleResolver resolver\)\s*\{)',
        '$1' + [Environment]::NewLine + '        Interlocked.Increment(ref BenchmarkChecks);')
    [IO.File]::WriteAllText($checkerPath, $checker)
}

function Invoke-CheckedDotNet([string[]]$DotNetArguments) {
    & dotnet @DotNetArguments
    if ($LASTEXITCODE) { throw "dotnet failed: $($DotNetArguments[0])" }
}

$restoreOptions = @('-p:NuGetAudit=false', '-p:MinVerVersionOverride=0.0.0-benchmark')
if ($PackageCache) {
    # Optional offline replay against an already populated cache; never downloads into it.
    $emptyFeed = Join-Path $outputRoot 'empty-feed'
    New-Item -ItemType Directory -Path $emptyFeed -Force | Out-Null
    $restoreOptions += @("-p:RestorePackagesPath=$([IO.Path]::GetFullPath($PackageCache))", '--source', $emptyFeed)
}
Invoke-CheckedDotNet (@('build', (Join-Path $baselineRoot 'src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj'), '-c', 'Release', '-v', 'minimal') + $restoreOptions)
# Build current separately so both measurements use exactly the requested source state.
Invoke-CheckedDotNet (@('build', (Join-Path $repoRoot 'src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj'), '-c', 'Release', '-v', 'minimal') + $restoreOptions)

$runnerProject = Join-Path $PSScriptRoot 'EditorAnalysis.csproj'
foreach ($implementation in @('baseline', 'current')) {
    $libraryRoot = if ($implementation -eq 'baseline') { $baselineRoot } else { $repoRoot }
    $libraryDirectory = Join-Path $libraryRoot 'src/SharpTS.LanguageServer/bin/Release/net10.0'
    $runnerOptions = @('build', $runnerProject, '-c', 'Release', '-v', 'minimal',
        "-p:LibraryDirectory=$libraryDirectory", "-p:BaseOutputPath=$outputRoot/bin/$implementation/",
        "-p:BaseIntermediateOutputPath=$outputRoot/obj/$implementation/")
    if ($implementation -eq 'baseline') { $runnerOptions += '-p:Baseline=true' }
    Invoke-CheckedDotNet ($runnerOptions + $restoreOptions)
    Invoke-CheckedDotNet @((Join-Path $outputRoot "bin/$implementation/Release/net10.0/SharpTS.Tests.dll"), $outputRoot, "$implementation.json", $BaselineCommit)
}

$before = Get-Content -LiteralPath (Join-Path $outputRoot 'baseline.json') -Raw | ConvertFrom-Json
$after = Get-Content -LiteralPath (Join-Path $outputRoot 'current.json') -Raw | ConvertFrom-Json
for ($index = 0; $index -lt $before.reports.Count; $index++) {
    if ($before.reports[$index].cold.resultFingerprint -ne $after.reports[$index].cold.resultFingerprint) {
        throw "Baseline/current result mismatch: $($before.reports[$index].name)"
    }
}
Write-Output "Matching query results. Reports: $outputRoot/baseline.json and current.json"
