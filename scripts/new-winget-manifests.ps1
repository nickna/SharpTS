[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDirectory = './artifacts/winget-initial',
    [string]$AssetDirectory
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'WinGetInitialManifest.psm1') -Force
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Use a stable major.minor.patch version without a v prefix.'
}
if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install the GitHub CLI (gh) to read and download published release assets.' }
$releaseJson = gh api "repos/nickna/SharpTS/releases/tags/v$Version"
if ($LASTEXITCODE -ne 0) { throw "Could not read the published release v$Version." }
$release = ($releaseJson -join "`n") | ConvertFrom-Json
if ($release.draft -or $release.prerelease -or $release.tag_name -cne "v$Version") {
    throw 'Initial manifests require a published stable release matching the requested version.'
}
$scratch = $null
try {
    if ([string]::IsNullOrWhiteSpace($AssetDirectory)) {
        $scratch = Join-Path ([IO.Path]::GetTempPath()) "sharpts-winget-assets-$([guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($scratch) | Out-Null
        $AssetDirectory = $scratch
        foreach ($name in Get-SharpTSWindowsReleaseAssetNames $Version) {
            $asset = @($release.assets | Where-Object { $_.name -ceq $name })
            $url = "https://github.com/nickna/SharpTS/releases/download/v$Version/$name"
            if ($asset.Count -ne 1 -or $asset[0].browser_download_url -cne $url) {
                throw "Published release asset missing or URL mismatch: $name"
            }
            gh release download "v$Version" --repo nickna/SharpTS --pattern $name --dir $AssetDirectory
            if ($LASTEXITCODE -ne 0) { throw "Could not download published release asset: $name" }
        }
    }
    $result = New-SharpTSWinGetInitialManifestSet -Version $Version -Release $release `
        -AssetDirectory $AssetDirectory -OutputDirectory $OutputDirectory
    $result | ConvertTo-Json -Depth 6
    foreach ($directory in $result.PackageDirectories) { Write-Host "Validate: winget validate --manifest `"$directory`"" }
}
finally {
    if ($scratch -and (Test-Path -LiteralPath $scratch)) {
        $resolved = [IO.Path]::GetFullPath($scratch)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (!$resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) `
            -or (Split-Path -Leaf $resolved) -notlike 'sharpts-winget-assets-*') {
            throw 'Refusing to remove a temporary directory outside the manifest preparation workspace.'
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
