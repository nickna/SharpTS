[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NuGetRelease.psm1') -Force

$script:passed = 0
$script:failed = 0
$releaseVersion = '2.0.0'
$packageIds = @('SharpTS.LanguageServer', 'SharpTS.DebugAdapter', 'SharpTS.Hosting', 'SharpTS.Sdk', 'SharpTS', 'SharpTS.Gui.Sdk')

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

function Remove-TestFixture($Fixture) {
    $root = [IO.Path]::GetFullPath($Fixture.Root)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($root) -cne $temporaryRoot -or
        [IO.Path]::GetFileName($root) -notmatch '^sharpts-nuget-release-[a-f0-9]{32}$') {
        throw "Refusing to remove an unexpected test fixture: $root"
    }
    Remove-Item -LiteralPath $root -Recurse -Force
}

Invoke-Test 'artifact action accepts a complete unregistered fixture without feed or credentials' {
    $fixture = New-TestFixture
    try {
        $state = [pscustomobject]@{ Fetches = 0; Pushes = 0 }
        $fetch = { param($Id, $Uri) $state.Fetches++; @() }.GetNewClosure()
        $push = { param($Path, $Id, $Version, $Key, $Source) $state.Pushes++ }.GetNewClosure()
        Invoke-NuGetRelease -Action ValidateArtifacts -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory `
            -Version $releaseVersion -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch -PushPackage $push
        Assert-True ($state.Fetches -eq 0 -and $state.Pushes -eq 0) 'Artifact validation crossed the registration or publication boundary.'
        # Exercise the real CLI action too: this endpoint cannot serve a package index.
        & (Join-Path $PSScriptRoot 'nuget-release.ps1') -Action ValidateArtifacts -Version $releaseVersion `
            -PackageDirectory $fixture.PackageDirectory -ManifestPath $fixture.ManifestPath -FlatContainerBaseUri 'http://127.0.0.1:1/unused'
    }
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'preflight and publish reject unregistered or unavailable IDs before every push' {
    $fixture = New-TestFixture
    try {
        foreach ($releaseAction in @('Preflight', 'Publish')) {
            foreach ($unavailable in @($false, $true)) {
                $state = [pscustomobject]@{ Fetches = 0; Pushes = 0 }
                $fetch = {
                    param($Id, $Uri)
                    $state.Fetches++
                    if ($Id -eq 'SharpTS.DebugAdapter') {
                        if ($unavailable) { throw 'fixture feed unavailable' }
                        return @()
                    }
                    @('1.0.7')
                }.GetNewClosure()
                $push = { param($Path, $Id, $Version, $Key, $Source) $state.Pushes++ }.GetNewClosure()
                Assert-ThrowsContaining {
                    Invoke-NuGetRelease -Action $releaseAction -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory `
                        -Version $releaseVersion -RepositoryRoot $fixture.Root -ApiKey key -FetchPackageVersions $fetch -PushPackage $push
                } "Package ID 'SharpTS.DebugAdapter' is not registered"
                Assert-True ($state.Fetches -eq $packageIds.Count) 'Registration did not inspect the complete package set.'
                Assert-True ($state.Pushes -eq 0) "$releaseAction attempted a push before successful registration."
            }
        }
    }
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'every action rejects invalid versions and package artifacts before registration or push' {
    foreach ($releaseAction in @('ValidateArtifacts', 'Preflight', 'Publish')) {
        foreach ($invalid in @('release-version', 'missing-artifact', 'unexpected-artifact', 'embedded-id', 'embedded-version')) {
            $fixture = New-TestFixture
            try {
                $version = $releaseVersion
                $path = Join-Path $fixture.PackageDirectory "SharpTS.DebugAdapter.$releaseVersion.nupkg"
                switch ($invalid) {
                    'release-version' { $version = '01.0.0'; $expected = 'Invalid release version' }
                    'missing-artifact' { Remove-Item -LiteralPath $path; $expected = 'Expected package artifact not found' }
                    'unexpected-artifact' {
                        New-TestPackage (Join-Path $fixture.PackageDirectory "Unexpected.$releaseVersion.nupkg") 'Unexpected' $releaseVersion
                        $expected = 'Unexpected package artifact found'
                    }
                    'embedded-id' { New-TestPackage $path 'Wrong.DebugAdapter' $releaseVersion; $expected = "contains ID 'Wrong.DebugAdapter'" }
                    'embedded-version' { New-TestPackage $path 'SharpTS.DebugAdapter' '2.0.1'; $expected = "contains version '2.0.1'" }
                }
                $state = [pscustomobject]@{ Fetches = 0; Pushes = 0 }
                $fetch = { param($Id, $Uri) $state.Fetches++; @('1.0.7') }.GetNewClosure()
                $push = { param($Path, $Id, $Version, $Key, $Source) $state.Pushes++ }.GetNewClosure()
                Assert-ThrowsContaining {
                    Invoke-NuGetRelease -Action $releaseAction -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory `
                        -Version $version -RepositoryRoot $fixture.Root -ApiKey key -FetchPackageVersions $fetch -PushPackage $push
                } $expected
                Assert-True ($state.Fetches -eq 0 -and $state.Pushes -eq 0) "$releaseAction crossed a remote boundary with $invalid."
            }
            finally { Remove-TestFixture $fixture }
        }
    }
}

Invoke-Test 'publish requires credentials after successful artifact and registration checks' {
    $fixture = New-TestFixture
    try {
        $state = [pscustomobject]@{ Fetches = 0; Pushes = 0 }
        $fetch = { param($Id, $Uri) $state.Fetches++; @('1.0.7') }.GetNewClosure()
        $push = { param($Path, $Id, $Version, $Key, $Source) $state.Pushes++ }.GetNewClosure()
        Assert-ThrowsContaining {
            Invoke-NuGetRelease -Action Publish -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory `
                -Version $releaseVersion -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch -PushPackage $push
        } 'NUGET_API_KEY must be set'
        Assert-True ($state.Fetches -eq $packageIds.Count -and $state.Pushes -eq 0) 'Missing credentials bypassed preflight or attempted a push.'
    }
    finally { Remove-TestFixture $fixture }
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
    finally { Remove-TestFixture $fixture }
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
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'preflight requires all six tag-versioned files' {
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
    finally { Remove-TestFixture $fixture }
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
    finally { Remove-TestFixture $fixture }
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
    finally { Remove-TestFixture $fixture }
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
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'publication uses one version and all six filenames' {
    $fixture = New-TestFixture
    try {
        $pushes = [Collections.Generic.List[object]]::new()
        $push = {
            param($Path, $Id, $Version, $Key, $Source)
            $pushes.Add([pscustomobject]@{
                File = [IO.Path]::GetFileName($Path)
                Id = $Id
                Version = $Version
                Key = $Key
                Source = $Source
            })
        }.GetNewClosure()
        $source = 'https://packages.example.test/v3/index.json'
        $versions = New-VersionMap
        $fetch = { param($Id, $Uri) @($versions[$Id]) }.GetNewClosure()
        Invoke-NuGetRelease -Action Publish -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory `
            -Version $releaseVersion -RepositoryRoot $fixture.Root -ApiKey key -NuGetSource $source -FetchPackageVersions $fetch -PushPackage $push
        $expected = @($packageIds | ForEach-Object { "$($_).$releaseVersion.nupkg" } | Sort-Object)
        Assert-True ($pushes.Count -eq $packageIds.Count) 'Expected each package to be pushed once.'
        Assert-True (-not (Compare-Object $expected @($pushes.File | Sort-Object) -SyncWindow 0)) 'Expected filenames differ.'
        $versions = @($pushes.Version | Select-Object -Unique)
        $keys = @($pushes.Key | Select-Object -Unique)
        $sources = @($pushes.Source | Select-Object -Unique)
        Assert-True ($versions.Count -eq 1 -and $versions[0] -ceq $releaseVersion) 'Pushes did not share the release version.'
        Assert-True ($keys.Count -eq 1 -and $keys[0] -ceq 'key') 'Pushes did not share the API key.'
        Assert-True ($sources.Count -eq 1 -and $sources[0] -ceq $source) 'Pushes did not share the NuGet source.'
    }
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'publication attempts every package after a push failure' {
    $fixture = New-TestFixture
    try {
        $attempts = [Collections.Generic.List[string]]::new()
        $push = {
            param($Path, $Id, $Version, $Key, $Source)
            $attempts.Add($Id)
            if ($Id -eq 'SharpTS.Hosting') { throw 'permission failure' }
        }.GetNewClosure()
        Assert-ThrowsContaining {
            Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -PushPackage $push
        } 'SharpTS.Hosting 2.0.0: permission failure'
        Assert-True ($attempts.Count -eq $packageIds.Count) 'A failed push stopped the package inventory early.'
        Assert-True (-not (Compare-Object ($packageIds | Sort-Object) ($attempts | Sort-Object) -SyncWindow 0)) 'Not every package was attempted.'
    }
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'publication reports multiple push failures together' {
    $fixture = New-TestFixture
    try {
        $push = {
            param($Path, $Id, $Version, $Key, $Source)
            if ($Id -eq 'SharpTS.LanguageServer') { throw 'first failure' }
            if ($Id -eq 'SharpTS.Gui.Sdk') { throw 'second failure' }
        }.GetNewClosure()
        $message = $null
        try { Publish-NuGetPackages $fixture.Manifest $fixture.PackageDirectory $releaseVersion key -PushPackage $push }
        catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'Expected publication to fail.'
        Assert-True ($message -like '*SharpTS.LanguageServer 2.0.0: first failure*') 'First push failure was omitted.'
        Assert-True ($message -like '*SharpTS.Gui.Sdk 2.0.0: second failure*') 'Second push failure was omitted.'
    }
    finally { Remove-TestFixture $fixture }
}

Invoke-Test 'publication contains no availability polling or sleeping' {
    $command = Get-Command Publish-NuGetPackages
    foreach ($removedParameter in @('FlatContainerBaseUri', 'VerificationAttempts', 'VerificationDelaySeconds', 'FetchPackageVersions', 'Sleep')) {
        Assert-True (-not $command.Parameters.ContainsKey($removedParameter)) "Publish-NuGetPackages still exposes $removedParameter."
    }
    $functionSource = $command.ScriptBlock.ToString()
    Assert-True (-not $functionSource.Contains('Get-NuGetPackageVersions', [StringComparison]::Ordinal)) 'Publication still queries package availability.'
    Assert-True (-not $functionSource.Contains('Start-Sleep', [StringComparison]::Ordinal)) 'Publication still sleeps for package availability.'
    Assert-True ($functionSource.Contains('--skip-duplicate', [StringComparison]::Ordinal)) 'Publication reruns must use --skip-duplicate.'
}

Write-Host "NuGet release helper tests: $script:passed passed, $script:failed failed."
if ($script:failed) { exit 1 }
