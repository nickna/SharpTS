[CmdletBinding()]
param(
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$typescriptRoot = Join-Path $repoRoot "external/typescript"
$typescriptLibRoot = Join-Path $typescriptRoot "lib"
$resourcesRoot = Join-Path $repoRoot "src/SharpTS/Modules/TypeScriptLibResources"
$providerPath = Join-Path $repoRoot "src/SharpTS/Modules/TypeScriptLibProvider.cs"
$manifestPath = Join-Path $resourcesRoot "SHA256SUMS"
$licenseSource = Join-Path $typescriptRoot "LICENSE.txt"
$licenseTarget = Join-Path $resourcesRoot "LICENSE.txt"

if (-not (Test-Path -LiteralPath (Join-Path $typescriptRoot "package.json"))) {
    throw "external/typescript is not initialized. Run 'git submodule update --init external/typescript'."
}

if (-not (Test-Path -LiteralPath $typescriptLibRoot)) {
    throw "The initialized external/typescript checkout does not contain its lib directory."
}

$package = Get-Content -Raw -LiteralPath (Join-Path $typescriptRoot "package.json") |
    ConvertFrom-Json
$version = [string]$package.version
$versionParts = $version.Split(".")
if ($versionParts.Count -ne 3 -or
    $versionParts.Where({ $_ -notmatch "^\d+$" }).Count -ne 0) {
    throw "Expected a stable three-part TypeScript version, found '$version'."
}

$safeTypeScriptRoot = $typescriptRoot.Replace("\", "/")
$commit = (& git -c "safe.directory=$safeTypeScriptRoot" -C $typescriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Could not read the external/typescript commit."
}

$tag = (& git -c "safe.directory=$safeTypeScriptRoot" -C $typescriptRoot describe --tags --exact-match HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $tag -ne "v$version") {
    throw "external/typescript must be checked out at tag v$version; found '$tag' ($commit)."
}

$sourceFiles = @(Get-ChildItem -LiteralPath $typescriptLibRoot -File -Filter "lib*.d.ts" |
    Sort-Object Name)
if ($sourceFiles.Count -eq 0) {
    throw "No lib*.d.ts files were found in '$typescriptLibRoot'."
}

$sourceNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($sourceFile in $sourceFiles) {
    if (-not $sourceNames.Add($sourceFile.Name)) {
        throw "Duplicate upstream library name '$($sourceFile.Name)'."
    }
}

function Get-HashLine([string]$Path, [string]$Name) {
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    return "$hash  $Name"
}

function Get-ExpectedManifest {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# TypeScript v$version ($commit)")
    $lines.Add("# SHA-256 hashes for the exact upstream files embedded in SharpTS.dll.")
    $lines.Add((Get-HashLine -Path $licenseSource -Name "LICENSE.txt"))
    foreach ($sourceFile in $sourceFiles) {
        $lines.Add((Get-HashLine -Path $sourceFile.FullName -Name $sourceFile.Name))
    }
    return [string]::Join("`n", $lines) + "`n"
}

if ($Update) {
    $resolvedResourcesRoot = [System.IO.Path]::GetFullPath($resourcesRoot)
    $resourcePrefix = $resolvedResourcesRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar

    foreach ($embeddedFile in Get-ChildItem -LiteralPath $resourcesRoot -File -Filter "lib*.d.ts") {
        if ($sourceNames.Contains($embeddedFile.Name)) {
            continue
        }

        $resolvedTarget = [System.IO.Path]::GetFullPath($embeddedFile.FullName)
        if (-not $resolvedTarget.StartsWith($resourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a file outside '$resolvedResourcesRoot': $resolvedTarget"
        }
        Remove-Item -LiteralPath $resolvedTarget
    }

    foreach ($sourceFile in $sourceFiles) {
        Copy-Item -LiteralPath $sourceFile.FullName -Destination (
            Join-Path $resourcesRoot $sourceFile.Name)
    }
    Copy-Item -LiteralPath $licenseSource -Destination $licenseTarget

    $providerText = [System.IO.File]::ReadAllText($providerPath)
    if ([regex]::Matches(
        $providerText,
        "Reads the TypeScript \d+\.\d+\.\d+").Count -ne 1 -or
        [regex]::Matches(
        $providerText,
        "CompilerVersion = new\(\d+,\s*\d+,\s*\d+\);").Count -ne 1) {
        throw "TypeScriptLibProvider.cs did not contain the expected version declarations."
    }
    $updatedProviderText = [regex]::Replace(
        $providerText,
        "Reads the TypeScript \d+\.\d+\.\d+",
        "Reads the TypeScript $version")
    $updatedProviderText = [regex]::Replace(
        $updatedProviderText,
        "CompilerVersion = new\(\d+,\s*\d+,\s*\d+\);",
        "CompilerVersion = new($($versionParts[0]), $($versionParts[1]), $($versionParts[2]));")
    [System.IO.File]::WriteAllText(
        $providerPath,
        $updatedProviderText,
        [System.Text.UTF8Encoding]::new($false))

    [System.IO.File]::WriteAllText(
        $manifestPath,
        (Get-ExpectedManifest),
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Synchronized $($sourceFiles.Count) TypeScript v$version libraries."
    Write-Host "Stage external/typescript and the generated files, then run this script without -Update."
    exit 0
}

$errors = [System.Collections.Generic.List[string]]::new()

$indexEntry = (& git -C $repoRoot ls-files --stage -- external/typescript)
if ($LASTEXITCODE -ne 0 -or $indexEntry -notmatch "^160000 ([0-9a-f]{40}) 0\s+external/typescript$") {
    $errors.Add("Could not read the external/typescript gitlink from the index.")
}
elseif ($Matches[1] -ne $commit) {
    $errors.Add(
        "The external/typescript gitlink is $($Matches[1]), but the initialized checkout is $commit.")
}

$providerText = [System.IO.File]::ReadAllText($providerPath)
$expectedVersionDeclaration =
    "CompilerVersion = new($($versionParts[0]), $($versionParts[1]), $($versionParts[2]));"
if (-not $providerText.Contains("Reads the TypeScript $version") -or
    -not $providerText.Contains($expectedVersionDeclaration)) {
    $errors.Add("TypeScriptLibProvider.CompilerVersion does not declare $version.")
}

$embeddedFiles = @(Get-ChildItem -LiteralPath $resourcesRoot -File -Filter "lib*.d.ts" |
    Sort-Object Name)

foreach ($sourceFile in $sourceFiles) {
    $target = Join-Path $resourcesRoot $sourceFile.Name
    if (-not (Test-Path -LiteralPath $target)) {
        $errors.Add("Missing embedded library: $($sourceFile.Name)")
        continue
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $sourceFile.FullName).Hash -ne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash) {
        $errors.Add("Embedded library differs from upstream: $($sourceFile.Name)")
    }
}

foreach ($embeddedFile in $embeddedFiles) {
    if (-not $sourceNames.Contains($embeddedFile.Name)) {
        $errors.Add("Obsolete embedded library: $($embeddedFile.Name)")
    }
}

if ((Get-FileHash -Algorithm SHA256 -LiteralPath $licenseSource).Hash -ne
    (Get-FileHash -Algorithm SHA256 -LiteralPath $licenseTarget).Hash) {
    $errors.Add("src/SharpTS/Modules/TypeScriptLibResources/LICENSE.txt differs from upstream.")
}

$expectedManifest = Get-ExpectedManifest
if (-not (Test-Path -LiteralPath $manifestPath) -or
    [System.IO.File]::ReadAllText($manifestPath) -ne $expectedManifest) {
    $errors.Add("src/SharpTS/Modules/TypeScriptLibResources/SHA256SUMS is stale.")
}

if ($errors.Count -ne 0) {
    throw "TypeScript library synchronization check failed:`n - " +
        [string]::Join("`n - ", $errors)
}

Write-Host (
    "TypeScript v$version synchronization verified: gitlink, compiler version, license, " +
    "$($sourceFiles.Count) libraries, and SHA-256 manifest.")
