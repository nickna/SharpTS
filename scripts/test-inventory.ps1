# Shared, bounded VSTest discovery and multiset checks. No source-code inventory:
# theories are discovered from the exact binaries that the job will execute.
function Get-TestInventory {
    param([string]$Project, [string]$Configuration = 'Release', [string]$Filter,
        [string]$Settings, [ValidateSet('Full', 'Pr')][string]$Coverage = 'Full')
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = Split-Path -Parent $PSScriptRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['SHARPTS_DEPLOYMENT_COVERAGE'] = $Coverage
    foreach ($arg in @('test', $Project, '--no-build', '--configuration', $Configuration,
        '--list-tests', '--verbosity', 'quiet')) { $start.ArgumentList.Add($arg) }
    if ($Filter) { $start.ArgumentList.Add('--filter'); $start.ArgumentList.Add($Filter) }
    if ($Settings) { $start.ArgumentList.Add('--settings'); $start.ArgumentList.Add($Settings) }
    $process = [Diagnostics.Process]::Start($start)
    $reads = [Threading.CancellationTokenSource]::new()
    $stdout = $process.StandardOutput.ReadToEndAsync($reads.Token)
    $stderr = $process.StandardError.ReadToEndAsync($reads.Token)
    try {
        if (-not $process.WaitForExit(60000)) { throw 'Test discovery exceeded 60 seconds.' }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000)) {
            throw 'Test discovery pipes did not close.'
        }
        if ($process.ExitCode -ne 0) { throw "Discovery failed: $($stdout.Result)`n$($stderr.Result)" }
        $listing = $false
        $entries = [Collections.Generic.List[string]]::new()
        foreach ($line in [regex]::Split($stdout.Result, '\r?\n')) {
            if ($line -eq 'The following Tests are available:') { $listing = $true; continue }
            if (-not $listing -or [string]::IsNullOrWhiteSpace($line)) { continue }
            if ($line -notmatch '^\s+(SharpTS\.[A-Za-z0-9_.+]+(?:\(.*\))?)$') {
                throw "Unrecognized test identity; refusing partial coverage: $line"
            }
            $entries.Add($Matches[1])
        }
        if ($entries.Count -eq 0) { throw 'No tests were discovered.' }
        return $entries.ToArray()
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        $reads.Cancel()
        try { [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000) } catch { }
        $process.Dispose(); $reads.Dispose()
    }
}

function Get-InventoryCounts([string[]]$Entries) {
    $counts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Entries) {
        if (-not $counts.ContainsKey($entry)) { $counts[$entry] = 0 }
        $counts[$entry]++
    }
    return ,$counts
}

function Get-InventoryDigest([string[]]$Entries) {
    [string[]]$sorted = $Entries.Clone()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    return [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($sorted -join "`n")))
}

function Assert-SameTestInventory([string[]]$Expected, [string[]]$Actual) {
    if ($Expected.Count -ne $Actual.Count -or (Get-InventoryDigest $Expected) -cne (Get-InventoryDigest $Actual)) {
        throw 'Test discovery lost, duplicated or added entries.'
    }
}

function New-TestCadenceProof([string[]]$Full, [string[]]$Pr) {
    if ($Full.Count -eq 0 -or $Pr.Count -eq 0) { throw 'Full and PR inventories must be nonempty.' }
    $remaining = Get-InventoryCounts $Full
    foreach ($entry in $Pr) {
        if (-not $remaining.ContainsKey($entry) -or $remaining[$entry] -eq 0) {
            throw "PR inventory is not a subset of full discovery: $entry"
        }
        $remaining[$entry]--
    }
    $fullMethods = @($Full | ForEach-Object { ($_ -split '\(', 2)[0] } | Sort-Object -Unique)
    $prMethods = @($Pr | ForEach-Object { ($_ -split '\(', 2)[0] } | Sort-Object -Unique)
    Assert-SameTestInventory $fullMethods $prMethods
    $deferred = @($remaining.Keys | Sort-Object | ForEach-Object {
        for ($i = 0; $i -lt $remaining[$_]; $i++) { $_ }
    })
    Assert-SameTestInventory $Full @($Pr + $deferred)
    [ordered]@{
        FullEntries = $Full.Count; PrEntries = $Pr.Count; DeferredEntries = $deferred.Count
        FullDigest = Get-InventoryDigest $Full; PrDigest = Get-InventoryDigest $Pr
        EveryMethodOnPr = $true; ExactOnce = $true
        # Retain the reviewable rows, not just a scalar count. Full daily runs
        # execute PR + Deferred; there is no separate allowlist that can go stale.
        Pr = $Pr; Deferred = $deferred
    }
}

function Assert-TestResults([string]$Path, [int]$MinimumTests,
    [string[]]$ExpectedUnits = @(), [ValidateSet('core', 'standalone')][string]$Kind = 'core') {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Test results missing: $Path" }
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt $MinimumTests -or [int]$counters.executed -eq 0) {
        throw "Test execution did not cover its discovery inventory: $Path"
    }
    if ([int]$counters.failed -ne 0 -or $trx.TestRun.ResultSummary.outcome -eq 'Failed') {
        throw "Tests failed: $Path"
    }
    if ($ExpectedUnits.Count -gt 0) {
        $completedIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($result in $trx.TestRun.Results.UnitTestResult) { [void]$completedIds.Add($result.testId) }
        $units = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($definition in $trx.TestRun.TestDefinitions.UnitTest) {
            if (-not $completedIds.Contains($definition.id)) { continue }
            $class = [string]$definition.TestMethod.className
            $method = ([string]$definition.TestMethod.name -split '\(', 2)[0]
            [void]$units.Add($(if ($Kind -eq 'core') { $class } else { "$class.$method" }))
        }
        Assert-SameTestInventory $ExpectedUnits @($units)
    }
}
