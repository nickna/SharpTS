Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'core-shard-plan.ps1')
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid coverage plan was accepted.' }
}
$a = 'SharpTS.Tests.SharedTests.A.Test(value: 1)'
$b = 'SharpTS.Tests.SharedTests.B.Test'
$c = 'SharpTS.Tests.CompilerTests.C.Test'
$d = 'SharpTS.Tests.NewArea.NewClass.Test'
$entries = @($a, $a, $b, $c, $d)
$durations = @{ 'SharpTS.Tests.SharedTests.A' = @{ rows = 2; seconds = 50 } }
$plan = New-CoreShardPlan -Entries $entries -ShardCount 3 -Durations $durations
$reversed = $entries.Clone(); [Array]::Reverse($reversed)
$again = New-CoreShardPlan -Entries $reversed -ShardCount 3 -Durations $durations
if (($plan | ConvertTo-Json -Depth 8 -Compress) -cne ($again | ConvertTo-Json -Depth 8 -Compress)) {
    throw 'Core shard assignment depends on discovery ordering.'
}
if ($plan.DiscoveredEntries -ne 5 -or $plan.Shards[0].Entries -ne 2) { throw 'Lost duplicates or ignored measured cost.' }
Assert-SameTestInventory @($plan.Classes) @($plan.Shards | ForEach-Object { $_.Classes.ToArray() })
Reject { New-CoreShardPlan -Entries @('unexpected') -ShardCount 1 }
Reject { New-CoreShardPlan -Entries @($a) -ShardCount 2 }
$cadence = New-TestCadenceProof -Full $entries -Pr @($a, $b, $c, $d)
if ($cadence.DeferredEntries -ne 1 -or -not $cadence.EveryMethodOnPr) { throw 'Cadence lost multiplicity.' }
Reject { New-TestCadenceProof -Full $entries -Pr @($a, $b, $c) }
Reject { New-TestCadenceProof -Full $entries -Pr @($a, $a, $a, $b, $c, $d) }
Reject { Assert-SameTestInventory @($a, $b) @($a, $a) }
Reject { New-TestCadenceProof -Full @() -Pr @() }
$routine = & (Join-Path $PSScriptRoot 'get-test-coverage-scope.ps1') -ChangedPaths @('src/SharpTS/TypeSystem/TypeChecker.cs')
if ($routine.Deployment -ne 'Pr' -or $routine.Gui -ne 'Pr') { throw 'Routine code changes should retain representative boundary tests.' }
foreach ($path in @('.github/workflows/ci.yml', 'scripts/test-inventory.ps1', 'global.json',
    'Directory.Build.props', 'tests/fixtures/Example/main.ts', 'tests/Shared/TestProcess.cs',
    'src/SharpTS/Compilation/ILCompiler.cs', 'src/SharpTS.Sdk/Sdk/Sdk.targets')) {
    $scope = & (Join-Path $PSScriptRoot 'get-test-coverage-scope.ps1') -ChangedPaths @($path)
    if ($scope.Deployment -ne 'Full') { throw "Deployment-affecting change did not select full coverage: $path" }
}
$forced = & (Join-Path $PSScriptRoot 'get-test-coverage-scope.ps1') -Full
if ($forced.Deployment -ne 'Full' -or $forced.Gui -ne 'Full' -or $forced.Benchmarks -ne 'Full') { throw 'Full override is incomplete.' }
$gui = & (Join-Path $PSScriptRoot 'get-test-coverage-scope.ps1') -ChangedPaths @('samples/SharpPaint/presentation.tests.tsx')
if ($gui.Gui -ne 'Full') { throw 'GUI changes must exercise every presentation case.' }
$changedTests = & (Join-Path $PSScriptRoot 'get-test-coverage-scope.ps1') -ChangedPaths @(
    'tests/SharpTS.Tests/CompilerTests/StandaloneDllTests.HistoricalRuntime.cs',
    'tests/SharpTS.Tests/SharedTests/RuntimeSuperclassTests.cs')
if ($changedTests.Deployment -ne 'Pr' -or $changedTests.DeploymentMethods -notmatch 'Isolated_HistoricalRuntimeSources0' -or
    $changedTests.DeploymentTypes -notmatch 'RuntimeSuperclassTests') { throw 'Changed tests did not expand their own deployment rows.' }
$deletedTests = & (Join-Path $PSScriptRoot 'get-test-coverage-scope.ps1') -ChangedPaths @('tests/SharpTS.Tests/RemovedTest.cs')
if ($deletedTests.Deployment -ne 'Full') { throw 'Unavailable changed source must fall back to full deployment.' }

# Exercise the actual gate with complete evidence, then remove/duplicate a shard
# and truncate execution. A green matrix alone is not proof of complete coverage.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-shard-gate-' + [guid]::NewGuid().ToString('N'))
foreach ($os in @('ubuntu-24.04', 'windows-2025')) {
    $directory = Join-Path $fixture "core-partitions-$os"
    [void][IO.Directory]::CreateDirectory($directory)
    [ordered]@{ BaseEntries = 5; InProcessEntries = 4; ProcessEntries = 1; ExactOnce = $true
        InProcessInventoryDigest = Get-InventoryDigest @('Unit0', 'Unit1', 'Unit2', 'Unit3') } |
        ConvertTo-Json | Set-Content (Join-Path $directory 'core-partitions.json')
    foreach ($kind in @('core', 'standalone')) {
        $count = if ($kind -eq 'core') { 4 } else { 8 }
        $units = @(0..($count - 1) | ForEach-Object { if ($kind -eq 'core') { "Unit$_" } else { "Fixture.Unit$_" } })
        for ($i = 0; $i -lt $count; $i++) {
            $directory = Join-Path $fixture "$kind-shard-$os-$i"
            [void][IO.Directory]::CreateDirectory($directory)
            [ordered]@{ Kind = $kind; Coverage = 'Pr'; ShardIndex = $i; ShardCount = $count
                InventoryDigest = Get-InventoryDigest $units; DiscoveredEntries = $count
                AllUnits = $units; SelectedUnits = @($units[$i]); SelectedEntries = 1; ExactOnce = $true } |
                ConvertTo-Json | Set-Content (Join-Path $directory 'shard-proof.json')
            $class = if ($kind -eq 'core') { "Unit$i" } else { 'Fixture' }
            $method = if ($kind -eq 'core') { 'Case' } else { "Unit$i" }
            "<TestRun><Results><UnitTestResult testId='1' /></Results><TestDefinitions><UnitTest id='1'><TestMethod className='$class' name='$method' /></UnitTest></TestDefinitions><ResultSummary outcome='Completed'><Counters total='1' executed='1' failed='0' /></ResultSummary></TestRun>" |
                Set-Content (Join-Path $directory 'test.trx')
        }
    }
}
$gate = Join-Path $PSScriptRoot 'verify-ci-shard-results.ps1'
& $gate -ResultsDirectory $fixture
$partitionPath = Join-Path $fixture 'core-partitions-windows-2025/core-partitions.json'
$originalPartition = Get-Content $partitionPath -Raw
$wrongPartition = $originalPartition | ConvertFrom-Json
$wrongPartition.InProcessInventoryDigest = 'different-base-inventory'
$wrongPartition | ConvertTo-Json | Set-Content $partitionPath
Reject { & $gate -ResultsDirectory $fixture }
$originalPartition | Set-Content $partitionPath
$proofPath = Join-Path $fixture 'core-shard-windows-2025-0/shard-proof.json'
$original = Get-Content $proofPath -Raw
$bad = $original | ConvertFrom-Json
$bad.SelectedUnits = @('Unit1')
$bad | ConvertTo-Json | Set-Content $proofPath
Reject { & $gate -ResultsDirectory $fixture }
$original | Set-Content $proofPath
Move-Item -LiteralPath $proofPath -Destination "$proofPath.missing"
Reject { & $gate -ResultsDirectory $fixture }
Move-Item -LiteralPath "$proofPath.missing" -Destination $proofPath
$trxPath = Join-Path $fixture 'core-shard-windows-2025-0/test.trx'
$wrongTest = (Get-Content $trxPath -Raw).Replace("className='Unit0'", "className='Unexpected'")
$wrongTest | Set-Content $trxPath
Reject { & $gate -ResultsDirectory $fixture }
'<TestRun><ResultSummary outcome="Completed"><Counters total="0" executed="0" failed="0" /></ResultSummary></TestRun>' |
    Set-Content (Join-Path $fixture 'core-shard-windows-2025-0/test.trx')
Reject { & $gate -ResultsDirectory $fixture }
Write-Host 'Core shard and deployment cadence regressions passed.'
