[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$')][string]$BundleIdentifier,
    [Parameter(Mandatory)][string]$DisplayName,
    [Parameter(Mandatory)][ValidatePattern('^\d+(\.\d+){0,2}$')][string]$ShortVersion,
    [Parameter(Mandatory)][ValidatePattern('^\d+(\.\d+){0,2}$')][string]$BuildVersion,
    [Parameter(Mandatory)][ValidateSet('arm64')][string]$Architecture,
    [Parameter(Mandatory)][string]$Executable,
    [ValidatePattern('^\d+(\.\d+){0,2}$')][string]$MinimumSystemVersion = '12.0',
    [string]$IconFile,
    [string]$SigningIdentity,
    [string]$EntitlementsPath,
    [string]$NotaryKeychainProfile,
    [switch]$CreateDmg,
    [switch]$RequireSigned,
    [switch]$RequireNotarized,
    [switch]$StageOnly,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Resolve-FullPath([string]$Path) { [IO.Path]::GetFullPath($Path, (Get-Location).Path) }
function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
function Read-U32BigEndian([byte[]]$Bytes, [int]$Offset) {
    return ([uint32]$Bytes[$Offset] -shl 24) -bor ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor [uint32]$Bytes[$Offset + 3]
}
function Read-U32LittleEndian([byte[]]$Bytes, [int]$Offset) {
    return [uint32]$Bytes[$Offset] -bor ([uint32]$Bytes[$Offset + 1] -shl 8) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 16) -bor ([uint32]$Bytes[$Offset + 3] -shl 24)
}
function Get-MachOArchitectures([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 8) { throw "Mach-O file is truncated: $Path" }
    $magic = ($bytes[0..3] | ForEach-Object { $_.ToString('X2') }) -join ''
    $cpuTypes = [Collections.Generic.List[uint32]]::new()
    if ($magic -in @('CAFEBABE', 'CAFEBABF')) {
        $count = Read-U32BigEndian $bytes 4
        $entrySize = if ($magic -eq 'CAFEBABF') { 32 } else { 20 }
        if ($count -gt 16 -or $bytes.Length -lt 8 + ($count * $entrySize)) { throw "Invalid universal Mach-O header: $Path" }
        for ($index = 0; $index -lt $count; $index++) {
            $cpuTypes.Add((Read-U32BigEndian $bytes (8 + ($index * $entrySize))))
        }
    }
    elseif ($magic -in @('FEEDFACE', 'FEEDFACF')) {
        $cpuTypes.Add((Read-U32BigEndian $bytes 4))
    }
    elseif ($magic -in @('CEFAEDFE', 'CFFAEDFE')) {
        $cpuTypes.Add((Read-U32LittleEndian $bytes 4))
    }
    else { throw "File is not a supported Mach-O binary: $Path" }
    return @($cpuTypes | ForEach-Object {
        switch ($_ -band 0xFFFFFFFF) {
            0x0100000C { 'arm64' }
            default { "cpu-0x$($_.ToString('X8'))" }
        }
    })
}
function Assert-MachOArchitecture([string]$Path, [string]$Expected) {
    $architectures = @(Get-MachOArchitectures $Path)
    if ($Expected -notin $architectures) {
        throw "Mach-O '$Path' does not contain '$Expected' (contains: $($architectures -join ', '))."
    }
}
function Assert-SafeOutput([string]$Output, [string]$Publish) {
    $separator = [IO.Path]::DirectorySeparatorChar
    $root = [IO.Path]::GetPathRoot($Output)
    if ($Output.TrimEnd($separator) -eq $root.TrimEnd($separator)) { throw 'OutputDirectory cannot be a filesystem root.' }
    $outputPrefix = $Output.TrimEnd($separator) + $separator
    $publishPrefix = $Publish.TrimEnd($separator) + $separator
    if ($outputPrefix.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $publishPrefix.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory and PublishDirectory must not contain one another.'
    }
}
function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

$publishPath = Resolve-FullPath $PublishDirectory
$outputPath = Resolve-FullPath $OutputDirectory
if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) { throw "PublishDirectory does not exist: $publishPath" }
if ([IO.Path]::IsPathRooted($Executable) -or $Executable.Contains('/') -or $Executable.Contains('\')) {
    throw 'Executable must be a file name at the publish root.'
}
$publishedExecutable = Join-Path $publishPath $Executable
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) { throw "Published executable does not exist: $Executable" }
if ([string]::IsNullOrWhiteSpace($DisplayName)) { throw 'DisplayName must not be empty.' }
Assert-SafeOutput $outputPath $publishPath
Assert-MachOArchitecture $publishedExecutable $Architecture
foreach ($nativeLibrary in @(Get-ChildItem -LiteralPath $publishPath -File -Filter '*.dylib' -Recurse)) {
    Assert-MachOArchitecture $nativeLibrary.FullName $Architecture
}

if ($RequireSigned -and [string]::IsNullOrWhiteSpace($SigningIdentity)) { throw '-RequireSigned requires SigningIdentity.' }
if ($RequireNotarized -and ([string]::IsNullOrWhiteSpace($SigningIdentity) -or [string]::IsNullOrWhiteSpace($NotaryKeychainProfile))) {
    throw '-RequireNotarized requires SigningIdentity and NotaryKeychainProfile.'
}
if ($RequireNotarized -and $StageOnly) { throw '-RequireNotarized cannot be combined with StageOnly.' }
if (($RequireSigned -or $RequireNotarized -or -not [string]::IsNullOrWhiteSpace($SigningIdentity)) -and -not $IsMacOS) {
    throw 'Signing and notarization must run on macOS.'
}

$markerName = '.sharpts-gui-macos-distribution'
$markerPath = Join-Path $outputPath $markerName
if (Test-Path -LiteralPath $outputPath) {
    $existing = @(Get-ChildItem -LiteralPath $outputPath -Force)
    if ($existing.Count -ne 0 -and -not $Force) { throw "OutputDirectory is not empty; pass -Force to replace it: $outputPath" }
    if ($Force -and $existing.Count -ne 0 -and -not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Refusing to replace an output directory not created by this packager: $outputPath"
    }
    if ($Force) { Remove-Item -LiteralPath $outputPath -Recurse -Force }
}
New-Item -ItemType Directory -Path $outputPath | Out-Null
[IO.File]::WriteAllText((Join-Path $outputPath $markerName), "SharpTS GUI macOS distribution output`n", [Text.UTF8Encoding]::new($false))

$bundleSafeName = $DisplayName -replace '[^A-Za-z0-9._-]', '-'
$appPath = Join-Path $outputPath "$bundleSafeName.app"
$contentsPath = Join-Path $appPath 'Contents'
$macOsPath = Join-Path $contentsPath 'MacOS'
$resourcesPath = Join-Path $contentsPath 'Resources'
New-Item -ItemType Directory -Path $macOsPath, $resourcesPath | Out-Null
Get-ChildItem -LiteralPath $publishPath -Force | Copy-Item -Destination $macOsPath -Recurse -Force
Get-ChildItem -LiteralPath $macOsPath -File -Recurse |
    Where-Object Extension -in @('.pdb', '.dbg') |
    Remove-Item -Recurse -Force
Get-ChildItem -LiteralPath $macOsPath -Directory -Recurse |
    Where-Object Name -like '*.dSYM' |
    Remove-Item -Recurse -Force

$iconEntry = ''
if (-not [string]::IsNullOrWhiteSpace($IconFile)) {
    $iconPath = Resolve-FullPath $IconFile
    if ([IO.Path]::GetExtension($iconPath) -ne '.icns' -or -not (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
        throw 'IconFile must name an existing .icns file.'
    }
    Copy-Item -LiteralPath $iconPath -Destination (Join-Path $resourcesPath ([IO.Path]::GetFileName($iconPath)))
    $iconEntry = "<key>CFBundleIconFile</key>`n  <string>$(Escape-Xml ([IO.Path]::GetFileName($iconPath)))</string>"
}

$plist = Get-Content -LiteralPath (Join-Path $repositoryRoot 'distribution\macos\Info.plist.in') -Raw
$replacements = [ordered]@{
    '@@DISPLAY_NAME@@' = Escape-Xml $DisplayName
    '@@EXECUTABLE@@' = Escape-Xml $Executable
    '@@BUNDLE_IDENTIFIER@@' = Escape-Xml $BundleIdentifier
    '@@BUNDLE_NAME@@' = Escape-Xml $DisplayName
    '@@SHORT_VERSION@@' = $ShortVersion
    '@@BUILD_VERSION@@' = $BuildVersion
    '@@MINIMUM_SYSTEM_VERSION@@' = $MinimumSystemVersion
    '@@ARCHITECTURE@@' = $Architecture
    '@@ICON_ENTRY@@' = $iconEntry
}
foreach ($replacement in $replacements.GetEnumerator()) { $plist = $plist.Replace($replacement.Key, $replacement.Value) }
if ($plist.Contains('@@', [StringComparison]::Ordinal)) { throw 'Info.plist template contains an unresolved placeholder.' }
$plistPath = Join-Path $contentsPath 'Info.plist'
[IO.File]::WriteAllText($plistPath, $plist, [Text.UTF8Encoding]::new($false))
[xml](Get-Content -LiteralPath $plistPath -Raw) | Out-Null
if ($IsMacOS) { Invoke-Checked '/usr/bin/plutil' @('-lint', $plistPath) }

$stagedExecutable = Join-Path $macOsPath $Executable
if ($IsMacOS) { Invoke-Checked '/bin/chmod' @('+x', $stagedExecutable) }

if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) {
    $codesignArguments = @('--force', '--timestamp', '--options', 'runtime', '--sign', $SigningIdentity)
    foreach ($library in @(Get-ChildItem -LiteralPath $macOsPath -File -Filter '*.dylib' -Recurse | Sort-Object FullName)) {
        Invoke-Checked '/usr/bin/codesign' @($codesignArguments + $library.FullName)
    }
    Invoke-Checked '/usr/bin/codesign' @($codesignArguments + $stagedExecutable)
    $bundleArguments = @($codesignArguments)
    if (-not [string]::IsNullOrWhiteSpace($EntitlementsPath)) {
        $resolvedEntitlements = Resolve-FullPath $EntitlementsPath
        if (-not (Test-Path -LiteralPath $resolvedEntitlements -PathType Leaf)) { throw "Entitlements file does not exist: $resolvedEntitlements" }
        $bundleArguments += @('--entitlements', $resolvedEntitlements)
    }
    Invoke-Checked '/usr/bin/codesign' @($bundleArguments + $appPath)
    Invoke-Checked '/usr/bin/codesign' @('--verify', '--deep', '--strict', '--verbose=4', $appPath)
}

$zipPath = Join-Path $outputPath "$bundleSafeName-$ShortVersion-$Architecture.zip"
if (-not $StageOnly) {
    if (-not $IsMacOS) { throw 'Creating a distributable macOS archive must run on macOS; use -StageOnly for structural validation.' }
    Invoke-Checked '/usr/bin/ditto' @('-c', '-k', '--sequesterRsrc', '--keepParent', $appPath, $zipPath)
    if ($RequireNotarized) {
        Invoke-Checked '/usr/bin/xcrun' @('notarytool', 'submit', $zipPath, '--keychain-profile', $NotaryKeychainProfile, '--wait')
        Invoke-Checked '/usr/bin/xcrun' @('stapler', 'staple', $appPath)
        Invoke-Checked '/usr/sbin/spctl' @('--assess', '--type', 'execute', '--verbose=4', $appPath)
        Remove-Item -LiteralPath $zipPath -Force
        Invoke-Checked '/usr/bin/ditto' @('-c', '-k', '--sequesterRsrc', '--keepParent', $appPath, $zipPath)
    }
}

$dmgPath = $null
if ($CreateDmg) {
    if ($StageOnly -or -not $IsMacOS) { throw 'DMG creation requires a non-stage-only macOS run.' }
    $dmgPath = Join-Path $outputPath "$bundleSafeName-$ShortVersion-$Architecture.dmg"
    Invoke-Checked '/usr/bin/hdiutil' @('create', '-volname', $DisplayName, '-srcfolder', $appPath, '-ov', '-format', 'UDZO', $dmgPath)
    if (-not [string]::IsNullOrWhiteSpace($SigningIdentity)) {
        Invoke-Checked '/usr/bin/codesign' @('--force', '--timestamp', '--sign', $SigningIdentity, $dmgPath)
        Invoke-Checked '/usr/bin/codesign' @('--verify', '--verbose=4', $dmgPath)
    }
    if ($RequireNotarized) {
        Invoke-Checked '/usr/bin/xcrun' @('notarytool', 'submit', $dmgPath, '--keychain-profile', $NotaryKeychainProfile, '--wait')
        Invoke-Checked '/usr/bin/xcrun' @('stapler', 'staple', $dmgPath)
        Invoke-Checked '/usr/bin/xcrun' @('stapler', 'validate', $dmgPath)
    }
}

$checksumsPath = Join-Path $outputPath 'SHA256SUMS'
$artifacts = if ($StageOnly) {
    @(Get-ChildItem -LiteralPath $appPath -File -Recurse | Sort-Object FullName)
}
else {
    @(Get-ChildItem -LiteralPath $outputPath -File | Where-Object Name -notin @($markerName, 'SHA256SUMS') | Sort-Object Name)
}
$lines = @($artifacts | ForEach-Object {
    $relativePath = [IO.Path]::GetRelativePath($outputPath, $_.FullName).Replace('\', '/')
    "$(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $relativePath"
})
[IO.File]::WriteAllLines($checksumsPath, $lines, [Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    App = $appPath
    Zip = if (Test-Path -LiteralPath $zipPath) { $zipPath } else { $null }
    Dmg = $dmgPath
    InfoPlist = $plistPath
    Checksums = $checksumsPath
    Signed = -not [string]::IsNullOrWhiteSpace($SigningIdentity)
    Notarized = [bool]($RequireNotarized -and -not $StageOnly)
}
