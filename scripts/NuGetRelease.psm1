Set-StrictMode -Version Latest

function Get-NuGetReleaseManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "NuGet release manifest not found: $Path"
    }

    $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Unsupported NuGet release manifest schema version '$($manifest.schemaVersion)'."
    }

    $packageIds = @($manifest.packages | ForEach-Object { $_.id })
    if ($packageIds.Count -eq 0 -or $packageIds -contains $null -or $packageIds -contains '') {
        throw 'The NuGet release manifest must contain at least one package ID.'
    }

    $duplicates = @($packageIds | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicates.Count -gt 0) {
        throw "Duplicate package IDs in NuGet release manifest: $($duplicates -join ', ')"
    }

    foreach ($package in @($manifest.packages)) {
        if ($package.PSObject.Properties.Name -contains 'publish' -and
            $package.publish -eq $false -and
            [string]::IsNullOrWhiteSpace([string]$package.version)) {
            throw "Non-published package '$($package.id)' must define its fixed preview version."
        }
    }

    if ([string]::IsNullOrWhiteSpace($manifest.documentedSdkVersion)) {
        throw 'The NuGet release manifest must define documentedSdkVersion.'
    }

    return $manifest
}

function Get-NuGetManifestPackageVersion {
    param($Package, [string] $ReleaseVersion)

    if ($Package.PSObject.Properties.Name -contains 'version' -and
        -not [string]::IsNullOrWhiteSpace([string]$Package.version)) {
        return [string]$Package.version
    }

    return $ReleaseVersion
}

function Get-PublishableNuGetManifestPackages {
    param($Manifest)

    return @($Manifest.packages | Where-Object {
        -not ($_.PSObject.Properties.Name -contains 'publish' -and $_.publish -eq $false)
    })
}

function Get-NuGetPackageVersions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $PackageId,
        [Parameter(Mandatory)][string] $FlatContainerBaseUri
    )

    $baseUri = $FlatContainerBaseUri.TrimEnd('/')
    $uri = "$baseUri/$($PackageId.ToLowerInvariant())/index.json"
    $response = Invoke-RestMethod -Uri $uri -Method Get
    return @($response.versions)
}

function Get-DocumentedSdkVersionErrors {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][scriptblock] $FetchPackageVersions,
        [Parameter(Mandatory)][string] $FlatContainerBaseUri
    )

    $errors = [System.Collections.Generic.List[string]]::new()
    $expectedVersion = [string]$Manifest.documentedSdkVersion
    $versionPattern = 'SharpTS\.Sdk(?:/|"\s*:\s*")(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)'

    foreach ($relativePath in @($Manifest.documentationFiles)) {
        $path = Join-Path $RepositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $errors.Add("Documentation file not found: $relativePath")
            continue
        }

        $matches = [regex]::Matches((Get-Content -LiteralPath $path -Raw), $versionPattern)
        if ($matches.Count -eq 0) {
            $errors.Add("No concrete SharpTS.Sdk version found in $relativePath")
            continue
        }

        foreach ($match in $matches) {
            $actualVersion = $match.Groups['version'].Value
            if ($actualVersion -ne $expectedVersion) {
                $errors.Add("$relativePath references SharpTS.Sdk/$actualVersion; expected $expectedVersion")
            }
        }
    }

    try {
        $publishedVersions = @(& $FetchPackageVersions 'SharpTS.Sdk' $FlatContainerBaseUri)
        if ($publishedVersions -notcontains $expectedVersion) {
            $errors.Add("Documented SharpTS.Sdk version $expectedVersion is not published on NuGet")
        }
    }
    catch {
        $errors.Add("Could not verify documented SharpTS.Sdk version $expectedVersion on NuGet: $($_.Exception.Message)")
    }

    return $errors.ToArray()
}

function Assert-NuGetReleasePreflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)][string] $PackageDirectory,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [string] $FlatContainerBaseUri = 'https://api.nuget.org/v3-flatcontainer',
        [scriptblock] $FetchPackageVersions
    )

    if ($null -eq $FetchPackageVersions) {
        $FetchPackageVersions = { param($PackageId, $BaseUri) Get-NuGetPackageVersions -PackageId $PackageId -FlatContainerBaseUri $BaseUri }
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($package in @($Manifest.packages)) {
        $packageId = [string]$package.id
        $packageVersion = Get-NuGetManifestPackageVersion -Package $package -ReleaseVersion $Version
        $packagePath = Join-Path $PackageDirectory "$packageId.$packageVersion.nupkg"
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            $errors.Add("Expected package artifact not found: $packagePath")
        }

        try {
            $publishedVersions = @(& $FetchPackageVersions $packageId $FlatContainerBaseUri)
            if ($publishedVersions.Count -eq 0) {
                $errors.Add("Package ID '$packageId' is not registered on NuGet; onboard it and validate API-key scope before tagging a release")
            }
        }
        catch {
            $errors.Add("Package ID '$packageId' is not registered or NuGet could not be queried: $($_.Exception.Message)")
        }
    }

    foreach ($documentationError in @(Get-DocumentedSdkVersionErrors `
        -Manifest $Manifest `
        -RepositoryRoot $RepositoryRoot `
        -FetchPackageVersions $FetchPackageVersions `
        -FlatContainerBaseUri $FlatContainerBaseUri)) {
        $errors.Add($documentationError)
    }

    if ($errors.Count -gt 0) {
        throw "NuGet release preflight failed:`n - $($errors -join "`n - ")"
    }

    Write-Host "NuGet release preflight passed for version $Version."
}

function Publish-NuGetPackages {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Manifest,
        [Parameter(Mandatory)][string] $PackageDirectory,
        [Parameter(Mandatory)][string] $Version,
        [Parameter(Mandatory)][string] $ApiKey,
        [string] $NuGetSource = 'https://api.nuget.org/v3/index.json',
        [string] $FlatContainerBaseUri = 'https://api.nuget.org/v3-flatcontainer',
        [ValidateRange(1, 100)][int] $VerificationAttempts = 12,
        [ValidateRange(0, 300)][int] $VerificationDelaySeconds = 10,
        [scriptblock] $PushPackage,
        [scriptblock] $FetchPackageVersions,
        [scriptblock] $Sleep
    )

    if ($null -eq $PushPackage) {
        $PushPackage = {
            param($PackagePath, $PackageId, $PackageVersion, $Key, $Source)
            & dotnet nuget push $PackagePath --api-key $Key --source $Source --skip-duplicate
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet nuget push exited with code $LASTEXITCODE"
            }
        }
    }
    if ($null -eq $FetchPackageVersions) {
        $FetchPackageVersions = { param($PackageId, $BaseUri) Get-NuGetPackageVersions -PackageId $PackageId -FlatContainerBaseUri $BaseUri }
    }
    if ($null -eq $Sleep) {
        $Sleep = { param($Seconds) Start-Sleep -Seconds $Seconds }
    }

    $pushFailures = [System.Collections.Generic.List[string]]::new()
    $publishablePackages = @(Get-PublishableNuGetManifestPackages -Manifest $Manifest)
    foreach ($package in $publishablePackages) {
        $packageId = [string]$package.id
        $packagePath = Join-Path $PackageDirectory "$packageId.$Version.nupkg"
        Write-Host "::group::Push $packageId $Version"
        try {
            & $PushPackage $packagePath $packageId $Version $ApiKey $NuGetSource
            Write-Host "PUSH SUCCEEDED: $packageId $Version"
        }
        catch {
            $message = $_.Exception.Message
            $pushFailures.Add("$packageId ${Version}: $message")
            Write-Warning "PUSH FAILED: $packageId ${Version}: $message"
        }
        finally {
            Write-Host '::endgroup::'
        }
    }

    $inventory = @{}
    for ($attempt = 1; $attempt -le $VerificationAttempts; $attempt++) {
        $missing = [System.Collections.Generic.List[string]]::new()
        foreach ($package in $publishablePackages) {
            $packageId = [string]$package.id
            try {
                $versions = @(& $FetchPackageVersions $packageId $FlatContainerBaseUri)
                $isPublished = $versions -contains $Version
                $inventory[$packageId] = if ($isPublished) { 'published' } else { 'missing' }
                if (-not $isPublished) {
                    $missing.Add($packageId)
                }
            }
            catch {
                $inventory[$packageId] = "query failed: $($_.Exception.Message)"
                $missing.Add($packageId)
            }
        }

        if ($missing.Count -eq 0) {
            break
        }

        Write-Host "NuGet verification attempt $attempt/$VerificationAttempts missing: $($missing -join ', ')"
        if ($attempt -lt $VerificationAttempts) {
            & $Sleep $VerificationDelaySeconds
        }
    }

    Write-Host "NuGet inventory for version ${Version}:"
    foreach ($package in $publishablePackages) {
        $packageId = [string]$package.id
        Write-Host " - $packageId`: $($inventory[$packageId])"
    }

    $inventoryFailures = @($publishablePackages | Where-Object { $inventory[[string]$_.id] -ne 'published' } | ForEach-Object { [string]$_.id })
    $failureMessages = [System.Collections.Generic.List[string]]::new()
    foreach ($failure in $pushFailures) { $failureMessages.Add("Push failed: $failure") }
    foreach ($packageId in $inventoryFailures) { $failureMessages.Add("Version $Version is not visible for $packageId") }

    if ($failureMessages.Count -gt 0) {
        throw "NuGet release did not complete:`n - $($failureMessages -join "`n - ")"
    }

    Write-Host "All expected NuGet packages expose version $Version."
}

Export-ModuleMember -Function @(
    'Get-NuGetReleaseManifest',
    'Get-NuGetPackageVersions',
    'Get-DocumentedSdkVersionErrors',
    'Assert-NuGetReleasePreflight',
    'Publish-NuGetPackages'
)
