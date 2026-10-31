[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][ValidateSet('Managed', 'Native')][string]$Sku,
    [Parameter(Mandatory)][string]$Version,
    [string]$ExpectedExecutableSha256,
    [switch]$RequireSigned,
    [string]$ResultsDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SmokeProcess.psm1') -Force
$archive = (Resolve-Path -LiteralPath $ArchivePath).Path
if (-not $ResultsDirectory) { $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-archive-' + [guid]::NewGuid().ToString('N')) }
$root = [IO.Path]::GetFullPath($ResultsDirectory)
$payload = Join-Path $root 'extracted'
if (Test-Path -LiteralPath $root) { throw "Results directory already exists: $root" }
New-Item -ItemType Directory -Path $payload -Force | Out-Null
if ($archive.EndsWith('.zip')) { Expand-Archive -LiteralPath $archive -DestinationPath $payload }
elseif ($archive.EndsWith('.tar.gz')) {
    Invoke-SmokeProcess -Name extract -FileName tar -Arguments @('-xzf', $archive, '-C', $payload) -WorkingDirectory $root -LogDirectory $root | Out-Null
}
else { throw 'Expected a release .zip or .tar.gz archive.' }
foreach ($name in @('LICENSE', 'README.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw "Archive lacks root-level $name" }
}
$executable = Join-Path $payload $(if ($IsWindows) { 'sharpts.exe' } else { 'sharpts' })
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Archive lacks the root-level sharpts executable.' }
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedExecutableSha256 -and $hash -cne $ExpectedExecutableSha256.ToLowerInvariant()) { throw 'Archived executable differs from the validated publish payload.' }
$options = @{ WorkingDirectory = $root; LogDirectory = $root }
if ($RequireSigned) {
    if (-not $IsMacOS) { throw 'Developer ID and Gatekeeper validation require macOS.' }
    Invoke-SmokeProcess -Name codesign -FileName /usr/bin/codesign -Arguments @('--verify', '--deep', '--strict', '--verbose=4', $executable) @options | Out-Null
    Invoke-SmokeProcess -Name gatekeeper -FileName /usr/sbin/spctl -Arguments @('--assess', '--type', 'execute', '--verbose=4', $executable) @options | Out-Null
}
$actualVersion = Invoke-SmokeProcess -Name version -FileName $executable -Arguments @('--version') @options
if ($actualVersion -cne "sharpts $Version") { throw "Archive version mismatch: $actualVersion" }
@'
import * as path from "node:path";
console.log(path.join("alpha", "beta").split(path.sep).join("|"));
console.log([1, 2, 3].map(value => value * 2).reduce((a, b) => a + b, 0));
'@ | Set-Content -LiteralPath (Join-Path $root 'smoke.ts') -Encoding utf8NoBOM
$expected = "alpha|beta`n12"
$interpreted = Invoke-SmokeProcess -Name interpret -FileName $executable -Arguments @('--no-tsconfig', 'smoke.ts') @options
if (($interpreted -replace "`r", '') -cne $expected) { throw "Archived interpreter output mismatch: $interpreted" }
$arguments = @('--no-tsconfig', '--compile', 'smoke.ts', '-o', 'smoke.dll')
$arguments += if ($Sku -eq 'Managed') { '--verify' } else { '--ref-asm' }
$compile = Invoke-SmokeProcess -Name compile -FileName $executable -Arguments $arguments @options
if ($Sku -eq 'Managed' -and $compile -notmatch 'IL verification passed\.') { throw 'Managed archive did not pass complete IL verification.' }
$compiled = Invoke-SmokeProcess -Name saved -FileName dotnet -Arguments @('smoke.dll') @options
if (($compiled -replace "`r", '') -cne $expected) { throw "Archived compiler output mismatch: $compiled" }
if ($Sku -eq 'Native') {
    $rejected = $false
    try { Invoke-SmokeProcess -Name managed-only -FileName $executable -Arguments @('--no-tsconfig', '--compile', 'smoke.ts', '--verify', '-o', 'unsupported.dll') @options | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch 'failed with exit code') { throw }
        $rejected = $true
    }
    $rejectionLog = Get-Content (Join-Path $root 'managed-only.log') -Raw
    if (-not $rejected -or $rejectionLog -notmatch 'Error SHARPTS007:\s+--verify\b') { throw 'Native archive did not retain its managed-only verification diagnostic.' }
    if (Test-Path -LiteralPath (Join-Path $root 'unsupported.dll')) { throw 'Rejected native verification wrote an output assembly.' }
}
[ordered]@{ archive = [IO.Path]::GetFileName($archive); sku = $Sku; version = $Version; executableSha256 = $hash; signed = [bool]$RequireSigned } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding utf8NoBOM
Write-Host "Archive smoke passed: $archive ($Sku); evidence: $root"
