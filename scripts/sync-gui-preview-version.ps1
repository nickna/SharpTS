[CmdletBinding()]
param(
    [switch]$Check,
    [Alias('Version')][string]$StagedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$isReleaseStaging = -not [string]::IsNullOrWhiteSpace($StagedVersion)
$version = if ($isReleaseStaging) { $StagedVersion } else { (& (Join-Path $PSScriptRoot 'get-gui-preview-version.ps1')).Version }
$marketingVersion = $version -replace '-.*$', ''
$errors = [System.Collections.Generic.List[string]]::new()

$projections = @(
    @{ Path = 'eng/GuiPreviewVersion.props'; Pattern = '<SharpTSGuiSdkVersion>[^<]+</SharpTSGuiSdkVersion>'; Replacement = "<SharpTSGuiSdkVersion>$version</SharpTSGuiSdkVersion>" },
    @{ Path = 'eng/GuiPreviewVersion.props'; Pattern = '<SharpTSGuiMarketingVersion>[^<]+</SharpTSGuiMarketingVersion>'; Replacement = "<SharpTSGuiMarketingVersion>$marketingVersion</SharpTSGuiMarketingVersion>" },
    @{ Path = 'Cli/GuiPreviewVersion.g.cs'; Pattern = 'internal const string Value = "[^"]+";'; Replacement = "internal const string Value = `"$version`";" },
    @{ Path = 'SharpTS.Gui.Sdk/Templates/sharpts-gui/SharpTSGuiApp.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'SharpTS.Gui.Sdk.Consumer/SharpTS.Gui.Sdk.Consumer.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'SharpTS.Gui.Sdk/GuiPackage/package.json'; Pattern = '"version"\s*:\s*"[^"]+"'; Replacement = "`"version`": `"$version`"" },
    @{ Path = 'Examples/Calculator/Calculator.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'Examples/Calculator/run-local.ps1'; Pattern = '\$sdkVersion\s*=\s*''[^'']+'''; Replacement = "`$sdkVersion = '$version'" }
)
if ($isReleaseStaging) {
    $projections += @{ Path = 'SharpTS.Gui.Sdk/readme.md'; Pattern = 'SharpTS\.Gui\.Sdk::(?:<version>|[0-9A-Za-z][0-9A-Za-z.-]*)'; Replacement = "SharpTS.Gui.Sdk::$version" }
}

foreach ($projection in $projections) {
    $path = Join-Path $repositoryRoot $projection.Path
    $content = Get-Content -LiteralPath $path -Raw
    $updated = [regex]::Replace($content, $projection.Pattern, $projection.Replacement)
    if ($updated -eq $content -and $content -notmatch [regex]::Escape($version)) {
        $errors.Add("$($projection.Path) does not expose a replaceable GUI preview version.")
        continue
    }
    if ($Check) {
        if ($updated -ne $content) { $errors.Add("$($projection.Path) is not synchronized to GUI preview version $version.") }
    }
    elseif ($updated -ne $content) {
        [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false))
        Write-Host "Updated $($projection.Path) to $version."
    }
}
if ($errors.Count -gt 0) { throw "GUI preview version synchronization failed:`n - $($errors -join "`n - ")" }
if ($Check) { Write-Host "GUI preview version projections are synchronized to $version." }
