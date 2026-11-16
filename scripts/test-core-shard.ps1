param(
    [Parameter(Mandatory)][ValidateRange(0, 15)][int]$ShardIndex,
    [ValidateRange(1, 16)][int]$ShardCount = 4,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'artifacts/core-shards',
    [switch]$DescribeOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($ShardIndex -ge $ShardCount) { throw 'ShardIndex must be less than ShardCount.' }
. (Join-Path $PSScriptRoot 'core-test-partitions.ps1')
. (Join-Path $PSScriptRoot 'core-shard-plan.ps1')
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'tests/SharpTS.Tests/SharpTS.Tests.csproj'
$filter = (Get-CoreTestFilters).InProcess
$entries = @(Get-TestInventory -Project $project -Configuration $Configuration -Filter $filter)
$timings = Get-Content (Join-Path $repository '.github/core-test-durations.json') -Raw | ConvertFrom-Json -AsHashtable
$plan = New-CoreShardPlan -Entries $entries -ShardCount $ShardCount -Durations $timings.classes
$shard = $plan.Shards[$ShardIndex]
$results = [IO.Path]::GetFullPath($ResultsDirectory, $repository)
[void][IO.Directory]::CreateDirectory($results)
# Runsettings avoids the Windows command-line limit for thousands of class names.
$classFilter = ($shard.Classes | ForEach-Object { "FullyQualifiedName~$_." }) -join '|'
$settings = Join-Path $results 'core.runsettings'
$xmlFilter = [Security.SecurityElement]::Escape("$filter&($classFilter)")
[IO.File]::WriteAllText($settings, "<RunSettings><RunConfiguration><TestCaseFilter>$xmlFilter</TestCaseFilter></RunConfiguration></RunSettings>")
$selected = @(Get-TestInventory -Project $project -Configuration $Configuration -Settings $settings)
$expected = @($entries | Where-Object {
    $method = ($_ -split '\(', 2)[0]
    $shard.Classes.Contains($method.Substring(0, $method.LastIndexOf('.')))
})
Assert-SameTestInventory $expected $selected
$proof = [ordered]@{
    Kind = 'core'; ShardIndex = $ShardIndex; ShardCount = $ShardCount
    InventoryDigest = $plan.InventoryDigest; DiscoveredEntries = $plan.DiscoveredEntries
    AllUnits = $plan.Classes; SelectedUnits = $shard.Classes.ToArray()
    SelectedEntries = $selected.Count; EstimatedSeconds = $shard.Weight; ExactOnce = $true
}
$proof | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $results 'shard-proof.json')
Write-Host "Core shard $ShardIndex/${ShardCount}: $($selected.Count) entries, $($shard.Classes.Count) classes."
if ($DescribeOnly) { return }
& dotnet test $project --no-build --configuration $Configuration --settings $settings `
    --logger 'console;verbosity=minimal' --logger 'trx;LogFileName=core.trx' `
    --results-directory $results
if ($LASTEXITCODE -ne 0) { throw "Core shard failed with exit $LASTEXITCODE." }
Assert-TestResults (Join-Path $results 'core.trx') $selected.Count $shard.Classes.ToArray() core
