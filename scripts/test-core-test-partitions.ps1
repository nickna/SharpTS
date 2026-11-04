[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'core-test-partitions.ps1')
$a = 'SharpTS.Tests.SharedTests.Core.Case'
$b = 'SharpTS.Tests.IntegrationTests.Cli.Case(value: 1)'
$c = 'SharpTS.Tests.PackagingTests.Package.Case'
$proof = New-CoreTestPartitionProof -BaseEntries @($a, $b, $b, $c) -InProcessEntries @($a) -ProcessEntries @($b, $b, $c)
if ($proof.BaseEntries -ne 4 -or $proof.InProcessEntries -ne 1 -or $proof.ProcessEntries -ne 3 -or -not $proof.ExactOnce) {
    throw 'Core partition proof did not retain duplicate discovery entries.'
}
foreach ($invalid in @(
    @{ BaseEntries = @($a, $b); InProcessEntries = @($a); ProcessEntries = @($a, $b) },
    @{ BaseEntries = @($a, $b, $b); InProcessEntries = @($a); ProcessEntries = @($b) },
    @{ BaseEntries = @($a, $b); InProcessEntries = @($a); ProcessEntries = @($b, $c) },
    @{ BaseEntries = @($a, $b); InProcessEntries = @(); ProcessEntries = @($b) },
    @{ BaseEntries = @($a, 'unexpected'); InProcessEntries = @($a); ProcessEntries = @('unexpected') }
)) {
    $rejected = $false
    try { $null = New-CoreTestPartitionProof @invalid } catch { $rejected = $true }
    if (-not $rejected) { throw 'An incomplete, overlapping, empty or unknown partition was accepted.' }
}
Write-Host 'Core partition proof regressions passed.'
