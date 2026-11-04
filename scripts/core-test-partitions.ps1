function Get-CoreTestFilters {
    $baseFilter = 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm&FullyQualifiedName!~SharpTS.Tests.CompilerTests.StandaloneDllTests.'
    [ordered]@{
        Base = $baseFilter
        InProcess = $baseFilter + '&FullyQualifiedName!~SharpTS.Tests.IntegrationTests.&FullyQualifiedName!~SharpTS.Tests.PackagingTests.'
        Process = $baseFilter + '&(FullyQualifiedName~SharpTS.Tests.IntegrationTests.|FullyQualifiedName~SharpTS.Tests.PackagingTests.)'
    }
}

function New-CoreTestPartitionProof {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$BaseEntries,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$InProcessEntries,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ProcessEntries
    )
    function Get-Counts([string[]]$Entries) {
        $counts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
        foreach ($entry in $Entries) {
            if ($entry -notmatch '^SharpTS\.Tests\.[A-Za-z0-9_.]+(?:\(|$)') {
                throw "Unrecognized core test identity; refusing partial coverage: $entry"
            }
            if (-not $counts.ContainsKey($entry)) { $counts[$entry] = 0 }
            $counts[$entry]++
        }
        return ,$counts
    }
    if ($BaseEntries.Count -eq 0 -or $InProcessEntries.Count -eq 0 -or $ProcessEntries.Count -eq 0) {
        throw 'Every core partition must discover tests.'
    }
    $baseCounts = Get-Counts $BaseEntries
    $inProcessCounts = Get-Counts $InProcessEntries
    $processCounts = Get-Counts $ProcessEntries
    foreach ($name in $inProcessCounts.Keys) {
        if ($processCounts.ContainsKey($name)) { throw "Core partitions overlap: $name" }
    }
    foreach ($name in @($inProcessCounts.Keys) + @($processCounts.Keys)) {
        if (-not $baseCounts.ContainsKey($name)) { throw "Core partition contains an extra test: $name" }
    }
    foreach ($name in $baseCounts.Keys) {
        $actual = 0
        if ($inProcessCounts.ContainsKey($name)) { $actual += $inProcessCounts[$name] }
        if ($processCounts.ContainsKey($name)) { $actual += $processCounts[$name] }
        if ($actual -ne $baseCounts[$name]) { throw "Core partitions lost or duplicated discovery entries: $name" }
    }
    $names = [string[]]@($baseCounts.Keys)
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $inventory = @($names | ForEach-Object { "$_`t$($baseCounts[$_])" })
    $digest = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($inventory -join "`n")))
    [ordered]@{
        Filters = Get-CoreTestFilters
        BaseEntries = $BaseEntries.Count
        InProcessEntries = $InProcessEntries.Count
        ProcessEntries = $ProcessEntries.Count
        InventoryDigest = $digest
        Inventory = $inventory
        ExactOnce = $true
    }
}
