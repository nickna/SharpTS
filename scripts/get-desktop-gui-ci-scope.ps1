[CmdletBinding()]
param(
    [string[]]$ChangedPath = @(),
    [ValidateSet('auto', 'all', 'windows', 'macos')]
    [string]$Target = 'auto'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Target -ne 'auto') {
    return [pscustomobject]@{
        Windows = $Target -in @('all', 'windows')
        MacOS = $Target -in @('all', 'macos')
    }
}

$commonPatterns = @(
    'Execution/*',
    'Hosting/*',
    'SharpTS.Hosting.Abstractions/*',
    'Parsing/*',
    'TypeSystem/*',
    'Runtime/*',
    'Compilation/*',
    'Cli/*',
    'References/*',
    'SharpTS.Tests/Hosting/*',
    'SharpTS.Tests/CliTests/*',
    'SharpTS.Gui/*',
    'SharpTS.Gui.Host/*',
    'SharpTS.Gui.Conformance.Tests/*',
    'SharpTS.Gui.Sdk.Consumer/*',
    'SharpTS.Gui.Sdk/*',
    'SharpTS.Sdk.Tasks/*',
    'Program.cs',
    'SharpTS.csproj',
    'SharpTS.sln',
    'Directory.Build.props',
    'eng/GuiVersion.props',
    'global.json',
    'scripts/get-gui-version.ps1',
    'scripts/sync-gui-version.ps1',
    'scripts/get-desktop-gui-ci-scope.ps1',
    '.github/workflows/desktop-gui.yml'
)
$windowsPatterns = @(
    'distribution/windows/*',
    'scripts/package-gui-windows.ps1',
    'scripts/generate-gui-windows-assets.ps1',
    'scripts/collect-gui-support-bundle.ps1',
    'scripts/test-gui-distribution.ps1',
    '.github/workflows/windows-gui-distribution.yml',
    '.github/workflows/reusable-windows-gui-distribution.yml'
)
$macOSPatterns = @(
    'distribution/macos/*',
    'scripts/package-gui-macos.ps1',
    'scripts/test-gui-macos-package.ps1',
    '.github/workflows/macos-gui-distribution.yml',
    '.github/workflows/reusable-macos-gui-distribution.yml'
)

function Test-MatchesAnyPattern([string]$Path, [string[]]$Patterns) {
    $normalized = $Path.Replace('\', '/')
    return @($Patterns | Where-Object { $normalized -like $_ }).Count -gt 0
}

$runWindows = $false
$runMacOS = $false
foreach ($path in $ChangedPath) {
    if (Test-MatchesAnyPattern $path $commonPatterns) {
        $runWindows = $true
        $runMacOS = $true
        continue
    }
    if (Test-MatchesAnyPattern $path $windowsPatterns) { $runWindows = $true }
    if (Test-MatchesAnyPattern $path $macOSPatterns) { $runMacOS = $true }
}

[pscustomobject]@{ Windows = $runWindows; MacOS = $runMacOS }
