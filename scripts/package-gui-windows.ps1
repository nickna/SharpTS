[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.-]{3,50}$')][string]$PackageIdentity,
    [Parameter(Mandatory)][ValidatePattern('^CN=.+')][string]$Publisher,
    [Parameter(Mandatory)][string]$DisplayName,
    [Parameter(Mandatory)][ValidatePattern('^\d{1,5}(\.\d{1,5}){3}$')][string]$Version,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$AssetsDirectory,
    [string]$Description = $DisplayName,
    [string]$PublisherDisplayName = $DisplayName,
    [string]$PackageUri,
    [ValidateRange(0, 255)][int]$UpdateCheckHours = 4,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [string]$MakeAppxPath,
    [string]$SignToolPath,
    [string]$RepositoryUrl = 'https://github.com/nickna/SharpTS',
    [string]$SourceCommit,
    [string]$BuildInvocationId = $env:GITHUB_RUN_ID,
    [switch]$RequireSigned,
    [switch]$StageOnly,
    [switch]$KeepStaging,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Resolve-FullPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path, (Get-Location).Path)
}

function Assert-SafeOutputPath([string]$Path, [string]$PublishPath) {
    $root = [IO.Path]::GetPathRoot($Path)
    if ([string]::Equals($Path.TrimEnd('\'), $root.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory cannot be a drive root: $Path"
    }
    $separator = [IO.Path]::DirectorySeparatorChar
    $outputWithSeparator = $Path.TrimEnd($separator) + $separator
    $publishWithSeparator = $PublishPath.TrimEnd($separator) + $separator
    if ([string]::Equals($Path.TrimEnd($separator), $PublishPath.TrimEnd($separator), [StringComparison]::OrdinalIgnoreCase) -or
        $outputWithSeparator.StartsWith($publishWithSeparator, [StringComparison]::OrdinalIgnoreCase) -or
        $publishWithSeparator.StartsWith($outputWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputDirectory and PublishDirectory must not contain one another.'
    }
}

function Resolve-WindowsSdkTool([string]$ExplicitPath, [string]$FileName) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = Resolve-FullPath $ExplicitPath
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "$FileName does not exist: $resolved"
        }
        return $resolved
    }
    $command = Get-Command $FileName -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }
    if ([string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        throw "$FileName was not found and the Windows SDK root is unavailable."
    }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Filter $FileName -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object FullName -match '\\x64\\' |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "$FileName was not found. Install the Windows 10/11 SDK or pass its explicit path."
    }
    return $candidate.FullName
}

function Escape-Xml([string]$Value) {
    return [Security.SecurityElement]::Escape($Value)
}

function Invoke-Sign([string]$Tool, [string]$Path, [string]$Thumbprint, [string]$Timestamp) {
    & $Tool sign /sha1 $Thumbprint /s My /fd SHA256 /tr $Timestamp /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed to sign '$Path'." }
    & $Tool verify /pa /all $Path
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for '$Path'." }
}

function Get-FileInventory([string]$Root) {
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
        [pscustomobject]@{
            RelativePath = $relative
            FullName = $_.FullName
            Length = $_.Length
            Sha1 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA1).Hash.ToLowerInvariant()
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}

$publishPath = Resolve-FullPath $PublishDirectory
$outputPath = Resolve-FullPath $OutputDirectory
$assetsPath = Resolve-FullPath $AssetsDirectory
if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw "PublishDirectory does not exist: $publishPath"
}
if ([IO.Path]::IsPathRooted($Executable)) { throw 'Executable must be relative to PublishDirectory.' }
$publishedExecutablePath = [IO.Path]::GetFullPath($Executable, $publishPath)
$publishPrefix = $publishPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publishedExecutablePath.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Executable must remain inside PublishDirectory.'
}
if (-not (Test-Path -LiteralPath $publishedExecutablePath -PathType Leaf)) {
    throw "Published executable does not exist: $Executable"
}
Assert-SafeOutputPath $outputPath $publishPath
foreach ($requiredText in @($DisplayName, $Description, $PublisherDisplayName)) {
    if ([string]::IsNullOrWhiteSpace($requiredText)) { throw 'Display, description, and publisher names must not be empty.' }
}

$versionParts = @($Version.Split('.') | ForEach-Object { [int]$_ })
if ($versionParts | Where-Object { $_ -gt 65535 }) {
    throw 'Each MSIX version component must be between 0 and 65535.'
}
if (-not [string]::IsNullOrWhiteSpace($PackageUri)) {
    [Uri]$parsedPackageUri = $null
    if (-not [Uri]::TryCreate($PackageUri, [UriKind]::Absolute, [ref]$parsedPackageUri) -or
        $parsedPackageUri.Scheme -ne 'https') {
        throw 'PackageUri must be an absolute HTTPS URI.'
    }
}

$requiredAssets = @('Square44x44Logo.png', 'Square150x150Logo.png', 'StoreLogo.png')
foreach ($asset in $requiredAssets) {
    if (-not (Test-Path -LiteralPath (Join-Path $assetsPath $asset) -PathType Leaf)) {
        throw "Required MSIX asset is missing: $asset"
    }
}

$outputMarkerName = '.sharpts-gui-distribution'
$outputMarkerPath = Join-Path $outputPath $outputMarkerName
if (Test-Path -LiteralPath $outputPath) {
    $existing = @(Get-ChildItem -LiteralPath $outputPath -Force)
    if ($existing.Count -ne 0 -and -not $Force) {
        throw "OutputDirectory is not empty; pass -Force to replace it: $outputPath"
    }
    if ($Force -and $existing.Count -ne 0 -and -not (Test-Path -LiteralPath $outputMarkerPath -PathType Leaf)) {
        throw "Refusing to replace an output directory not created by this packager: $outputPath"
    }
    if ($Force) { Remove-Item -LiteralPath $outputPath -Recurse -Force }
}
New-Item -ItemType Directory -Path $outputPath | Out-Null
[IO.File]::WriteAllText((Join-Path $outputPath $outputMarkerName), "SharpTS GUI distribution output`n", [Text.UTF8Encoding]::new($false))
$stagingPath = Join-Path $outputPath 'staging'
New-Item -ItemType Directory -Path $stagingPath | Out-Null
Copy-Item -Path (Join-Path $publishPath '*') -Destination $stagingPath -Recurse -Force
Get-ChildItem -LiteralPath $stagingPath -File -Recurse |
    Where-Object Extension -in @('.pdb', '.dbg') |
    Remove-Item -Force

$stagedAssetsPath = Join-Path $stagingPath 'Assets'
New-Item -ItemType Directory -Path $stagedAssetsPath -Force | Out-Null
foreach ($asset in $requiredAssets) {
    Copy-Item -LiteralPath (Join-Path $assetsPath $asset) -Destination (Join-Path $stagedAssetsPath $asset) -Force
}

$template = Get-Content -LiteralPath (Join-Path $repositoryRoot 'distribution\windows\AppxManifest.xml.in') -Raw
$replacements = [ordered]@{
    '@@PACKAGE_IDENTITY@@' = Escape-Xml $PackageIdentity
    '@@PUBLISHER@@' = Escape-Xml $Publisher
    '@@VERSION@@' = $Version
    '@@ARCHITECTURE@@' = $Architecture
    '@@DISPLAY_NAME@@' = Escape-Xml $DisplayName
    '@@PUBLISHER_DISPLAY_NAME@@' = Escape-Xml $PublisherDisplayName
    '@@DESCRIPTION@@' = Escape-Xml $Description
    '@@EXECUTABLE@@' = Escape-Xml $Executable
}
foreach ($replacement in $replacements.GetEnumerator()) {
    $template = $template.Replace($replacement.Key, $replacement.Value)
}
$manifestPath = Join-Path $stagingPath 'AppxManifest.xml'
[IO.File]::WriteAllText($manifestPath, $template, [Text.UTF8Encoding]::new($false))
[xml](Get-Content -LiteralPath $manifestPath -Raw) | Out-Null

if ($RequireSigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw '-RequireSigned requires CertificateThumbprint.'
}
$signTool = $null
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if ($null -eq $certificate) { throw "Signing certificate was not found in CurrentUser\\My: $CertificateThumbprint" }
    if (-not [string]::Equals($certificate.Subject, $Publisher, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Signing certificate subject '$($certificate.Subject)' does not match Publisher '$Publisher'."
    }
    $signTool = Resolve-WindowsSdkTool $SignToolPath 'signtool.exe'
    foreach ($binary in @(Get-ChildItem -LiteralPath $stagingPath -File -Recurse | Where-Object Extension -in @('.exe', '.dll'))) {
        Invoke-Sign $signTool $binary.FullName $CertificateThumbprint $TimestampUrl
    }
}

$artifactBaseName = ($DisplayName -replace '[^A-Za-z0-9.-]', '-') + "-$Version-$Architecture"
$msixPath = Join-Path $outputPath "$artifactBaseName.msix"
if (-not $StageOnly) {
    $makeAppx = Resolve-WindowsSdkTool $MakeAppxPath 'makeappx.exe'
    & $makeAppx pack /d $stagingPath /p $msixPath /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $msixPath -PathType Leaf)) {
        throw 'makeappx did not produce the expected MSIX artifact.'
    }
    if ($null -ne $signTool) {
        Invoke-Sign $signTool $msixPath $CertificateThumbprint $TimestampUrl
    }
    elseif ($RequireSigned) {
        throw 'The MSIX artifact was not signed.'
    }
}

$appInstallerPath = $null
if (-not [string]::IsNullOrWhiteSpace($PackageUri)) {
    $appInstallerUri = [Uri]$PackageUri
    $appInstallerPath = Join-Path $outputPath "$artifactBaseName.appinstaller"
    $appInstaller = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller Uri="$(Escape-Xml ($appInstallerUri.GetLeftPart([UriPartial]::Path) -replace '[^/]+$', "$artifactBaseName.appinstaller"))" Version="$Version" xmlns="http://schemas.microsoft.com/appx/appinstaller/2018">
  <MainPackage Name="$(Escape-Xml $PackageIdentity)" Publisher="$(Escape-Xml $Publisher)" Version="$Version" ProcessorArchitecture="$Architecture" Uri="$(Escape-Xml $PackageUri)" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="$UpdateCheckHours" ShowPrompt="true" UpdateBlocksActivation="false" />
  </UpdateSettings>
</AppInstaller>
"@
    [IO.File]::WriteAllText($appInstallerPath, $appInstaller, [Text.UTF8Encoding]::new($false))
    [xml](Get-Content -LiteralPath $appInstallerPath -Raw) | Out-Null
}

$inventory = Get-FileInventory $stagingPath
$inventorySeed = ($inventory | ForEach-Object { "$($_.RelativePath):$($_.Sha256)" }) -join "`n"
$inventoryDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($inventorySeed))).ToLowerInvariant()
$created = [DateTimeOffset]::UtcNow.ToString('O')
$spdxPath = Join-Path $outputPath "$artifactBaseName.spdx.json"
$spdxFiles = for ($index = 0; $index -lt $inventory.Count; $index++) {
    [ordered]@{
        fileName = "./$($inventory[$index].RelativePath)"
        SPDXID = "SPDXRef-File-$($index + 1)"
        checksums = @(
            [ordered]@{ algorithm = 'SHA1'; checksumValue = $inventory[$index].Sha1 },
            [ordered]@{ algorithm = 'SHA256'; checksumValue = $inventory[$index].Sha256 })
        licenseConcluded = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
    }
}
$verificationSeed = (@($inventory.Sha1) | Sort-Object) -join ''
$verificationCode = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([Text.Encoding]::ASCII.GetBytes($verificationSeed))).ToLowerInvariant()
$relationships = @([ordered]@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = 'SPDXRef-Package' })
$relationships += @($spdxFiles | ForEach-Object {
    [ordered]@{ spdxElementId = 'SPDXRef-Package'; relationshipType = 'CONTAINS'; relatedSpdxElement = $_.SPDXID }
})
$spdx = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "$DisplayName $Version"
    documentNamespace = "https://spdx.sharpts.dev/gui/$PackageIdentity/$Version/$inventoryDigest"
    creationInfo = [ordered]@{ created = $created; creators = @('Tool: SharpTS GUI distribution packager') }
    packages = @([ordered]@{
        name = $DisplayName
        SPDXID = 'SPDXRef-Package'
        versionInfo = $Version
        downloadLocation = 'NOASSERTION'
        filesAnalyzed = $true
        packageVerificationCode = [ordered]@{ packageVerificationCodeValue = $verificationCode }
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
    })
    files = @($spdxFiles)
    relationships = @($relationships)
}
[IO.File]::WriteAllText($spdxPath, ($spdx | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = (& git -C $repositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0) { $SourceCommit = 'unknown' }
}
$subjects = @()
if (Test-Path -LiteralPath $msixPath -PathType Leaf) {
    $subjects += [ordered]@{ name = [IO.Path]::GetFileName($msixPath); digest = [ordered]@{ sha256 = (Get-FileHash $msixPath -Algorithm SHA256).Hash.ToLowerInvariant() } }
} else {
    $subjects += [ordered]@{ name = 'staged-msix-content'; digest = [ordered]@{ sha256 = $inventoryDigest } }
}
$provenancePath = Join-Path $outputPath "$artifactBaseName.intoto.jsonl"
$provenance = [ordered]@{
    _type = 'https://in-toto.io/Statement/v1'
    subject = $subjects
    predicateType = 'https://slsa.dev/provenance/v1'
    predicate = [ordered]@{
        buildDefinition = [ordered]@{
            buildType = 'https://sharpts.dev/buildtypes/gui-msix/v1'
            externalParameters = [ordered]@{ packageIdentity = $PackageIdentity; version = $Version; architecture = $Architecture; signed = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint) }
            resolvedDependencies = @([ordered]@{ uri = "git+$RepositoryUrl"; digest = [ordered]@{ gitCommit = $SourceCommit } })
        }
        runDetails = [ordered]@{
            builder = [ordered]@{ id = 'https://github.com/nickna/SharpTS/.github/workflows/windows-gui-distribution.yml' }
            metadata = [ordered]@{ invocationId = $BuildInvocationId; startedOn = $created; finishedOn = [DateTimeOffset]::UtcNow.ToString('O') }
        }
    }
}
[IO.File]::WriteAllText($provenancePath, ($provenance | ConvertTo-Json -Depth 10 -Compress) + "`n", [Text.UTF8Encoding]::new($false))

$checksumsPath = Join-Path $outputPath 'SHA256SUMS'
$evidenceFiles = @(Get-ChildItem -LiteralPath $outputPath -File |
    Where-Object Name -notin @('SHA256SUMS', $outputMarkerName) |
    Sort-Object Name)
$checksumLines = @($evidenceFiles | ForEach-Object { "$(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $($_.Name)" })
[IO.File]::WriteAllLines($checksumsPath, $checksumLines, [Text.UTF8Encoding]::new($false))

if (-not $StageOnly -and -not $KeepStaging) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}

[pscustomobject]@{
    Msix = if (Test-Path -LiteralPath $msixPath) { $msixPath } else { $null }
    AppInstaller = $appInstallerPath
    Sbom = $spdxPath
    Provenance = $provenancePath
    Checksums = $checksumsPath
    Staging = if (Test-Path -LiteralPath $stagingPath) { $stagingPath } else { $null }
}
