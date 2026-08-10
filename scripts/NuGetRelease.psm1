Set-StrictMode -Version Latest

function Test-NuGetReleaseVersion {
    param([Parameter(Mandatory)][string] $Version)

    $match = [regex]::Match($Version, '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?$')
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

function Get-NuGetReleaseManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "NuGet release manifest not found: $Path"
    }

    $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 2) {
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
        $unsupportedFields = @($package.PSObject.Properties.Name | Where-Object { $_ -ne 'id' })
        if ($unsupportedFields.Count -gt 0) {
            throw "NuGet release package '$($package.id)' contains unsupported field(s): $($unsupportedFields -join ', '). Every package inherits the release version; per-package version, publish, and sha256 overrides are forbidden."
        }
    }

    return $manifest
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

function Get-NuGetPackageIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $Path)

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
        try {
            $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -match '^[^/\\]+\.nuspec$' })
            if ($nuspecEntries.Count -ne 1) {
                throw "expected exactly one root .nuspec, found $($nuspecEntries.Count)"
            }
            $reader = [IO.StreamReader]::new($nuspecEntries[0].Open())
            try { [xml]$nuspec = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    catch {
        throw "Invalid NuGet package '$Path': $($_.Exception.Message)"
    }

    $metadata = $nuspec.package.metadata
    $id = [string]$metadata.id
    $version = [string]$metadata.version
    if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($version)) {
        throw "Invalid NuGet package '$Path': the embedded .nuspec must contain metadata id and version."
    }
    return [pscustomobject]@{ Id = $id; Version = $version }
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

    if (-not (Test-NuGetReleaseVersion $Version)) {
        throw "Invalid release version '$Version'."
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $expectedFiles = @($Manifest.packages | ForEach-Object { "$($_.id).$Version.nupkg" })
    $actualFiles = @(
        Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -File -ErrorAction SilentlyContinue |
            ForEach-Object Name
    )
    foreach ($unexpectedFile in @($actualFiles | Where-Object { $_ -notin $expectedFiles })) {
        $errors.Add("Unexpected package artifact found: $(Join-Path $PackageDirectory $unexpectedFile)")
    }
    foreach ($package in @($Manifest.packages)) {
        $packageId = [string]$package.id
        $packagePath = Join-Path $PackageDirectory "$packageId.$Version.nupkg"
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            $errors.Add("Expected package artifact not found: $packagePath")
        }
        else {
            try {
                $identity = Get-NuGetPackageIdentity -Path $packagePath
                if ($identity.Id -cne $packageId) {
                    $errors.Add("Package artifact '$packagePath' contains ID '$($identity.Id)', expected '$packageId'")
                }
                if ($identity.Version -cne $Version) {
                    $errors.Add("Package artifact '$packagePath' contains version '$($identity.Version)', expected '$Version'")
                }
            }
            catch {
                $errors.Add($_.Exception.Message)
            }
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
        [ValidateRange(1, 100)][int] $VerificationAttempts = 30,
        [ValidateRange(0, 300)][int] $VerificationDelaySeconds = 20,
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

    $pushFailures = @{}
    $packages = @($Manifest.packages)
    foreach ($package in $packages) {
        $packageId = [string]$package.id
        $packagePath = Join-Path $PackageDirectory "$packageId.$Version.nupkg"
        $alreadyPublished = $false
        try { $alreadyPublished = @(& $FetchPackageVersions $packageId $FlatContainerBaseUri) -contains $Version }
        catch { Write-Warning "Could not check whether $packageId $Version is already visible before push: $($_.Exception.Message)" }
        if ($alreadyPublished) {
            Write-Host "SKIP PUSH: $packageId $Version is already visible on NuGet."
            continue
        }
        Write-Host "::group::Push $packageId $Version"
        try {
            & $PushPackage $packagePath $packageId $Version $ApiKey $NuGetSource
            Write-Host "PUSH SUCCEEDED: $packageId $Version"
        }
        catch {
            $pushFailures[$packageId] = $_.Exception.Message
            Write-Warning "PUSH FAILED: $packageId ${Version}: $($_.Exception.Message)"
        }
        finally {
            Write-Host '::endgroup::'
        }
    }

    $inventory = @{}
    for ($attempt = 1; $attempt -le $VerificationAttempts; $attempt++) {
        $missing = [System.Collections.Generic.List[string]]::new()
        foreach ($package in $packages) {
            $packageId = [string]$package.id
            try {
                $versions = @(& $FetchPackageVersions $packageId $FlatContainerBaseUri)
                $isPublished = $versions -contains $Version
                $inventory[$packageId] = if ($isPublished) { 'published' } else { 'missing' }
                if (-not $isPublished) {
                    $missing.Add("$packageId $Version")
                }
            }
            catch {
                $inventory[$packageId] = "query failed: $($_.Exception.Message)"
                $missing.Add("$packageId $Version")
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

    Write-Host 'NuGet inventory:'
    foreach ($package in $packages) {
        $packageId = [string]$package.id
        Write-Host " - $packageId $Version`: $($inventory[$packageId])"
    }

    $failureMessages = [System.Collections.Generic.List[string]]::new()
    foreach ($package in $packages) {
        $packageId = [string]$package.id
        if ($inventory[$packageId] -eq 'published') { continue }
        if ($pushFailures.ContainsKey($packageId)) {
            $failureMessages.Add("Push failed: $packageId ${Version}: $($pushFailures[$packageId])")
        }
        $failureMessages.Add("Version $Version is not visible for $packageId")
    }

    if ($failureMessages.Count -gt 0) {
        throw "NuGet release did not complete:`n - $($failureMessages -join "`n - ")"
    }

    Write-Host "All expected NuGet packages expose release version $Version."
}

Export-ModuleMember -Function @(
    'Get-NuGetReleaseManifest',
    'Get-NuGetPackageIdentity',
    'Get-NuGetPackageVersions',
    'Assert-NuGetReleasePreflight',
    'Publish-NuGetPackages'
)
