[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NuGetRelease.psm1') -Force

$script:passed = 0
$script:failed = 0
$releaseVersion = '2.0.0'
$packageIds = @('SharpTS.LanguageServer', 'SharpTS.Hosting', 'SharpTS.Sdk', 'SharpTS', 'SharpTS.Gui.Sdk')

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsContaining([scriptblock]$Action, [string]$ExpectedText) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedText*") {
            throw "Expected '$ExpectedText', got: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected '$ExpectedText'."
}

function Invoke-Test([string]$Name, [scriptblock]$Test) {
    try {
        & $Test
        $script:passed++
        Write-Host "PASS: $Name"
    }
    catch {
        $script:failed++
        Write-Error "FAIL: $Name`n$($_.Exception.Message)" -ErrorAction Continue
    }
}

function New-TestPackage([string]$Path, [string]$Id, [string]$Version) {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry("$Id.nuspec")
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try {
            $writer.Write("<?xml version=`"1.0`"?><package><metadata><id>$Id</id><version>$Version</version></metadata></package>")
        }
        finally { $writer.Dispose() }
    }
    finally { $archive.Dispose() }
}

function New-TestFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) "sharpts-nuget-release-$([guid]::NewGuid().ToString('N'))"
    $directory = Join-Path $root 'nupkg'
    New-Item -ItemType Directory $directory | Out-Null
    $manifestPath = Join-Path $root 'nuget-packages.json'
    [ordered]@{
        schemaVersion = 2
        packages = @($packageIds | ForEach-Object { [ordered]@{ id = $_ } })
    } | ConvertTo-Json -Depth 5 | Set-Content $manifestPath
    foreach ($id in $packageIds) {
        New-TestPackage (Join-Path $directory "$id.$releaseVersion.nupkg") $id $releaseVersion
    }
    [pscustomobject]@{
        Root = $root
        PackageDirectory = $directory
        ManifestPath = $manifestPath
        Manifest = Get-NuGetReleaseManifest $manifestPath
    }
}

function New-VersionMap {
    $map = @{}
    foreach ($id in $packageIds) { $map[$id] = [Collections.ArrayList]@('1.0.7') }
    $map
}

Invoke-Test 'schema v2 rejects per-package release overrides' {
    $fixture = New-TestFixture
    try {
        foreach ($field in @('version', 'publish', 'sha256')) {
            $data = [ordered]@{
                schemaVersion = 2
                packages = @($packageIds | ForEach-Object { [ordered]@{ id = $_ } })
            }
            $data.packages[0][$field] = if ($field -eq 'publish') { $false } else { 'override' }
            $data | ConvertTo-Json -Depth 5 | Set-Content $fixture.ManifestPath
            Assert-ThrowsContaining { Get-NuGetReleaseManifest $fixture.ManifestPath } "unsupported field(s): $field"
        }
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'preflight rejects malformed release versions' {
    $fixture = New-TestFixture
    try {
        $versions = New-VersionMap
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        foreach ($invalidVersion in @('01.0.0', '1.0.0-bad..version', '1.0.0-01')) {
            Assert-ThrowsContaining {
                Assert-NuGetReleasePreflight $fixture.Manifest $fixture.PackageDirectory $invalidVersion $fixture.Root -FetchPackageVersions $fetch
            } "Invalid release version '$invalidVersion'"
        }
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'preflight requires all five tag-versioned files' {
    $fixture = New-TestFixture
    try {
        $versions = New-VersionMap
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Assert-NuGetReleasePreflight $fixture.Manifest $fixture.PackageDirectory $releaseVersion $fixture.Root -FetchPackageVersions $fetch
        Remove-Item (Join-Path $fixture.PackageDirectory "SharpTS.Gui.Sdk.$releaseVersion.nupkg")
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight $fixture.Manifest $fixture.PackageDirectory $releaseVersion $fixture.Root -FetchPackageVersions $fetch
        } "SharpTS.Gui.Sdk.$releaseVersion.nupkg"
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'preflight rejects an embedded package ID mismatch' {
    $fixture = New-TestFixture
    try {
        $path = Join-Path $fixture.PackageDirectory "SharpTS.Gui.Sdk.$releaseVersion.nupkg"
        New-TestPackage $path 'Wrong.Gui.Sdk' $releaseVersion
        $versions = New-VersionMap
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight $fixture.Manifest $fixture.PackageDirectory $releaseVersion $fixture.Root -FetchPackageVersions $fetch
        } "contains ID 'Wrong.Gui.Sdk'"
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'preflight rejects an embedded package version mismatch' {
    $fixture = New-TestFixture
    try {
        $path = Join-Path $fixture.PackageDirectory "SharpTS.Gui.Sdk.$releaseVersion.nupkg"
        New-TestPackage $path 'SharpTS.Gui.Sdk' '2.0.1'
        $versions = New-VersionMap
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight $fixture.Manifest $fixture.PackageDirectory $releaseVersion $fixture.Root -FetchPackageVersions $fetch
        } "contains version '2.0.1'"
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'preflight rejects unexpected package artifacts' {
    $fixture = New-TestFixture
    try {
        New-TestPackage (Join-Path $fixture.PackageDirectory "Unexpected.$releaseVersion.nupkg") 'Unexpected' $releaseVersion
        $versions = New-VersionMap
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight $fixture.Manifest $fixture.PackageDirectory $releaseVersion $fixture.Root -FetchPackageVersions $fetch
        } 'Unexpected package artifact found'
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'publication uses one version and all five filenames' {
    $fixture = New-TestFixture
    try {
        $versions = New-VersionMap
        $files = [Collections.Generic.List[string]]::new()
        $push = {
            param($Path, $Id, $Version, $Key, $Source)
            $files.Add([IO.Path]::GetFileName($Path))
            [void]$versions[$Id].Add($Version)
        }.GetNewClosure()
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -VerificationAttempts 1 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch
        $expected = @($packageIds | ForEach-Object { "$($_).$releaseVersion.nupkg" } | Sort-Object)
        Assert-True (-not (Compare-Object $expected @($files | Sort-Object) -SyncWindow 0)) 'Expected filenames differ.'
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'publication waits for delayed NuGet indexing' {
    $fixture = New-TestFixture
    try {
        $versions = New-VersionMap
        $counts = @{}
        $sleeps = [pscustomobject]@{ Value = 0 }
        $expected = $releaseVersion
        $push = { param($Path, $Id, $Version, $Key, $Source) }
        $fetch = {
            param($Id, $Uri)
            $counts[$Id] = 1 + ($counts[$Id] ?? 0)
            if ($counts[$Id] -ge 3 -and $versions[$Id] -notcontains $expected) { [void]$versions[$Id].Add($expected) }
            @($versions[$Id])
        }.GetNewClosure()
        $sleep = { param($Seconds) $sleeps.Value++ }.GetNewClosure()
        Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -VerificationAttempts 2 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch -Sleep $sleep
        Assert-True ($sleeps.Value -eq 1) 'Expected one indexing wait.'
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'push error is nonfatal after final inventory visibility' {
    $fixture = New-TestFixture
    try {
        $versions = New-VersionMap
        $push = {
            param($Path, $Id, $Version, $Key, $Source)
            [void]$versions[$Id].Add($Version)
            if ($Id -eq 'SharpTS.Gui.Sdk') { throw 'response lost' }
        }.GetNewClosure()
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -VerificationAttempts 1 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Invoke-Test 'partial publish rerun pushes only the missing version' {
    $fixture = New-TestFixture
    try {
        $versions = New-VersionMap
        $attempts = [Collections.Generic.List[string]]::new()
        $first = [pscustomobject]@{ Value = $true }
        $push = {
            param($Path, $Id, $Version, $Key, $Source)
            $attempts.Add($Id)
            if ($first.Value -and $Id -eq 'SharpTS.Hosting') { throw 'permission failure' }
            if ($versions[$Id] -notcontains $Version) { [void]$versions[$Id].Add($Version) }
        }.GetNewClosure()
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -VerificationAttempts 1 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch
        } 'permission failure'
        $first.Value = $false
        $before = $attempts.Count
        Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -VerificationAttempts 1 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch
        $rerun = @($attempts | Select-Object -Skip $before)
        Assert-True ($rerun.Count -eq 1 -and $rerun[0] -eq 'SharpTS.Hosting') 'Rerun did not skip visible versions.'
    }
    finally { Remove-Item $fixture.Root -Recurse -Force }
}

Write-Host "NuGet release helper tests: $script:passed passed, $script:failed failed."
if ($script:failed) { exit 1 }
