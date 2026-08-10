[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$propsPath = Join-Path $repositoryRoot 'eng\GuiPreviewVersion.props'
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$propertyGroup = $props.Project.PropertyGroup
$version = [string]$propertyGroup.SharpTSGuiSdkVersion
$marketingVersion = [string]$propertyGroup.SharpTSGuiMarketingVersion

if ([string]::IsNullOrWhiteSpace($version) -or
    $version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
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
