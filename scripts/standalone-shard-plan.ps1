function New-StandaloneShardPlan {
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyCollection()][string[]]$MethodEntries,
        [Parameter(Mandatory)][ValidateRange(1, 32)][int]$ShardCount,
        [Parameter(Mandatory)][AllowEmptyString()][string]$BaseFilter,
        [switch]$ExpandToFit
    )

    Set-StrictMode -Version Latest
    if ([string]::IsNullOrWhiteSpace($BaseFilter)) {
        throw 'The standalone base filter must not be empty.'
    }

    $weights = [System.Collections.Generic.Dictionary[string, long]]::new([StringComparer]::Ordinal)
    foreach ($method in $MethodEntries) {
        if ($null -eq $method -or $method -cnotmatch '\ASharpTS\.Tests\.CompilerTests\.StandaloneDllTests\.\w+\z') {
            throw "Invalid standalone test method identity: $method"
        }
        if ($weights.ContainsKey($method)) { $weights[$method]++ }
        else { $weights.Add($method, 1L) }
    }
    if ($weights.Count -eq 0) {
        throw 'No standalone test methods were discovered.'
    }
    if ($weights.Count -lt $ShardCount) {
        throw 'There are fewer standalone methods than shards; refusing empty shards.'
    }

    [string[]]$methods = @($weights.Keys)
    [Array]::Sort($methods, [StringComparer]::Ordinal)
    $methodWeights = [System.Collections.Generic.List[object]]::new()
    $canonical = [Text.StringBuilder]::new()
    [long]$totalWeight = 0
    [long]$heaviest = 0
    foreach ($method in $methods) {
        $weight = $weights[$method]
        $methodWeights.Add([pscustomobject][ordered]@{
            Method = $method
            DiscoveredEntries = $weight
            Weight = $weight
        })
        [void]$canonical.Append($method).Append("`t").Append($weight.ToString([Globalization.CultureInfo]::InvariantCulture)).Append("`n")
        $totalWeight += $weight
        $heaviest = [Math]::Max($heaviest, $weight)
    }
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical.ToString()))).ToLowerInvariant()

    # A discovery entry is a cost proxy, not a duration. Keep opaque theories
    # and every expanded row together by assigning only whole method names.
    $priority = [System.Collections.Generic.List[object]]::new($methodWeights)
    $priority.Sort([Comparison[object]]{
        param($left, $right)
        $byWeight = $right.Weight.CompareTo($left.Weight)
        if ($byWeight -ne 0) { return $byWeight }
        return [StringComparer]::Ordinal.Compare($left.Method, $right.Method)
    })
    $bins = @(
        for ($index = 0; $index -lt $ShardCount; $index++) {
            [pscustomobject]@{
                Names = [System.Collections.Generic.List[string]]::new()
                Weight = 0L
            }
        }
    )
    foreach ($entry in $priority) {
        $lightest = 0
        for ($index = 1; $index -lt $ShardCount; $index++) {
            if ($bins[$index].Weight -lt $bins[$lightest].Weight) { $lightest = $index }
        }
        $bins[$lightest].Names.Add($entry.Method)
        $bins[$lightest].Weight += $entry.Weight
    }

    $assigned = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $shards = [System.Collections.Generic.List[object]]::new()
    [long]$assignedWeight = 0
    for ($index = 0; $index -lt $ShardCount; $index++) {
        [string[]]$selected = $bins[$index].Names.ToArray()
        [Array]::Sort($selected, [StringComparer]::Ordinal)
        if ($selected.Count -eq 0) { throw "Standalone shard $index is empty." }
        [long]$selectedWeight = 0
        foreach ($method in $selected) {
            if (-not $assigned.Add($method)) { throw "Standalone method was assigned more than once: $method" }
            $selectedWeight += $weights[$method]
        }
        if ($selectedWeight -ne $bins[$index].Weight) { throw 'Standalone shard weight does not match its methods.' }
        $methodFilter = ($selected | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
        $filter = "$BaseFilter&($methodFilter)"
        if ($filter.Length -gt 24000) {
            if ($ExpandToFit -and $ShardCount -lt [Math]::Min(32, $methods.Count)) {
                return New-StandaloneShardPlan -MethodEntries $MethodEntries -ShardCount ($ShardCount + 1) -BaseFilter $BaseFilter -ExpandToFit
            }
            throw 'The standalone shard filter is too long for a portable command line.'
        }
        $shards.Add([pscustomobject][ordered]@{
            ShardIndex = $index
            SelectedMethods = $selected
            SelectedDiscoveryEntries = $selectedWeight
            EstimatedWeight = $selectedWeight
            Filter = $filter
        })
        $assignedWeight += $selectedWeight
    }
    if ($assigned.Count -ne $methods.Count -or $assignedWeight -ne $totalWeight -or $totalWeight -ne $MethodEntries.Count) {
        throw 'Standalone shard planning did not preserve the complete discovery inventory.'
    }

    [pscustomobject][ordered]@{
        Algorithm = 'method-row-lpt-v1'
        WeightUnit = 'discovered-entry'
        ShardCount = $ShardCount
        BaseFilter = $BaseFilter
        DiscoveredEntries = $totalWeight
        Methods = $methods
        MethodWeights = $methodWeights.ToArray()
        Shards = $shards.ToArray()
        InventoryDigest = $digest
        HeaviestMethodWeight = $heaviest
        LowerBound = [Math]::Max([long][Math]::Ceiling($totalWeight / [double]$ShardCount), $heaviest)
    }
}
