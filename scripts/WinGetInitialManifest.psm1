Set-StrictMode -Version Latest

$script:ManifestVersion = '1.12.0'
$script:RepositoryUrl = 'https://github.com/nickna/SharpTS'

function Assert-SharpTSStableRelease {
    param([string]$Version, [object]$Release)
    if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw 'Initial WinGet manifests require a stable major.minor.patch version without a v prefix.'
    }
    if ($Release.tag_name -cne "v$Version" -or $Release.draft -isnot [bool] -or $Release.draft `
        -or $Release.prerelease -isnot [bool] -or $Release.prerelease) {
        throw 'The release must be published, non-draft, non-prerelease, and match the requested stable version.'
    }
}

function Get-SharpTSWindowsReleaseAssetNames {
    param([Parameter(Mandatory)][string]$Version)
    foreach ($prefix in @('sharpts', 'sharpts-native')) {
        foreach ($architecture in @('x64', 'arm64')) { "$prefix-$Version-win-$architecture.zip" }
    }
}

function Assert-SharpTSWindowsArchive {
    param([string]$Path, [ValidateSet('x64', 'arm64')][string]$Architecture)
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $files = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.StartsWith('/') -or $name.Contains(':') -or ($name.Split('/') -contains '..')) {
                throw "Archive contains an unsafe path: $($entry.FullName)"
            }
            while ($name.StartsWith('./')) { $name = $name.Substring(2) }
            if (!$name -or $name.EndsWith('/')) { continue }
            if ($files.ContainsKey($name)) { throw "Archive contains duplicate paths: $name" }
            $files.Add($name, $entry)
        }
        foreach ($required in @('sharpts.exe', 'LICENSE', 'README.md')) {
            if (!$files.ContainsKey($required) -or $files[$required].Length -eq 0) {
                throw "Archive must contain a nonempty root-level $required."
            }
        }
        $reader = [IO.BinaryReader]::new($files['sharpts.exe'].Open())
        try {
            if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'sharpts.exe is not a Windows PE executable.' }
            if ($reader.ReadBytes(58).Length -ne 58) { throw 'Truncated PE DOS header.' }
            $offset = $reader.ReadInt32()
            if ($offset -lt 64 -or $offset -gt 1048576) { throw 'Invalid PE header offset.' }
            if ($reader.ReadBytes($offset - 64).Length -ne ($offset - 64) -or $reader.ReadUInt32() -ne 0x4550) {
                throw 'Invalid PE signature.'
            }
            $expectedMachine = if ($Architecture -eq 'x64') { 0x8664 } else { 0xaa64 }
            if ($reader.ReadUInt16() -ne $expectedMachine) { throw "sharpts.exe architecture does not match $Architecture." }
        }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
}

function New-SharpTSWinGetInitialManifestSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][object]$Release,
        [Parameter(Mandatory)][string]$AssetDirectory,
        [Parameter(Mandatory)][string]$OutputDirectory
    )
    Assert-SharpTSStableRelease $Version $Release
    $assets = @{}
    foreach ($name in Get-SharpTSWindowsReleaseAssetNames $Version) {
        $matches = @($Release.assets | Where-Object { $_.name -ceq $name })
        $url = "$script:RepositoryUrl/releases/download/v$Version/$name"
        if ($matches.Count -ne 1 -or $matches[0].browser_download_url -cne $url) {
            throw "Published release must contain exactly one asset with its immutable release URL: $name"
        }
        $path = Join-Path $AssetDirectory $name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release archive not found: $path" }
        $architecture = if ($name.EndsWith('-win-arm64.zip')) { 'arm64' } else { 'x64' }
        Assert-SharpTSWindowsArchive $path $architecture
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $digestProperty = $matches[0].PSObject.Properties['digest']
        if ($digestProperty -and $digestProperty.Value -and $digestProperty.Value -cne "sha256:$($hash.ToLowerInvariant())") {
            throw "Downloaded archive hash differs from the GitHub release asset digest: $name"
        }
        $assets[$name] = [ordered]@{ name = $name; url = $url; sha256 = $hash }
    }

    # Prepare every file before writing any output, so missing or mismatched assets
    # cannot leave a partially generated package submission.
    $files = [ordered]@{}
    $directories = @()
    foreach ($native in @($false, $true)) {
        $identifier = if ($native) { 'SharpTS.SharpTS.NativeAOT' } else { 'SharpTS.SharpTS' }
        $prefix = if ($native) { 'sharpts-native' } else { 'sharpts' }
        $packageName = if ($native) { 'SharpTS (Native AOT)' } else { 'SharpTS' }
        $description = if ($native) {
            'Native AOT TypeScript interpreter and compiler with a reduced feature surface'
        } else { 'TypeScript interpreter and compiler for .NET with full interop support' }
        $details = if ($native) {
            'Native AOT supports straight TypeScript. Third-party .NET references, --verify, --gen-decl, and compiled child_process.fork require the managed SKU.'
        } else { 'The managed, self-contained distribution provides the full SharpTS feature set, including .NET interop.' }
        $relative = Join-Path 'manifests/s' (($identifier.Split('.') + $Version) -join '/')
        $directory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $relative
        $directories += $directory
        $managedAsset = $assets["$prefix-$Version-win-x64.zip"]
        $armAsset = $assets["$prefix-$Version-win-arm64.zip"]
        $files[(Join-Path $directory "$identifier.yaml")] = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
PackageIdentifier: $identifier
PackageVersion: '$Version'
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $script:ManifestVersion
"@
        $files[(Join-Path $directory "$identifier.locale.en-US.yaml")] = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json
PackageIdentifier: $identifier
PackageVersion: '$Version'
PackageLocale: en-US
Publisher: Nick Nassiri
PublisherUrl: https://github.com/nickna
PublisherSupportUrl: $script:RepositoryUrl/issues
PackageName: $packageName
PackageUrl: $script:RepositoryUrl
License: MIT
LicenseUrl: $script:RepositoryUrl/blob/v$Version/LICENSE
ShortDescription: $description
Description: >-
  $details
  Both SharpTS packages expose the sharpts command and are mutually exclusive.
  Uninstall the current SKU before installing the other one.
ReleaseNotesUrl: $script:RepositoryUrl/releases/tag/v$Version
InstallationNotes: Uninstall the other SharpTS SKU before installing this package.
Tags:
- typescript
- compiler
- dotnet
- cli
ManifestType: defaultLocale
ManifestVersion: $script:ManifestVersion
"@
        $files[(Join-Path $directory "$identifier.installer.yaml")] = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
PackageIdentifier: $identifier
PackageVersion: '$Version'
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: sharpts.exe
  PortableCommandAlias: sharpts
Commands:
- sharpts
Installers:
- Architecture: x64
  InstallerUrl: $($managedAsset.url)
  InstallerSha256: $($managedAsset.sha256)
- Architecture: arm64
  InstallerUrl: $($armAsset.url)
  InstallerSha256: $($armAsset.sha256)
ManifestType: installer
ManifestVersion: $script:ManifestVersion
"@
    }
    foreach ($path in @($files.Keys)) {
        $files[$path] = $files[$path].Replace("`r`n", "`n").TrimEnd() + "`n"
        if (Test-Path -LiteralPath $path) {
            if ([IO.File]::ReadAllText($path) -cne $files[$path]) {
                throw "Refusing to overwrite a different existing manifest: $path"
            }
        }
    }
    foreach ($path in $files.Keys) {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
        [IO.File]::WriteAllText($path, $files[$path], [Text.UTF8Encoding]::new($false))
    }
    [pscustomobject]@{
        Version = $Version
        ManifestVersion = $script:ManifestVersion
        PackageDirectories = $directories
        Assets = @($assets.Values | Sort-Object name)
    }
}

Export-ModuleMember -Function Get-SharpTSWindowsReleaseAssetNames, New-SharpTSWinGetInitialManifestSet
