param([ValidateSet('interpreted', 'compiled')][string]$Mode = 'compiled', [switch]$Headless)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactRoot = Join-Path $root 'artifacts\calculator-local'
$feed = Join-Path $root 'artifacts\tsx-api-feed'
$packages = Join-Path $artifactRoot 'packages'
New-Item -ItemType Directory -Force -Path $feed, $packages | Out-Null
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = $packages
dotnet build (Join-Path $root 'SharpTS.Sdk.Tasks\SharpTS.Sdk.Tasks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the GUI SDK tasks.' }
dotnet publish (Join-Path $root 'SharpTS.Gui.Host\SharpTS.Gui.Host.csproj') -c Release --self-contained false
if ($LASTEXITCODE -ne 0) { throw 'Failed to publish the GUI host.' }
dotnet pack (Join-Path $root 'SharpTS.Gui.Sdk\SharpTS.Gui.Sdk.csproj') -c Release -o $feed -p:MinVerVersionOverride=0.1.0-preview.1
if ($LASTEXITCODE -ne 0) { throw 'Failed to pack the GUI SDK.' }
$project = Join-Path $PSScriptRoot 'Calculator.csproj'
dotnet restore $project --force
if ($LASTEXITCODE -ne 0) { throw 'Failed to restore the Calculator.' }
$arguments = @('run', '--project', $project, '--no-restore', '--', '--mode', $Mode)
if ($Headless) {
    $env:SHARPTS_GUI_SMOKE_CLOSE = '1'
    $arguments += '--headless'
}
dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Calculator exited with code $LASTEXITCODE." }
