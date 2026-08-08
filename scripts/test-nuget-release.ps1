[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NuGetRelease.psm1') -Force

$script:passed = 0
$script:failed = 0

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsContaining {
    param([scriptblock] $Action, [string] $ExpectedText)
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedText*") {
            throw "Expected an error containing '$ExpectedText', got: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected an error containing '$ExpectedText', but no error was thrown."
}

function Invoke-Test {
    param([string] $Name, [scriptblock] $Test)
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

function New-TestFixture {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) "sharpts-nuget-release-$([guid]::NewGuid().ToString('N'))"
    $packageDirectory = Join-Path $root 'nupkg'
    $documentationDirectory = Join-Path $root 'docs'
    New-Item -ItemType Directory -Path $packageDirectory, $documentationDirectory | Out-Null

    $publishedPackageIds = @('SharpTS.LanguageServer', 'SharpTS.Hosting', 'SharpTS.Sdk', 'SharpTS')
    $previewPackageId = 'SharpTS.Gui.Sdk'
    $packageIds = @($publishedPackageIds) + $previewPackageId
    $manifestData = [ordered]@{
        schemaVersion = 1
        documentedSdkVersion = '1.0.7'
        documentationFiles = @('README.md', 'docs/sdk.md')
        packages = @(
            @($publishedPackageIds | ForEach-Object { [ordered]@{ id = $_ } })
            [ordered]@{ id = $previewPackageId; version = '0.1.0-preview.1'; publish = $false }
        )
    }
    $manifestPath = Join-Path $root 'nuget-packages.json'
    $manifestData | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
    '<Project Sdk="SharpTS.Sdk/1.0.7">' | Set-Content -LiteralPath (Join-Path $root 'README.md')
    '{ "msbuild-sdks": { "SharpTS.Sdk": "1.0.7" } }' | Set-Content -LiteralPath (Join-Path $root 'docs/sdk.md')
    foreach ($packageId in $publishedPackageIds) {
        'test package' | Set-Content -LiteralPath (Join-Path $packageDirectory "$packageId.2.0.0.nupkg")
    }
    'test package' | Set-Content -LiteralPath (Join-Path $packageDirectory "$previewPackageId.0.1.0-preview.1.nupkg")

    return [pscustomobject]@{
        Root = $root
        PackageDirectory = $packageDirectory
        Manifest = Get-NuGetReleaseManifest -Path $manifestPath
        PackageIds = $packageIds
        PublishedPackageIds = $publishedPackageIds
        PreviewPackageId = $previewPackageId
    }
}

function New-PublishedVersionMap {
    param([string[]] $PackageIds)
    $result = @{}
    foreach ($packageId in $PackageIds) {
        $result[$packageId] = [System.Collections.ArrayList]@('1.0.7')
    }
    return $result
}

Invoke-Test 'preflight accepts complete registered package set and verified documentation' {
    $fixture = New-TestFixture
    try {
        $published = New-PublishedVersionMap $fixture.PackageIds
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'preflight rejects a missing package artifact' {
    $fixture = New-TestFixture
    try {
        Remove-Item -LiteralPath (Join-Path $fixture.PackageDirectory 'SharpTS.Hosting.2.0.0.nupkg')
        $published = New-PublishedVersionMap $fixture.PackageIds
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
        } 'Expected package artifact not found'
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'preflight requires the fixed preview artifact' {
    $fixture = New-TestFixture
    try {
        Remove-Item -LiteralPath (Join-Path $fixture.PackageDirectory 'SharpTS.Gui.Sdk.0.1.0-preview.1.nupkg')
        $published = New-PublishedVersionMap $fixture.PackageIds
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
        } 'SharpTS.Gui.Sdk.0.1.0-preview.1.nupkg'
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'preflight gates an unregistered package ID before publication' {
    $fixture = New-TestFixture
    try {
        $published = New-PublishedVersionMap $fixture.PackageIds
        $published['SharpTS.LanguageServer'].Clear()
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
        } 'onboard it and validate API-key scope'
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'preflight gates an unregistered non-published preview ID' {
    $fixture = New-TestFixture
    try {
        $published = New-PublishedVersionMap $fixture.PackageIds
        $published[$fixture.PreviewPackageId].Clear()
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
        } "Package ID '$($fixture.PreviewPackageId)' is not registered"
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'preflight rejects inconsistent documentation versions' {
    $fixture = New-TestFixture
    try {
        '<Project Sdk="SharpTS.Sdk/9.9.9">' | Set-Content -LiteralPath (Join-Path $fixture.Root 'README.md')
        $published = New-PublishedVersionMap $fixture.PackageIds
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
        } 'expected 1.0.7'
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'preflight rejects a documented SDK version absent from NuGet' {
    $fixture = New-TestFixture
    try {
        $published = New-PublishedVersionMap $fixture.PackageIds
        $published['SharpTS.Sdk'].Clear()
        [void]$published['SharpTS.Sdk'].Add('1.0.6')
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        Assert-ThrowsContaining {
            Assert-NuGetReleasePreflight -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -RepositoryRoot $fixture.Root -FetchPackageVersions $fetch
        } 'Documented SharpTS.Sdk version 1.0.7 is not published on NuGet'
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'publication retries until the complete inventory is visible' {
    $fixture = New-TestFixture
    try {
        $published = New-PublishedVersionMap $fixture.PackageIds
        $fetchCounts = @{}
        $sleepCount = [pscustomobject]@{ Value = 0 }
        $push = { param($PackagePath, $PackageId, $PackageVersion, $ApiKey, $Source) }.GetNewClosure()
        $fetch = {
            param($PackageId, $BaseUri)
            $fetchCounts[$PackageId] = 1 + ($fetchCounts[$PackageId] ?? 0)
            if ($fetchCounts[$PackageId] -ge 2 -and $published[$PackageId] -notcontains '2.0.0') {
                [void]$published[$PackageId].Add('2.0.0')
            }
            @($published[$PackageId])
        }.GetNewClosure()
        $sleep = { param($Seconds) $sleepCount.Value++ }.GetNewClosure()

        Publish-NuGetPackages -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -ApiKey 'test-key' -VerificationAttempts 2 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch -Sleep $sleep

        Assert-True ($sleepCount.Value -eq 1) 'Publication did not perform exactly one bounded retry.'
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Invoke-Test 'partial publication reports every package and converges on rerun' {
    $fixture = New-TestFixture
    try {
        $published = New-PublishedVersionMap $fixture.PackageIds
        $pushAttempts = [System.Collections.Generic.List[string]]::new()
        $firstRun = [pscustomobject]@{ Value = $true }
        $push = {
            param($PackagePath, $PackageId, $PackageVersion, $ApiKey, $Source)
            $pushAttempts.Add($PackageId)
            if ($firstRun.Value -and $pushAttempts.Count -eq 2) {
                throw 'simulated permission failure'
            }
            if ($published[$PackageId] -notcontains $PackageVersion) {
                [void]$published[$PackageId].Add($PackageVersion)
            }
        }.GetNewClosure()
        $fetch = { param($PackageId, $BaseUri) @($published[$PackageId]) }.GetNewClosure()
        $sleep = { param($Seconds) }.GetNewClosure()

        Assert-ThrowsContaining {
            Publish-NuGetPackages -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -ApiKey 'test-key' -VerificationAttempts 1 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch -Sleep $sleep
        } 'simulated permission failure'

        Assert-True ($pushAttempts.Count -eq 4) 'The first run did not attempt every publishable package after a failure.'
        Assert-True ($pushAttempts -notcontains $fixture.PreviewPackageId) 'The preview-only package was unexpectedly pushed.'
        Assert-True ($published['SharpTS.LanguageServer'] -contains '2.0.0') 'The first package was not published.'
        Assert-True ($published['SharpTS.Hosting'] -notcontains '2.0.0') 'The simulated failed package was unexpectedly published.'

        $firstRun.Value = $false
        Publish-NuGetPackages -Manifest $fixture.Manifest -PackageDirectory $fixture.PackageDirectory -Version '2.0.0' -ApiKey 'test-key' -VerificationAttempts 1 -VerificationDelaySeconds 0 -PushPackage $push -FetchPackageVersions $fetch -Sleep $sleep

        Assert-True ($pushAttempts.Count -eq 8) 'The rerun did not attempt every publishable package deterministically.'
        Assert-True ($pushAttempts -notcontains $fixture.PreviewPackageId) 'The preview-only package was unexpectedly pushed on rerun.'
        foreach ($packageId in $fixture.PublishedPackageIds) {
            Assert-True ($published[$packageId] -contains '2.0.0') "$packageId did not converge to version 2.0.0."
        }
    }
    finally {
        Remove-Item -LiteralPath $fixture.Root -Recurse -Force
    }
}

Write-Host "NuGet release helper tests: $script:passed passed, $script:failed failed."
if ($script:failed -gt 0) {
    exit 1
}
