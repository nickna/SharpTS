. (Join-Path $PSScriptRoot 'test-inventory.ps1')

function New-CoreShardPlan {
    param([string[]]$Entries, [ValidateRange(1, 16)][int]$ShardCount = 4,
        [System.Collections.IDictionary]$Durations = @{})
    $classes = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Entries) {
        if ($entry -notmatch '^(SharpTS\.Tests\.[A-Za-z0-9_.+]+)\.\w+(?:\(|$)') {
            throw "Unknown core identity: $entry"
        }
        $name = $Matches[1]
        if (-not $classes.ContainsKey($name)) { $classes[$name] = 0 }
        $classes[$name]++
    }
    if ($classes.Count -lt $ShardCount) { throw 'Not enough core classes for nonempty shards.' }
    $priority = [Collections.Generic.List[object]]::new()
    foreach ($name in $classes.Keys) {
        # A duration is only a balancing hint, never a selection allowlist. New
        # classes and new rows always participate and receive a conservative cost.
        $weight = $classes[$name] * 0.1
        if ($Durations.Contains($name)) {
            $sample = $Durations[$name]
            $weight = [Math]::Max(0.001, [double]$sample.seconds / [Math]::Max(1, [int]$sample.rows)) * $classes[$name]
        }
        $priority.Add([pscustomobject]@{ Name = $name; Weight = $weight; Entries = $classes[$name] })
    }
    $priority.Sort([Comparison[object]]{
        param($a, $b)
        $order = $b.Weight.CompareTo($a.Weight)
        if ($order -ne 0) { return $order }
        return [StringComparer]::Ordinal.Compare($a.Name, $b.Name)
    })
    $bins = @(for ($i = 0; $i -lt $ShardCount; $i++) {
        [pscustomobject]@{ ShardIndex = $i; Classes = [Collections.Generic.List[string]]::new(); Weight = 0.0; Entries = 0 }
    })
    foreach ($item in $priority) {
        $index = 0
        for ($i = 1; $i -lt $ShardCount; $i++) {
            if ($bins[$i].Weight -lt $bins[$index].Weight) { $index = $i }
        }
        $bins[$index].Classes.Add($item.Name)
        $bins[$index].Weight += $item.Weight
        $bins[$index].Entries += $item.Entries
    }
    $assigned = @($bins | ForEach-Object { $_.Classes.ToArray() })
    Assert-SameTestInventory @($classes.Keys) $assigned
    [ordered]@{
        Algorithm = 'class-duration-lpt-v1'; InventoryDigest = Get-InventoryDigest $Entries
        DiscoveredEntries = $Entries.Count; Classes = @($classes.Keys | Sort-Object)
        ShardCount = $ShardCount; Shards = $bins; ExactOnce = $true
    }
}
