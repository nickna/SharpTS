[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$importOutput = @(. (Join-Path $PSScriptRoot 'standalone-shard-plan.ps1'))
if ($importOutput.Count -ne 0) { throw 'Sourcing the shard planner must not emit success output.' }

$methodPrefix = 'SharpTS.Tests.CompilerTests.StandaloneDllTests.'
$externalFilter = 'FullyQualifiedName~SharpTS.Tests.CompilerTests.StandaloneDllTests.'
$hermeticFilter = 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm&' + $externalFilter

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message Expected '$Expected', got '$Actual'." }
}

function Assert-Sequence([string[]]$Expected, [string[]]$Actual, [string]$Message) {
    Assert-Equal $Expected.Count $Actual.Count "$Message Count differs."
    for ($index = 0; $index -lt $Expected.Count; $index++) {
        Assert-True ([StringComparer]::Ordinal.Equals($Expected[$index], $Actual[$index])) "$Message Entry $index differs."
    }
}

function Assert-Throws([scriptblock]$Action, [string]$Message, [string]$Pattern) {
    try { & $Action | Out-Null }
    catch {
        Assert-True ($_.Exception.Message -match $Pattern) "$Message Unexpected error: $($_.Exception.Message)"
        return
    }
    throw "$Message Expected rejection."
}

function New-MethodEntries([Collections.IDictionary]$Counts) {
    $entries = [Collections.Generic.List[string]]::new()
    foreach ($name in $Counts.Keys) {
        for ($row = 0; $row -lt $Counts[$name]; $row++) { $entries.Add($methodPrefix + $name) }
    }
    return ,$entries.ToArray()
}

function Test-PlanIntegrity($Plan, [string[]]$Entries, [int]$Count, [string]$Filter) {
    foreach ($property in @('Algorithm', 'WeightUnit', 'ShardCount', 'BaseFilter', 'DiscoveredEntries',
        'Methods', 'MethodWeights', 'Shards', 'InventoryDigest', 'HeaviestMethodWeight', 'LowerBound')) {
        Assert-True ($null -ne $Plan.PSObject.Properties[$property]) "Plan is missing $property."
    }
    Assert-Equal 'method-row-lpt-v1' $Plan.Algorithm 'Algorithm identity.'
    Assert-Equal 'discovered-entry' $Plan.WeightUnit 'Weight unit must not claim measured durations.'
    Assert-Equal $Count $Plan.ShardCount 'Shard count.'
    Assert-Equal $Filter $Plan.BaseFilter 'Base filter.'
    Assert-Equal $Entries.Count $Plan.DiscoveredEntries 'Discovery-entry conservation.'

    $counts = [Collections.Generic.Dictionary[string, long]]::new([StringComparer]::Ordinal)
    foreach ($entry in $Entries) {
        if ($counts.ContainsKey($entry)) { $counts[$entry]++ } else { $counts.Add($entry, 1L) }
    }
    [string[]]$expectedMethods = @($counts.Keys)
    [Array]::Sort($expectedMethods, [StringComparer]::Ordinal)
    Assert-Sequence $expectedMethods $Plan.Methods 'Complete ordinal method catalog.'
    Assert-Equal $counts.Count @($Plan.MethodWeights).Count 'Method weight record count.'
    $weighted = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in $Plan.MethodWeights) {
        Assert-True ($counts.ContainsKey($record.Method)) 'Weight record must belong to the input inventory.'
        Assert-True ($weighted.Add($record.Method)) 'A method must have exactly one weight record.'
        Assert-Equal $counts[$record.Method] $record.DiscoveredEntries 'Per-method discovery entries.'
        Assert-Equal $counts[$record.Method] $record.Weight 'Each discovered entry contributes positive unit weight.'
    }

    Assert-Equal $Count @($Plan.Shards).Count 'All shards must be planned together.'
    $assigned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [long]$total = 0
    for ($index = 0; $index -lt $Count; $index++) {
        $shard = $Plan.Shards[$index]
        Assert-Equal $index $shard.ShardIndex 'Shard index order.'
        Assert-True (@($shard.SelectedMethods).Count -gt 0) 'No shard may be empty.'
        [string[]]$sorted = @($shard.SelectedMethods)
        [Array]::Sort($sorted, [StringComparer]::Ordinal)
        Assert-Sequence $sorted $shard.SelectedMethods 'Selected methods must be ordinally sorted.'
        [long]$weight = 0
        foreach ($method in $shard.SelectedMethods) {
            Assert-True ($counts.ContainsKey($method)) 'A shard may not invent a method.'
            Assert-True ($assigned.Add($method)) "Method assigned to multiple shards: $method"
            $weight += $counts[$method]
        }
        Assert-Equal $weight $shard.SelectedDiscoveryEntries 'Selected row weight.'
        Assert-Equal $weight $shard.EstimatedWeight 'Estimated weight is the sum of selected entry counts.'
        $total += $weight

        $prefix = $Filter + '&('
        if (-not $shard.Filter.StartsWith($prefix, [StringComparison]::Ordinal)) { $prefix = '(' + $Filter + ')&(' }
        Assert-True ($shard.Filter.StartsWith($prefix, [StringComparison]::Ordinal) -and
            $shard.Filter.EndsWith(')', [StringComparison]::Ordinal)) 'The base must constrain the whole method OR group.'
        $terms = $shard.Filter.Substring($prefix.Length, $shard.Filter.Length - $prefix.Length - 1).Split('|')
        [string[]]$expectedTerms = @($shard.SelectedMethods | ForEach-Object { 'FullyQualifiedName=' + $_ })
        Assert-Sequence $expectedTerms $terms 'Selectors must equal whole method FQNs, never display-name/prefix matches.'
        Assert-True ($shard.Filter.Length -le 24000) 'Every planned filter must fit the portable command line bound.'
    }
    [string[]]$owned = @($assigned)
    [Array]::Sort($owned, [StringComparer]::Ordinal)
    Assert-Sequence $expectedMethods $owned 'Every method must be owned.'
    Assert-Equal $Entries.Count $total 'Sum of shard entry weights.'

    $canonical = [Text.StringBuilder]::new()
    [long]$heaviest = 0
    foreach ($method in $expectedMethods) {
        [void]$canonical.Append($method).Append("`t").Append($counts[$method].ToString([Globalization.CultureInfo]::InvariantCulture)).Append("`n")
        $heaviest = [Math]::Max($heaviest, $counts[$method])
    }
    $expectedDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical.ToString())))
    Assert-True ([StringComparer]::OrdinalIgnoreCase.Equals($expectedDigest, $Plan.InventoryDigest)) 'Digest must use ordinal names, invariant counts, UTF8 and explicit LF.'
    Assert-Equal $heaviest $Plan.HeaviestMethodWeight 'Heaviest method weight.'
    Assert-Equal ([Math]::Max([long][Math]::Ceiling($Entries.Count / [double]$Count), $heaviest)) $Plan.LowerBound 'Indivisible-method/average lower bound.'
}

function New-CheckedPlan([string[]]$Entries, [int]$Count, [string]$Filter = $hermeticFilter) {
    $output = @(New-StandaloneShardPlan -MethodEntries $Entries -ShardCount $Count -BaseFilter $Filter)
    Assert-Equal 1 $output.Count 'Planner must emit one result object, without incidental success output.'
    Assert-True ($output[0] -is [pscustomobject]) 'Planner result must be a PSCustomObject.'
    Test-PlanIntegrity $output[0] $Entries $Count $Filter
    return $output[0]
}

# 1. Unequal weights, ordinal method ties and lowest-index shard ties.
$entries = New-MethodEntries ([ordered]@{ A = 5; B = 5; C = 3; D = 3; E = 2; F = 2 })
$plan = New-CheckedPlan $entries 3
Assert-Sequence @(($methodPrefix + 'A'), ($methodPrefix + 'E')) $plan.Shards[0].SelectedMethods 'Known shard0 placement.'
Assert-Sequence @(($methodPrefix + 'B'), ($methodPrefix + 'F')) $plan.Shards[1].SelectedMethods 'Known shard1 placement.'
Assert-Sequence @(($methodPrefix + 'C'), ($methodPrefix + 'D')) $plan.Shards[2].SelectedMethods 'Known shard2 placement.'
Assert-Sequence @('7', '7', '6') @($plan.Shards | ForEach-Object { [string]$_.EstimatedWeight }) 'Known balanced loads.'
$shuffled = [string[]]$entries.Clone()
$random = [Random]::new(2158)
for ($index = $shuffled.Length - 1; $index -gt 0; $index--) {
    $other = $random.Next($index + 1)
    $saved = $shuffled[$index]; $shuffled[$index] = $shuffled[$other]; $shuffled[$other] = $saved
}
$reordered = New-CheckedPlan $shuffled 3
Assert-Equal ($plan | ConvertTo-Json -Depth 8 -Compress) ($reordered | ConvertTo-Json -Depth 8 -Compress) 'Shuffled discovery must not change any plan metadata.'

# 2. Every entry/method is covered for one and N shards; similar names stay distinct.
$single = New-CheckedPlan $entries 1
$perMethod = New-CheckedPlan $entries 6
Assert-Equal $single.InventoryDigest $perMethod.InventoryDigest 'Inventory digest does not depend on shard count.'
$similar = New-CheckedPlan (New-MethodEntries ([ordered]@{ A = 2; AB = 1; A_1 = 1 })) 2
Assert-Equal 3 $similar.Methods.Count 'Similar-prefix methods must remain separate equality terms.'

# 3. Ordinal identity/digest must survive cultures that sort I/i differently.
$ordinalCounts = [Collections.Generic.Dictionary[string, long]]::new([StringComparer]::Ordinal)
foreach ($name in @('A', 'a', 'I', 'i', 'İ', 'ı')) { $ordinalCounts.Add($name, 1L) }
$ordinalEntries = New-MethodEntries $ordinalCounts
$originalCulture = [Globalization.CultureInfo]::CurrentCulture
$originalUICulture = [Globalization.CultureInfo]::CurrentUICulture
try {
    $culturePlanJson = $null
    foreach ($culture in @('en-US', 'tr-TR')) {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
        [Globalization.CultureInfo]::CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
        $ordinalPlan = New-CheckedPlan $ordinalEntries 3
        Assert-Sequence @('A', 'I', 'a', 'i', 'İ', 'ı' | ForEach-Object { $methodPrefix + $_ }) $ordinalPlan.Methods 'Ordinal casing/Unicode order.'
        $json = $ordinalPlan | ConvertTo-Json -Depth 8 -Compress
        if ($null -eq $culturePlanJson) { $culturePlanJson = $json }
        else { Assert-Equal $culturePlanJson $json 'Culture must not change assignments, filters or digest.' }
        [Array]::Reverse($ordinalEntries)
    }
} finally {
    [Globalization.CultureInfo]::CurrentCulture = $originalCulture
    [Globalization.CultureInfo]::CurrentUICulture = $originalUICulture
}

# 4. A huge method remains whole and determines the attainable lower bound.
$huge = New-CheckedPlan (New-MethodEntries ([ordered]@{ Huge = 50; B = 1; C = 1; D = 1 })) 3
Assert-Equal 50 $huge.LowerBound 'A huge indivisible method limits the best possible load.'
Assert-Equal 50 $huge.Shards[0].EstimatedWeight 'Huge method must not be split, capped or duplicated.'
Assert-Sequence @($methodPrefix + 'Huge') $huge.Shards[0].SelectedMethods 'Huge method ownership.'

# 5. Fixed names that collide under the former SHA256-first-byte modulo3 rule.
$skewed = New-CheckedPlan (New-MethodEntries ([ordered]@{
    PlannerHeavy00 = 60; PlannerHeavy01 = 50; PlannerHeavy04 = 40
    PlannerLightA = 1; PlannerLightB = 1; PlannerLightC = 1
})) 3
$oldLoads = [long[]]@(0, 0, 0)
foreach ($record in $skewed.MethodWeights) {
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($record.Method))
    $oldLoads[$hash[0] % 3] += $record.Weight
}
Assert-True ($oldLoads[1] -ge 150) 'Fixed old-hash fixture must preserve its heavy-method collision.'
$newMaximum = [long]($skewed.Shards | Measure-Object -Property EstimatedWeight -Maximum).Maximum
$oldMaximum = [long]($oldLoads | Measure-Object -Maximum).Maximum
Assert-True ($newMaximum -lt $oldMaximum) 'Weighted placement must reduce the maximum predicted load for this skewed fixture.'

# 6. New discovery rows/methods add work without silently disappearing.
$grownEntries = [string[]]@($entries + @($methodPrefix + 'A') * 4 + @($methodPrefix + 'NewMethod'))
$grown = New-CheckedPlan $grownEntries 3
Assert-Equal 25 $grown.DiscoveredEntries 'New entries increase total weight.'
Assert-True (-not [StringComparer]::Ordinal.Equals($plan.InventoryDigest, $grown.InventoryDigest)) 'Inventory growth must change its digest.'
Assert-Equal 7 $grown.Methods.Count 'New methods must join the complete catalog.'

# 7. Category exclusions scope the full OR group; boundary is exactly24000 chars.
$external = New-CheckedPlan $entries 3 $externalFilter
Assert-Equal $plan.InventoryDigest $external.InventoryDigest 'Filter policy must not change inventory identity.'
$short = New-CheckedPlan @($methodPrefix + 'A') 1 $externalFilter
$suffixLength = 24000 - $short.Shards[0].Filter.Length + 1
$boundaryMethod = $methodPrefix + ('x' * $suffixLength)
$boundary = New-CheckedPlan @($boundaryMethod) 1 $externalFilter
Assert-Equal 24000 $boundary.Shards[0].Filter.Length 'Exact portable filter boundary.'
Assert-Throws { New-StandaloneShardPlan -MethodEntries @($boundaryMethod + 'x') -ShardCount 1 -BaseFilter $externalFilter } 'Oversized filter.' '(?i)(filter|command line)'
Assert-Throws { New-StandaloneShardPlan -MethodEntries @(($methodPrefix + 'A'), ($boundaryMethod + 'x')) -ShardCount 2 -BaseFilter $externalFilter } 'Every bin must fit, not just a requested shard.' '(?i)(filter|command line)'

# 8. Invalid catalogs/counts fail closed, without discovery or dotnet processes.
Assert-Throws { New-StandaloneShardPlan -MethodEntries @() -ShardCount 1 -BaseFilter $hermeticFilter } 'Empty inventory.' '(?i)(no.*methods|empty|entries)'
Assert-Throws { New-StandaloneShardPlan -MethodEntries $null -ShardCount 1 -BaseFilter $hermeticFilter } 'Null inventory.' '(?i)(no.*methods|empty|entries)'
foreach ($bad in @('', ' ', $methodPrefix, ($methodPrefix + 'A(row)'), ($methodPrefix + "A`nB"), 'Other.Tests.Method')) {
    Assert-Throws { New-StandaloneShardPlan -MethodEntries @($bad) -ShardCount 1 -BaseFilter $hermeticFilter } "Malformed method '$bad'." '(?i)(method|identity|entry)'
}
foreach ($invalidCount in @(0, 33)) {
    Assert-Throws { New-StandaloneShardPlan -MethodEntries $entries -ShardCount $invalidCount -BaseFilter $hermeticFilter } 'Invalid shard count.' '(?i)(ShardCount|range|count)'
}
Assert-Throws { New-StandaloneShardPlan -MethodEntries @(($methodPrefix + 'A'), ($methodPrefix + 'A'), ($methodPrefix + 'B')) -ShardCount 3 -BaseFilter $hermeticFilter } 'Duplicate rows cannot fill empty shards.' '(?i)(fewer|empty|methods|shards)'
Assert-Throws { New-StandaloneShardPlan -MethodEntries $entries -ShardCount 3 -BaseFilter '' } 'Empty base filter.' '(?i)(filter|empty)'

# 9. Default local plans grow deterministically without dropping long identities.
$largeEntries = [string[]]@(0..699 | ForEach-Object { $methodPrefix + ('LongMethodName' * 6) + $_ })
Assert-Throws { New-StandaloneShardPlan -MethodEntries $largeEntries -ShardCount 3 -BaseFilter $hermeticFilter } 'Fixed shard count still fails closed.' '(?i)(filter|command line)'
$fitting = New-StandaloneShardPlan -MethodEntries $largeEntries -ShardCount 3 -BaseFilter $hermeticFilter -ExpandToFit
Assert-True ($fitting.ShardCount -gt 3) 'Automatic mode must add enough shards.'
Test-PlanIntegrity $fitting $largeEntries $fitting.ShardCount $hermeticFilter
$fittingReversed = New-StandaloneShardPlan -MethodEntries ([string[]]$largeEntries[699..0]) -ShardCount 3 -BaseFilter $hermeticFilter -ExpandToFit
Assert-Equal ($fitting | ConvertTo-Json -Depth 8 -Compress) ($fittingReversed | ConvertTo-Json -Depth 8 -Compress) 'Automatic planning is independent of discovery order.'
Assert-Throws { New-StandaloneShardPlan -MethodEntries @($boundaryMethod + 'x') -ShardCount 1 -BaseFilter $externalFilter -ExpandToFit } 'An individually oversized method remains an error.' '(?i)(filter|command line)'

Write-Host 'Standalone shard planner tests passed (9 groups).'
