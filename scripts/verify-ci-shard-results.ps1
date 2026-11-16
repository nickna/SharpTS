param([Parameter(Mandatory)][string]$ResultsDirectory,
    [ValidateSet('Full', 'Pr')][string]$Coverage = 'Pr')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-inventory.ps1')
foreach ($os in @('ubuntu-24.04', 'windows-2025')) {
    $partitionFiles = @(Get-ChildItem -LiteralPath (Join-Path $ResultsDirectory "core-partitions-$os") -Recurse -Filter core-partitions.json)
    if ($partitionFiles.Count -ne 1) { throw "Expected the independent core/process discovery proof for $os." }
    $partition = Get-Content $partitionFiles[0].FullName -Raw | ConvertFrom-Json
    if (-not $partition.ExactOnce -or $partition.BaseEntries -ne $partition.InProcessEntries + $partition.ProcessEntries) {
        throw "Invalid core/process coverage proof for $os."
    }
    foreach ($kind in @('core', 'standalone')) {
        $count = if ($kind -eq 'core') { 4 } else { 8 }
        $allSelected = [Collections.Generic.List[string]]::new()
        $total = 0
        $reference = $null
        for ($i = 0; $i -lt $count; $i++) {
            $directory = Join-Path $ResultsDirectory "$kind-shard-$os-$i"
            $files = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter shard-proof.json)
            if ($files.Count -ne 1) { throw "Expected one shard proof in $directory" }
            $proof = Get-Content $files[0].FullName -Raw | ConvertFrom-Json
            if ($proof.Kind -ne $kind -or $proof.ShardIndex -ne $i -or $proof.ShardCount -ne $count -or
                -not $proof.ExactOnce -or $proof.SelectedEntries -le 0) { throw "Invalid shard proof: $directory" }
            if ($kind -eq 'standalone' -and $proof.Coverage -ne $Coverage) { throw 'Wrong standalone cadence.' }
            if ($null -eq $reference) { $reference = $proof }
            if ($proof.InventoryDigest -cne $reference.InventoryDigest -or $proof.DiscoveredEntries -ne $reference.DiscoveredEntries) {
                throw "Shards discovered different inventories: $kind $os"
            }
            Assert-SameTestInventory $reference.AllUnits $proof.AllUnits
            $allSelected.AddRange([string[]]$proof.SelectedUnits)
            $total += $proof.SelectedEntries
            $trx = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter '*.trx')
            if ($trx.Count -ne 1) { throw "Expected one execution result in $directory" }
            Assert-TestResults $trx[0].FullName $proof.SelectedEntries $proof.SelectedUnits $kind
        }
        Assert-SameTestInventory $reference.AllUnits $allSelected.ToArray()
        if ($total -ne $reference.DiscoveredEntries) { throw "Shard counts do not cover $kind $os." }
        if ($kind -eq 'core' -and ($total -ne $partition.InProcessEntries -or
            $reference.InventoryDigest -cne $partition.InProcessInventoryDigest)) {
            throw "Core shards do not cover the independent core/process partition for $os."
        }
        Write-Host "$kind ${os}: $total discovery entries assigned exactly once and executed across $count shards."
    }
}
