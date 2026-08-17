[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "sharpts-gui-version-$([guid]::NewGuid().ToString('N'))"
$projectionFiles = @(
    'eng/GuiVersion.props',
    'src/SharpTS/Cli/GuiVersion.g.cs',
    'src/SharpTS.Gui.Sdk/Templates/sharpts-gui/SharpTSGuiApp.csproj',
    'tests/fixtures/SharpTS.Gui.Sdk.Consumer/SharpTS.Gui.Sdk.Consumer.csproj',
    'src/SharpTS.Gui.Sdk/GuiPackage/package.json',
    'src/SharpTS.Gui.Sdk/readme.md',
    'samples/Calculator/Calculator.csproj',
    'samples/Calculator/run-local.ps1'
)

try {
    foreach ($relativePath in $projectionFiles) {
        $destination = Join-Path $fixtureRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $relativePath) -Destination $destination
    }

    $version = '9.8.7-test.4'
    & (Join-Path $PSScriptRoot 'sync-gui-version.ps1') -Version $version -RepositoryRoot $fixtureRoot
    & (Join-Path $PSScriptRoot 'sync-gui-version.ps1') -Check -Version $version -RepositoryRoot $fixtureRoot

    $expectedText = @{
        'eng/GuiVersion.props' = @($version, '<SharpTSGuiMarketingVersion>9.8.7</SharpTSGuiMarketingVersion>')
        'src/SharpTS/Cli/GuiVersion.g.cs' = @("Value = `"$version`"")
        'src/SharpTS.Gui.Sdk/Templates/sharpts-gui/SharpTSGuiApp.csproj' = @("SharpTS.Gui.Sdk/$version")
        'tests/fixtures/SharpTS.Gui.Sdk.Consumer/SharpTS.Gui.Sdk.Consumer.csproj' = @("SharpTS.Gui.Sdk/$version")
        'src/SharpTS.Gui.Sdk/GuiPackage/package.json' = @("`"version`": `"$version`"")
        'src/SharpTS.Gui.Sdk/readme.md' = @("SharpTS.Gui.Sdk::$version")
        'samples/Calculator/Calculator.csproj' = @("SharpTS.Gui.Sdk/$version")
        'samples/Calculator/run-local.ps1' = @("`$sdkVersion = '$version'")
    }
    foreach ($relativePath in $expectedText.Keys) {
        $content = Get-Content -LiteralPath (Join-Path $fixtureRoot $relativePath) -Raw
        foreach ($expected in $expectedText[$relativePath]) {
            if (-not $content.Contains($expected, [StringComparison]::Ordinal)) {
                throw "$relativePath did not stage expected text: $expected"
            }
        }
    }

    foreach ($invalidVersion in @('not-a-version', '01.0.0', '1.0.0-bad..version', '1.0.0-bad.', '1.0.0-01')) {
        try {
            & (Join-Path $PSScriptRoot 'sync-gui-version.ps1') -Version $invalidVersion -RepositoryRoot $fixtureRoot
            throw "Invalid staged version was accepted: $invalidVersion"
        }
        catch {
            if ($_.Exception.Message -notlike "*Invalid staged GUI version*") { throw }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Host 'GUI version staging tests passed.'
