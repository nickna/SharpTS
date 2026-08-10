[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$propsPath = Join-Path $repositoryRoot 'eng\GuiVersion.props'
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$propertyGroup = $props.Project.PropertyGroup
$version = [string]$propertyGroup.SharpTSGuiSdkVersion
$marketingVersion = [string]$propertyGroup.SharpTSGuiMarketingVersion

function Test-SemVer([string]$Value) {
    $match = [regex]::Match($Value, '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?$')
    if (-not $match.Success) { return $false }
    foreach ($identifier in $match.Groups[4].Value.Split('.', [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($identifier -notmatch '^[0-9A-Za-z-]+$') { return $false }
        if ($identifier -match '^\d+$' -and $identifier.Length -gt 1 -and $identifier[0] -eq '0') { return $false }
    }
    $prerelease = $match.Groups[4].Value
    return -not ($match.Groups[4].Success -and
        ($prerelease.StartsWith('.', [StringComparison]::Ordinal) -or
         $prerelease.EndsWith('.', [StringComparison]::Ordinal) -or
         $prerelease.Contains('..', [StringComparison]::Ordinal)))
}

if ([string]::IsNullOrWhiteSpace($version) -or
    -not (Test-SemVer $version)) {
    throw "Invalid SharpTSGuiSdkVersion in ${propsPath}: '$version'."
}
if ([string]::IsNullOrWhiteSpace($marketingVersion) -or
    $marketingVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid SharpTSGuiMarketingVersion in ${propsPath}: '$marketingVersion'."
}
if ($version.Split('-', 2)[0] -ne $marketingVersion) {
    throw "SharpTSGuiMarketingVersion '$marketingVersion' does not match '$version'."
}

[pscustomobject]@{
    Version = $version
    MarketingVersion = $marketingVersion
    PackageFileName = "SharpTS.Gui.Sdk.$version.nupkg"
}
