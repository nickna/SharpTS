[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string[]]$Path
)

$ErrorActionPreference = 'Stop'
$harnessDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
Import-Module (Join-Path $harnessDirectory 'Snapshot.psm1') -Force

foreach ($snapshotPath in $Path) {
    [void](Test-SharpTSPublicBenchmarkSnapshotFile $snapshotPath)
    Write-Host "Validated benchmark snapshot: $snapshotPath"
}
