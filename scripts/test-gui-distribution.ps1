[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packager = Join-Path $PSScriptRoot 'package-gui-windows.ps1'
$collector = Join-Path $PSScriptRoot 'collect-gui-support-bundle.ps1'
$canonicalAssets = Join-Path (Split-Path -Parent $PSScriptRoot) 'distribution\windows\assets'
$script:passed = 0
$script:failed = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ThrowsContaining([scriptblock]$Action, [string]$ExpectedText) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedText*") {
            throw "Expected '$ExpectedText', got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected an error containing '$ExpectedText'."
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

function New-DistributionFixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) "sharpts-gui-distribution-$([Guid]::NewGuid().ToString('N'))"
    $publish = Join-Path $root 'publish'
    $assets = Join-Path $root 'assets'
    New-Item -ItemType Directory -Path $publish, $assets | Out-Null
    [IO.File]::WriteAllText((Join-Path $publish 'Demo.exe'), 'executable')
    [IO.File]::WriteAllText((Join-Path $publish 'Demo.dll'), 'assembly')
    [IO.File]::WriteAllText((Join-Path $publish 'Demo.pdb'), 'symbols')
    foreach ($asset in @('Square44x44Logo.png', 'Square150x150Logo.png', 'StoreLogo.png')) {
        [IO.File]::WriteAllBytes((Join-Path $assets $asset), [byte[]](137, 80, 78, 71, 13, 10, 26, 10))
    }
    return [pscustomobject]@{ Root = $root; Publish = $publish; Assets = $assets; Output = (Join-Path $root 'output') }
}

function Invoke-Packager($Fixture, [hashtable]$Extra = @{}) {
    $arguments = @{
        PublishDirectory = $Fixture.Publish
        OutputDirectory = $Fixture.Output
        PackageIdentity = 'SharpTS.Gui.Distribution.Test'
        Publisher = 'CN=SharpTS Test'
        DisplayName = 'Demo & Test'
        Description = 'Distribution <test>'
        Version = '1.2.3.4'
        Architecture = 'x64'
        Executable = 'Demo.exe'
        AssetsDirectory = $Fixture.Assets
        PackageUri = 'https://updates.example.test/stable/Demo.msix'
        SourceCommit = ('a' * 40)
    }
    foreach ($entry in $Extra.GetEnumerator()) { $arguments[$entry.Key] = $entry.Value }
    return & $packager @arguments
}

Invoke-Test 'canonical Windows assets are branded, correctly sized PNGs' {
    Add-Type -AssemblyName System.Drawing
    foreach ($asset in @(
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 },
        @{ Name = 'StoreLogo.png'; Size = 50 })) {
        $path = Join-Path $canonicalAssets $asset.Name
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Missing canonical asset $($asset.Name)."
        $bitmap = [Drawing.Bitmap]::new($path)
        try {
            Assert-True ($bitmap.Width -eq $asset.Size -and $bitmap.Height -eq $asset.Size) "$($asset.Name) has incorrect dimensions."
            $colors = [Collections.Generic.HashSet[int]]::new()
            for ($x = 0; $x -lt $bitmap.Width; $x += [Math]::Max(1, [int]($bitmap.Width / 10))) {
                for ($y = 0; $y -lt $bitmap.Height; $y += [Math]::Max(1, [int]($bitmap.Height / 10))) {
                    $null = $colors.Add($bitmap.GetPixel($x, $y).ToArgb())
                }
            }
            Assert-True ($colors.Count -ge 3) "$($asset.Name) appears to be a blank placeholder."
        }
        finally { $bitmap.Dispose() }
    }
}

Invoke-Test 'stage-only package emits valid identity, update, SBOM, provenance, and checksums' {
    $fixture = New-DistributionFixture
    try {
        $result = Invoke-Packager $fixture @{ StageOnly = $true }
        Assert-True (Test-Path -LiteralPath (Join-Path $result.Staging 'Demo.exe')) 'Executable was not staged.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $result.Staging 'Demo.pdb'))) 'Symbols must not ship.'
        [xml]$manifest = Get-Content -LiteralPath (Join-Path $result.Staging 'AppxManifest.xml') -Raw
        Assert-True ($manifest.Package.Identity.Name -eq 'SharpTS.Gui.Distribution.Test') 'Package identity drifted.'
        Assert-True ($manifest.Package.Applications.Application.VisualElements.DisplayName -eq 'Demo & Test') 'Manifest escaping failed.'
        [xml]$appInstaller = Get-Content -LiteralPath $result.AppInstaller -Raw
        Assert-True ($appInstaller.AppInstaller.MainPackage.Uri -eq 'https://updates.example.test/stable/Demo.msix') 'Update URI drifted.'
        $sbom = Get-Content -LiteralPath $result.Sbom -Raw | ConvertFrom-Json
        Assert-True ($sbom.spdxVersion -eq 'SPDX-2.3') 'SPDX schema is missing.'
        Assert-True (@($sbom.files.fileName) -contains './Demo.exe') 'SBOM omitted the application.'
        $provenance = Get-Content -LiteralPath $result.Provenance -Raw | ConvertFrom-Json
        Assert-True ($provenance.predicateType -eq 'https://slsa.dev/provenance/v1') 'SLSA provenance is missing.'
        Assert-True ((Get-Content -LiteralPath $result.Checksums -Raw) -match '\.spdx\.json') 'Checksums omitted the SBOM.'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'packager invokes the selected MakeAppx tool and removes staging by default' {
    $fixture = New-DistributionFixture
    try {
        $fakeMakeAppx = Join-Path $fixture.Root 'fake-makeappx.cmd'
        [IO.File]::WriteAllText($fakeMakeAppx, "@echo off`r`nif not `%1==pack exit /b 2`r`ncopy /y NUL `"`%5`" >NUL`r`nexit /b 0`r`n")
        $result = Invoke-Packager $fixture @{ MakeAppxPath = $fakeMakeAppx }
        Assert-True (Test-Path -LiteralPath $result.Msix -PathType Leaf) 'MSIX output was not produced.'
        Assert-True ($null -eq $result.Staging) 'Production staging directory should be removed.'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'packager refuses a non-empty output without explicit replacement' {
    $fixture = New-DistributionFixture
    try {
        New-Item -ItemType Directory -Path $fixture.Output | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture.Output 'keep.txt'), 'keep')
        Assert-ThrowsContaining { Invoke-Packager $fixture @{ StageOnly = $true } } 'not empty'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'force replacement is limited to packager-owned output directories' {
    $fixture = New-DistributionFixture
    try {
        New-Item -ItemType Directory -Path $fixture.Output | Out-Null
        [IO.File]::WriteAllText((Join-Path $fixture.Output 'unrelated.txt'), 'keep')
        Assert-ThrowsContaining { Invoke-Packager $fixture @{ StageOnly = $true; Force = $true } } 'not created by this packager'
        Remove-Item -LiteralPath $fixture.Output -Recurse -Force
        $null = Invoke-Packager $fixture @{ StageOnly = $true }
        [IO.File]::WriteAllText((Join-Path $fixture.Output 'old.txt'), 'old')
        $result = Invoke-Packager $fixture @{ StageOnly = $true; Force = $true }
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture.Output 'old.txt'))) 'Managed output was not replaced.'
        Assert-True (Test-Path -LiteralPath $result.Sbom -PathType Leaf) 'Replacement output is incomplete.'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'support bundle is bounded, redacted, and trace-opt-in' {
    $fixture = New-DistributionFixture
    try {
        $diagnostics = Join-Path $fixture.Root 'diagnostics'
        $errors = Join-Path $diagnostics 'Errors'
        $traces = Join-Path $diagnostics 'Traces'
        New-Item -ItemType Directory -Path $errors, $traces | Out-Null
        $profile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        [IO.File]::WriteAllText((Join-Path $errors 'sharpts-gui-error-Demo-20260809.log'), "failure at $profile\source\main.tsx")
        [IO.File]::WriteAllText((Join-Path $traces 'sharpts-gui-host-Demo-compiled-20260809.json'), '{"detail":"private"}')
        $bundle = Join-Path $fixture.Root 'support.zip'
        & $collector -OutputPath $bundle -DiagnosticsRoot $diagnostics -ApplicationName Demo
        $expanded = Join-Path $fixture.Root 'expanded'
        Expand-Archive -LiteralPath $bundle -DestinationPath $expanded
        $copiedError = Get-ChildItem -LiteralPath (Join-Path $expanded 'Errors') -File | Select-Object -First 1
        Assert-True ((Get-Content -LiteralPath $copiedError.FullName -Raw) -match '%USERPROFILE%') 'User profile was not redacted.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $expanded 'Traces'))) 'Trace content must be opt-in.'
        $support = Get-Content -LiteralPath (Join-Path $expanded 'support.json') -Raw | ConvertFrom-Json
        Assert-True ($support.traceContentIncluded -eq $false) 'Support manifest trace flag is wrong.'
        Assert-True ($support.PSObject.Properties.Name -notcontains 'userName') 'Support manifest leaked a user name.'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Write-Host "GUI distribution tests: $script:passed passed, $script:failed failed."
if ($script:failed -ne 0) { exit 1 }
