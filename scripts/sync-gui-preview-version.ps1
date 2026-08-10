[CmdletBinding()]
param([switch]$Check)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$versionInfo = & (Join-Path $PSScriptRoot 'get-gui-preview-version.ps1')
$version = $versionInfo.Version
$errors = [System.Collections.Generic.List[string]]::new()

$projections = @(
    @{ Path = 'Cli/GuiPreviewVersion.g.cs'; Pattern = 'internal const string Value = "[^"]+";'; Replacement = "internal const string Value = `"$version`";" },
    @{ Path = 'SharpTS.Gui.Sdk.Consumer/SharpTS.Gui.Sdk.Consumer.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'SharpTS.Gui.Sdk/Templates/sharpts-gui/SharpTSGuiApp.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'SharpTS.Gui.Sdk/GuiPackage/package.json'; Pattern = '"version"\s*:\s*"[^"]+"'; Replacement = "`"version`": `"$version`"" },
    @{ Path = 'Examples/Calculator/Calculator.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'Examples/Calculator/run-local.ps1'; Pattern = '\$sdkVersion\s*=\s*''[^'']+'''; Replacement = "`$sdkVersion = '$version'" },
    @{ Path = 'README.md'; Pattern = 'SharpTS\.Gui\.Sdk::\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?'; Replacement = "SharpTS.Gui.Sdk::$version" },
    @{ Path = 'SharpTS.Gui.Sdk/readme.md'; Pattern = '0\.\d+\.\d+-preview\.\d+'; Replacement = $version },
    @{ Path = 'Examples/Calculator/README.md'; Pattern = '0\.\d+\.\d+-preview\.\d+'; Replacement = $version },
    @{ Path = 'docs/gui/sdk-development.md'; Pattern = 'SharpTS\.Gui\.Sdk::\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?'; Replacement = "SharpTS.Gui.Sdk::$version" }
)

foreach ($projection in $projections) {
    $path = Join-Path $repositoryRoot $projection.Path
    $content = Get-Content -LiteralPath $path -Raw
    $updated = [regex]::Replace($content, $projection.Pattern, $projection.Replacement)
    if ($updated -eq $content -and $content -notmatch [regex]::Escape($version)) {
        $errors.Add("$($projection.Path) does not expose GUI preview version $version and its projection pattern did not match.")
        continue
    }
    if ($Check) {
        if ($updated -ne $content) { $errors.Add("$($projection.Path) is not synchronized to GUI preview version $version.") }
    }
    elseif ($updated -ne $content) {
        [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false))
        Write-Host "Updated $($projection.Path)"
    }
}

$evidenceFiles = @('docs/gui/desktop-status.md', 'docs/gui/macos-distribution.md')
foreach ($relativePath in $evidenceFiles) {
    $content = Get-Content -LiteralPath (Join-Path $repositoryRoot $relativePath) -Raw
    if ($content -notmatch [regex]::Escape($version)) {
        $errors.Add("$relativePath must be updated manually with evidence for GUI preview version $version.")
    }
}

if ($errors.Count -gt 0) {
    throw "GUI preview version synchronization failed:`n - $($errors -join "`n - ")"
}

if ($Check) { Write-Host "GUI preview version projections match $version." }
