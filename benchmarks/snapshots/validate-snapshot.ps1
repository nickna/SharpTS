[CmdletBinding()]
param([Parameter(Mandatory, Position = 0)] [string[]]$Path)

$ErrorActionPreference = 'Stop'
$snapshotDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
Import-Module (Join-Path $snapshotDirectory 'PublicSnapshot.psm1') -Force
foreach ($snapshotPath in $Path) {
    [void](Test-SharpTSPublicPerformanceSnapshotFile $snapshotPath)
    Write-Host "Validated public performance snapshot: $snapshotPath"
}
