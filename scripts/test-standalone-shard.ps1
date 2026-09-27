param(
    [Parameter(Mandatory)][ValidateRange(0, 31)][int]$ShardIndex,
    [ValidateRange(1, 32)][int]$ShardCount = 3,
    [switch]$DescribeOnly,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$IncludeExternal,
    [ValidateRange(1, 3600)][int]$HangTimeoutSeconds = 120,
    [string]$ResultsDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($ShardIndex -ge $ShardCount) {
    throw 'ShardIndex must be less than ShardCount.'
}

$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'tests/SharpTS.Tests/SharpTS.Tests.csproj'
$baseFilter = 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm&FullyQualifiedName~SharpTS.Tests.CompilerTests.StandaloneDllTests.'
if ($IncludeExternal) {
    $baseFilter = 'FullyQualifiedName~SharpTS.Tests.CompilerTests.StandaloneDllTests.'
}

# Discover from the built assembly so new methods automatically join a shard.
# Keep all rows of a theory together; its test method is the stable identity.
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('test', $project, '--no-build', '--configuration', $Configuration,
    '--list-tests', '--verbosity', 'quiet', '--filter', $baseFilter)) { $start.ArgumentList.Add($argument) }
$process = [Diagnostics.Process]::Start($start)
$reads = [Threading.CancellationTokenSource]::new()
$stdout = $process.StandardOutput.ReadToEndAsync($reads.Token)
$stderr = $process.StandardError.ReadToEndAsync($reads.Token)
try {
    if (-not $process.WaitForExit(60000)) { throw 'Standalone test discovery exceeded 60 seconds.' }
    if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000)) {
        throw 'Standalone test discovery output did not close within 5 seconds.'
    }
    $discovery = @([regex]::Split($stdout.Result + $stderr.Result, '\r?\n'))
    if ($process.ExitCode -ne 0) {
        throw "Standalone test discovery failed:`n$($discovery -join [Environment]::NewLine)"
    }
} finally {
    if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
    $reads.Cancel()
    try { [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000) } catch { }
    $process.Dispose()
    $reads.Dispose()
}
$listedMethods = [System.Collections.Generic.List[string]]::new()
$inListing = $false
foreach ($line in $discovery) {
    if ([string]$line -eq 'The following Tests are available:') {
        $inListing = $true
        continue
    }
    if (-not $inListing -or [string]::IsNullOrWhiteSpace([string]$line)) { continue }
    $match = [regex]::Match([string]$line, '^\s*(SharpTS\.Tests\.CompilerTests\.StandaloneDllTests\.\w+)(?:\(|\s*$)')
    if (-not $match.Success) {
        throw "Unrecognized standalone test identity; refusing partial coverage: $line"
    }
    $listedMethods.Add($match.Groups[1].Value)
}
$methods = @($listedMethods | Sort-Object -Unique)
if ($methods.Count -eq 0) {
    throw 'No standalone test methods were discovered.'
}

$selected = @($methods | Where-Object {
    $digest = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($_))
    ($digest[0] % $ShardCount) -eq $ShardIndex
})
if ($selected.Count -eq 0) {
    throw "Standalone shard $ShardIndex is empty."
}
$methodFilter = ($selected | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
$filter = "$baseFilter&($methodFilter)"
if ($filter.Length -gt 24000) {
    throw 'The standalone shard filter is too long for a portable command line.'
}

if ($DescribeOnly) {
    [ordered]@{
        ShardIndex = $ShardIndex
        ShardCount = $ShardCount
        BaseFilter = $baseFilter
        DiscoveredEntries = $listedMethods.Count
        Methods = $methods
        SelectedMethods = $selected
        Filter = $filter
    } | ConvertTo-Json -Depth 4
    return
}

Write-Host "Standalone shard $ShardIndex/$ShardCount runs $($selected.Count) of $($methods.Count) methods, including every selected theory row."
$testArguments = @('test', $project, '--no-build', '--configuration', $Configuration,
    '--logger', 'console;verbosity=normal', '--filter', $filter,
    '--blame-hang-timeout', "${HangTimeoutSeconds}s", '--blame-hang-dump-type', 'mini')
if ($ResultsDirectory) {
    $testArguments += @('--results-directory', $ResultsDirectory, '--logger', "trx;LogFileName=standalone-$ShardIndex.trx")
}
& dotnet @testArguments
exit $LASTEXITCODE
