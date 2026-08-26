[CmdletBinding()]
param(
    [string]$ResultsFile = (Join-Path ([System.IO.Path]::GetTempPath()) 'bench-results/results.txt'),
    [string]$OutputFile = (Join-Path ([System.IO.Path]::GetTempPath()) 'bench-results/snapshot.json'),
    [string]$RepositoryRoot,
    [string[]]$SelectedRuntimes = @('interpreter', 'compiled', 'node', 'bun'),
    [string]$TimestampUtc,
    [string]$DotNetVersion,
    [string]$NodeVersion,
    [string]$BunVersion
)

$ErrorActionPreference = 'Stop'
$harnessDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path $harnessDirectory '../..')).Path
}
Import-Module (Join-Path $harnessDirectory 'Snapshot.psm1') -Force

$arguments = @{
    ResultsFile = $ResultsFile
    OutputFile = $OutputFile
    RepositoryRoot = $RepositoryRoot
    SelectedRuntimes = $SelectedRuntimes
}
foreach ($name in @('TimestampUtc', 'DotNetVersion', 'NodeVersion', 'BunVersion')) {
    if ($PSBoundParameters.ContainsKey($name)) { $arguments[$name] = $PSBoundParameters[$name] }
}

[void](Export-SharpTSPublicBenchmarkSnapshot @arguments)
Write-Host "Validated benchmark snapshot schema v1 and wrote $([IO.Path]::GetFullPath($OutputFile))"
