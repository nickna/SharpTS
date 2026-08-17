[CmdletBinding()]
param(
    [switch]$Check,
    [Alias('Version')][string]$StagedVersion,
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
} else {
    (Resolve-Path $RepositoryRoot).Path
}
$isReleaseStaging = -not [string]::IsNullOrWhiteSpace($StagedVersion)
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
if ($isReleaseStaging -and -not (Test-SemVer $StagedVersion)) {
    throw "Invalid staged GUI version '$StagedVersion'."
}
$version = if ($isReleaseStaging) {
    $StagedVersion
} else {
    (& (Join-Path $PSScriptRoot 'get-gui-version.ps1')).Version
}
$marketingVersion = $version -replace '-.*$', ''
$errors = [System.Collections.Generic.List[string]]::new()

$projections = @(
    @{ Path = 'eng/GuiVersion.props'; Pattern = '<SharpTSGuiSdkVersion>[^<]+</SharpTSGuiSdkVersion>'; Replacement = "<SharpTSGuiSdkVersion>$version</SharpTSGuiSdkVersion>" },
    @{ Path = 'eng/GuiVersion.props'; Pattern = '<SharpTSGuiMarketingVersion>[^<]+</SharpTSGuiMarketingVersion>'; Replacement = "<SharpTSGuiMarketingVersion>$marketingVersion</SharpTSGuiMarketingVersion>" },
    @{ Path = 'src/SharpTS/Cli/GuiVersion.g.cs'; Pattern = 'internal const string Value = "[^"]+";'; Replacement = "internal const string Value = `"$version`";" },
    @{ Path = 'src/SharpTS.Gui.Sdk/Templates/sharpts-gui/SharpTSGuiApp.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'tests/fixtures/SharpTS.Gui.Sdk.Consumer/SharpTS.Gui.Sdk.Consumer.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'src/SharpTS.Gui.Sdk/GuiPackage/package.json'; Pattern = '"version"\s*:\s*"[^"]+"'; Replacement = "`"version`": `"$version`"" },
    @{ Path = 'samples/Calculator/Calculator.csproj'; Pattern = 'SharpTS\.Gui\.Sdk/[^"<]+'; Replacement = "SharpTS.Gui.Sdk/$version" },
    @{ Path = 'samples/Calculator/run-local.ps1'; Pattern = '\$sdkVersion\s*=\s*''[^'']+'''; Replacement = "`$sdkVersion = '$version'" }
)
if ($isReleaseStaging) {
    $projections += @{ Path = 'src/SharpTS.Gui.Sdk/readme.md'; Pattern = 'SharpTS\.Gui\.Sdk::(?:<version>|[0-9A-Za-z][0-9A-Za-z.-]*)'; Replacement = "SharpTS.Gui.Sdk::$version" }
}

foreach ($projection in $projections) {
    $path = Join-Path $repositoryRoot $projection.Path
    $content = Get-Content -LiteralPath $path -Raw
    $updated = [regex]::Replace($content, $projection.Pattern, $projection.Replacement)
    if ($updated -eq $content -and $content -notmatch [regex]::Escape($version)) {
        $errors.Add("$($projection.Path) does not expose a replaceable GUI version.")
        continue
    }
    if ($Check) {
        if ($updated -ne $content) { $errors.Add("$($projection.Path) is not synchronized to GUI version $version.") }
    }
    elseif ($updated -ne $content) {
        [IO.File]::WriteAllText($path, $updated, [Text.UTF8Encoding]::new($false))
        Write-Host "Updated $($projection.Path) to $version."
    }
}
if ($errors.Count -gt 0) { throw "GUI version synchronization failed:`n - $($errors -join "`n - ")" }
if ($Check) { Write-Host "GUI version projections are synchronized to $version." }
