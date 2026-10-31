[CmdletBinding()]
param(
    [string]$ToolRoot = $(
        if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'sharpts-signature-verifier' }
        else { Join-Path ([IO.Path]::GetTempPath()) 'sharpts-signature-verifier' }
    ),
    [string]$ArchivePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -ne 'X64') {
    throw 'The pinned independent Authenticode verifier requires Windows x64.'
}

$version = '2.14'
$uri = 'https://github.com/mtrojnar/osslsigncode/releases/download/2.14/osslsigncode-2.14-windows-x64-mingw.zip'
$sha256 = '9a1722aaf62a27852c4eb9c35749a0248065052d0ae0a93d4ed6bb49def027f2'
$installRoot = Join-Path ([IO.Path]::GetFullPath($ToolRoot)) ("osslsigncode-$version-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $installRoot | Out-Null

if ($ArchivePath) {
    $archivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
}
else {
    $archivePath = Join-Path $installRoot 'osslsigncode.zip'
    Write-Host "Downloading independent signature verifier osslsigncode $version."
    Invoke-WebRequest -Uri $uri -OutFile $archivePath -TimeoutSec 120
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -cne $sha256) {
    throw "osslsigncode archive checksum mismatch. Expected $sha256, got $actualHash."
}

# Extract fresh after every checksum check; never trust an existing executable cache.
$payload = Join-Path $installRoot 'payload'
Expand-Archive -LiteralPath $archivePath -DestinationPath $payload
$executable = Join-Path $payload 'bin/osslsigncode.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'The verified osslsigncode archive did not contain bin/osslsigncode.exe.'
}
$versionOutput = & $executable --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch '(?m)^osslsigncode 2\.14(?:,|\s|$)') {
    throw "The pinned independent signature verifier did not report osslsigncode $version."
}
Write-Host "Verified osslsigncode $version archive SHA256 $sha256."
return (Resolve-Path -LiteralPath $executable).Path
