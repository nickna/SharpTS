[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packager = Join-Path $PSScriptRoot 'package-gui-macos.ps1'
$script:passed = 0
$script:failed = 0

function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-ThrowsContaining([scriptblock]$Action, [string]$Text) {
    try { & $Action } catch { if ($_.Exception.Message -like "*$Text*") { return }; throw }
    throw "Expected an error containing '$Text'."
}
function Invoke-Test([string]$Name, [scriptblock]$Test) {
    try { & $Test; $script:passed++; Write-Host "PASS: $Name" }
    catch { $script:failed++; Write-Error "FAIL: $Name`n$($_.Exception.Message)`n$($_.ScriptStackTrace)" -ErrorAction Continue }
}
function Write-ThinArm64MachO([string]$Path) {
    [byte[]]$bytes = 0..63 | ForEach-Object { 0 }
    [Array]::Copy([byte[]](0xCF, 0xFA, 0xED, 0xFE), 0, $bytes, 0, 4)
    [Array]::Copy([byte[]](0x0C, 0x00, 0x00, 0x01), 0, $bytes, 4, 4)
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function Write-UnsupportedMachO([string]$Path) {
    [byte[]]$bytes = 0..63 | ForEach-Object { 0 }
    [Array]::Copy([byte[]](0xCF, 0xFA, 0xED, 0xFE), 0, $bytes, 0, 4)
    [Array]::Copy([byte[]](0x07, 0x00, 0x00, 0x01), 0, $bytes, 4, 4)
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function Write-UniversalMachO([string]$Path) {
    [byte[]]$bytes = 0..63 | ForEach-Object { 0 }
    [Array]::Copy([byte[]](0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x02), 0, $bytes, 0, 8)
    [Array]::Copy([byte[]](0x01, 0x00, 0x00, 0x07), 0, $bytes, 8, 4)
    [Array]::Copy([byte[]](0x01, 0x00, 0x00, 0x0C), 0, $bytes, 28, 4)
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function New-Fixture([switch]$UnsupportedArchitecture) {
    $root = Join-Path ([IO.Path]::GetTempPath()) "sharpts-gui-macos-package-$([Guid]::NewGuid().ToString('N'))"
    $publish = Join-Path $root 'publish'
    New-Item -ItemType Directory -Path $publish | Out-Null
    if ($UnsupportedArchitecture) {
        Write-UnsupportedMachO (Join-Path $publish 'Demo')
    }
    else {
        Write-ThinArm64MachO (Join-Path $publish 'Demo')
    }
    Write-UniversalMachO (Join-Path $publish 'libAvaloniaNative.dylib')
    [IO.File]::WriteAllText((Join-Path $publish 'Demo.dll'), 'managed')
    [IO.File]::WriteAllText((Join-Path $publish 'Demo.pdb'), 'symbols')
    [pscustomobject]@{ Root = $root; Publish = $publish; Output = (Join-Path $root 'output'); Architecture = 'arm64' }
}
function Invoke-Packager($Fixture, [hashtable]$Extra = @{}) {
    $arguments = @{
        PublishDirectory = $Fixture.Publish
        OutputDirectory = $Fixture.Output
        BundleIdentifier = 'dev.sharpts.gui.test'
        DisplayName = 'Demo & Test'
        ShortVersion = '1.2.3'
        BuildVersion = '123'
        Architecture = $Fixture.Architecture
        Executable = 'Demo'
        StageOnly = $true
    }
    foreach ($item in $Extra.GetEnumerator()) { $arguments[$item.Key] = $item.Value }
    & $packager @arguments
}

Invoke-Test 'stage-only .app validates arm64 Mach-O and plist metadata' {
    $fixture = New-Fixture
    try {
        $result = Invoke-Packager $fixture
        $bundleExecutable = Join-Path (Join-Path (Join-Path $result.App 'Contents') 'MacOS') 'Demo'
        Assert-True (Test-Path -LiteralPath $bundleExecutable) 'Executable is missing from bundle.'
        Assert-True (-not (Test-Path -LiteralPath "$bundleExecutable.pdb")) 'Symbols must not ship.'
        [xml]$plist = Get-Content -LiteralPath $result.InfoPlist -Raw
        $keys = @($plist.plist.dict.key)
        Assert-True ($keys -contains 'CFBundleIdentifier') 'Bundle identifier is missing.'
        Assert-True ($keys -contains 'LSArchitecturePriority') 'Architecture metadata is missing.'
        $checksums = @(Get-Content -LiteralPath $result.Checksums)
        Assert-True ($checksums.Count -ge 3) 'Stage-only checksum inventory did not cover the app bundle.'
        Assert-True ([bool]($checksums -match 'Demo---Test.app/Contents/MacOS/Demo$')) 'Checksum inventory omitted the executable.'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'packager rejects a mismatched Mach-O architecture' {
    $fixture = New-Fixture -UnsupportedArchitecture
    try {
        Assert-ThrowsContaining { Invoke-Packager $fixture } "does not contain 'arm64'"
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'packager rejects malformed bundle metadata' {
    $fixture = New-Fixture
    try {
        Assert-ThrowsContaining { Invoke-Packager $fixture @{ BundleIdentifier = 'not-a-domain' } } 'BundleIdentifier'
        Assert-ThrowsContaining { Invoke-Packager $fixture @{ BuildVersion = '1..2' } } 'BuildVersion'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Invoke-Test 'release signing and notarization cannot silently downgrade to unsigned output' {
    $fixture = New-Fixture
    try {
        Assert-ThrowsContaining { Invoke-Packager $fixture @{ RequireSigned = $true } } 'requires SigningIdentity'
        Assert-ThrowsContaining { Invoke-Packager $fixture @{ RequireNotarized = $true } } 'requires SigningIdentity and NotaryKeychainProfile'
    }
    finally { Remove-Item -LiteralPath $fixture.Root -Recurse -Force }
}

Write-Host "GUI macOS package tests: $script:passed passed, $script:failed failed."
if ($script:failed -ne 0) { exit 1 }
