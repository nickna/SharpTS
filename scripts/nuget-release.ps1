[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('ValidateArtifacts', 'Preflight', 'Publish')]
    [string] $Action,

    [Parameter(Mandatory)]
    [string] $Version,

    [string] $PackageDirectory = './nupkg',
    [string] $ManifestPath,
    [string] $NuGetSource = 'https://api.nuget.org/v3/index.json',
    [string] $FlatContainerBaseUri = 'https://api.nuget.org/v3-flatcontainer'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repositoryRoot '.github/nuget-packages.json'
}

Import-Module (Join-Path $PSScriptRoot 'NuGetRelease.psm1') -Force
$manifest = Get-NuGetReleaseManifest -Path $ManifestPath

Invoke-NuGetRelease `
    -Action $Action `
    -Manifest $manifest `
    -PackageDirectory $PackageDirectory `
    -Version $Version `
    -RepositoryRoot $repositoryRoot `
    -FlatContainerBaseUri $FlatContainerBaseUri `
    -ApiKey $env:NUGET_API_KEY `
    -NuGetSource $NuGetSource
