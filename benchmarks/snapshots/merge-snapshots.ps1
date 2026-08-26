[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]]$Path,
    [Parameter(Mandatory)] [string]$OutputFile,
    [string]$GeneratedAtUtc,
    [switch]$RequireAllSuites
)

$ErrorActionPreference = 'Stop'
$snapshotDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
Import-Module (Join-Path $snapshotDirectory 'PublicSnapshot.psm1') -Force

$runs = [Collections.Generic.List[object]]::new()
foreach ($inputPath in $Path) {
    [void](Test-SharpTSPublicPerformanceSnapshotFile $inputPath)
    $snapshot = Get-Content -LiteralPath $inputPath -Raw | ConvertFrom-Json
    foreach ($run in @($snapshot.runs)) { $runs.Add($run) }
}
if ($RequireAllSuites) {
    $present = @($runs | ForEach-Object suite | Sort-Object -Unique)
    $missing = @('cross-runtime', 'compiler-micro', 'gui') | Where-Object { $_ -notin $present }
    if ($missing.Count -gt 0) { throw "Merged snapshot is missing required suite(s): $($missing -join ', ')." }
}
$arguments = @{ Runs = $runs.ToArray(); OutputFile = $OutputFile }
if ($GeneratedAtUtc) { $arguments.GeneratedAtUtc = $GeneratedAtUtc }
[void](Export-SharpTSPublicPerformanceSnapshot @arguments)
Write-Host "Merged $($runs.Count) performance runs into $([IO.Path]::GetFullPath($OutputFile))"
