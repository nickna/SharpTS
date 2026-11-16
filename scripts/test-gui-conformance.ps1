param([ValidateSet('Full', 'Pr')][string]$Coverage = 'Full')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-inventory.ps1')
$project = 'tests/gui-conformance/SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj'
$base = 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm'
$full = @(Get-TestInventory -Project $project -Filter $base)
$pr = @(Get-TestInventory -Project $project -Filter "$base&Cadence!=Daily")
$daily = @(Get-TestInventory -Project $project -Filter "$base&Cadence=Daily")
Assert-SameTestInventory $full @($pr + $daily)
$results = 'artifacts/gui-conformance'
[void][IO.Directory]::CreateDirectory($results)
[ordered]@{ Coverage = $Coverage; Full = $full; Pr = $pr; Deferred = $daily; ExactOnce = $true } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $results 'cadence-proof.json')
$filter = if ($Coverage -eq 'Full') { $base } else { "$base&Cadence!=Daily" }
$count = if ($Coverage -eq 'Full') { $full.Count } else { $pr.Count }
& dotnet test $project --no-build --configuration Release --filter $filter `
    --logger 'trx;LogFileName=gui.trx' --results-directory $results
if ($LASTEXITCODE -ne 0) { throw "GUI tests failed: $LASTEXITCODE" }
Assert-TestResults (Join-Path $results 'gui.trx') $count
