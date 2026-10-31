[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'WinGetInitialManifest.psm1') -Force
$script:passed = 0
$script:failed = 0
$script:roots = [Collections.Generic.List[string]]::new()

function Assert-True([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Assert-Throws([scriptblock]$Action, [string]$Text) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Text*") { throw "Expected '$Text', got '$($_.Exception.Message)'." }
        return
    }
    throw "Expected failure containing '$Text'."
}
function Invoke-Test([string]$Name, [scriptblock]$Body) {
    try { & $Body; $script:passed++; Write-Host "PASS: $Name" }
    catch { $script:failed++; Write-Error "FAIL: $Name`n$($_.Exception.Message)" -ErrorAction Continue }
}
function New-TestArchive([string]$Path, [string]$Architecture, [string]$ExecutableName = 'sharpts.exe') {
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path }
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $bytes = [byte[]]::new(200)
        $bytes[0] = 0x4d; $bytes[1] = 0x5a; $bytes[60] = 128
        $bytes[128] = 0x50; $bytes[129] = 0x45
        if ($Architecture -eq 'x64') { $bytes[132] = 0x64; $bytes[133] = 0x86 }
        else { $bytes[132] = 0x64; $bytes[133] = 0xaa }
        $stream = $zip.CreateEntry($ExecutableName).Open()
        try { $stream.Write($bytes) } finally { $stream.Dispose() }
        foreach ($name in @('LICENSE', 'README.md')) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open())
            try { $writer.Write('Test fixture content') } finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}
function New-Fixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) "sharpts-winget-test-$([guid]::NewGuid().ToString('N'))"
    $script:roots.Add($root)
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $assets = @()
    foreach ($name in Get-SharpTSWindowsReleaseAssetNames '2.0.0') {
        $path = Join-Path $root $name
        $architecture = if ($name.EndsWith('-win-arm64.zip')) { 'arm64' } else { 'x64' }
        New-TestArchive $path $architecture
        $assets += [pscustomobject]@{
            name = $name
            browser_download_url = "https://github.com/nickna/SharpTS/releases/download/v2.0.0/$name"
            digest = "sha256:$((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant())"
        }
    }
    [pscustomobject]@{
        Root = $root
        Release = [pscustomobject]@{ tag_name = 'v2.0.0'; draft = $false; prerelease = $false; assets = $assets }
        Output = (Join-Path $root 'output')
    }
}
function Invoke-Generate($Fixture, [string]$Version = '2.0.0') {
    New-SharpTSWinGetInitialManifestSet -Version $Version -Release $Fixture.Release `
        -AssetDirectory $Fixture.Root -OutputDirectory $Fixture.Output
}

try {
    Invoke-Test 'two identities receive six deterministic manifests and exact ZIP hashes' {
        $f = New-Fixture
        $result = Invoke-Generate $f
        Assert-True ($result.PackageDirectories.Count -eq 2) 'Expected two package directories.'
        $files = @(Get-ChildItem $f.Output -Recurse -Filter '*.yaml')
        Assert-True ($files.Count -eq 6) 'Expected three files for each identity.'
        foreach ($asset in $result.Assets) {
            $all = ($files | Get-Content -Raw) -join "`n"
            Assert-True ($all.Contains($asset.sha256)) 'A computed archive SHA256 is missing.'
        }
        $native = Get-Content (Join-Path $result.PackageDirectories[1] 'SharpTS.SharpTS.NativeAOT.locale.en-US.yaml') -Raw
        Assert-True ($native.Contains('Third-party .NET references, --verify, --gen-decl')) 'Native limitations missing.'
        $all = ($files | Get-Content -Raw) -join "`n"
        Assert-True (!$all.Contains('ArchiveBinariesDependOnPath') -and !$all.Contains('PackageDependencies')) 'Unexpected PATH override or .NET dependency.'
        Assert-True ($all.Contains('PortableCommandAlias: sharpts') -and $all.Contains('ManifestVersion: 1.12.0')) 'Portable command or current schema missing.'
        $hashes = @($files | Get-FileHash | ForEach-Object Hash)
        Invoke-Generate $f | Out-Null
        Assert-True ((@($files | Get-FileHash | ForEach-Object Hash) -join ',') -ceq ($hashes -join ',')) 'Regeneration changed manifest bytes.'
    }
    Invoke-Test 'prerelease draft mismatched tags and non-stable versions are rejected' {
        $f = New-Fixture
        Assert-Throws { Invoke-Generate $f '2.0.0-preview.1' } 'stable'
        $f.Release.prerelease = $true
        Assert-Throws { Invoke-Generate $f } 'published'
        $f.Release.prerelease = $false; $f.Release.draft = $true
        Assert-Throws { Invoke-Generate $f } 'published'
        $f.Release.draft = $false; $f.Release.tag_name = 'v2.0.1'
        Assert-Throws { Invoke-Generate $f } 'match'
        Assert-True (!(Test-Path $f.Output)) 'Rejected input wrote output.'
    }
    Invoke-Test 'missing or non-immutable release assets are rejected before writing' {
        $f = New-Fixture
        $f.Release.assets[0].browser_download_url = 'https://example.test/latest.zip'
        Assert-Throws { Invoke-Generate $f } 'immutable'
        Assert-True (!(Test-Path $f.Output)) 'Invalid release URL wrote output.'
        $f = New-Fixture
        $f.Release.assets = @($f.Release.assets | Select-Object -Skip 1)
        Assert-Throws { Invoke-Generate $f } 'exactly one'
    }
    Invoke-Test 'missing downloaded archives and mismatched GitHub digests are rejected' {
        $f = New-Fixture
        Remove-Item -LiteralPath (Join-Path $f.Root $f.Release.assets[0].name)
        Assert-Throws { Invoke-Generate $f } 'not found'
        $f = New-Fixture
        $f.Release.assets[0].digest = 'sha256:' + ('0' * 64)
        Assert-Throws { Invoke-Generate $f } 'differs'
    }
    Invoke-Test 'nested executables wrong architectures and unsafe paths are rejected' {
        $f = New-Fixture
        $path = Join-Path $f.Root $f.Release.assets[0].name
        New-TestArchive $path 'x64' 'nested/sharpts.exe'
        Assert-Throws { Invoke-Generate $f } 'root-level'
        New-TestArchive $path 'arm64'
        Assert-Throws { Invoke-Generate $f } 'architecture'
        New-TestArchive $path 'x64' '../sharpts.exe'
        Assert-Throws { Invoke-Generate $f } 'unsafe path'
    }
    Invoke-Test 'locally edited manifests are not silently overwritten' {
        $f = New-Fixture
        $result = Invoke-Generate $f
        $path = Join-Path $result.PackageDirectories[0] 'SharpTS.SharpTS.yaml'
        Add-Content $path '# local review edit'
        Assert-Throws { Invoke-Generate $f } 'Refusing to overwrite'
        Assert-True ((Get-Content $path -Raw).Contains('# local review edit')) 'Existing edit was overwritten.'
    }
}
finally {
    foreach ($root in $script:roots) {
        $resolved = [IO.Path]::GetFullPath($root)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (!$resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) `
            -or (Split-Path -Leaf $resolved) -notlike 'sharpts-winget-test-*') { throw 'Unsafe test cleanup path.' }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
Write-Host "Tests passed: $script:passed; failed: $script:failed"
if ($script:failed -gt 0) { exit 1 }
