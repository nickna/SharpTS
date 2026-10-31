[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-casks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$managed = Join-Path $root 'sharpts-2.0.0-osx-arm64.tar.gz'
$native = Join-Path $root 'sharpts-native-2.0.0-osx-arm64.tar.gz'
# These distinct fixture bytes exercise hash calculation; no fixture cask is published.
[IO.File]::WriteAllBytes($managed, [byte[]](1, 2, 3))
[IO.File]::WriteAllBytes($native, [byte[]](4, 5, 6))
$generate = Join-Path $PSScriptRoot 'generate-homebrew-casks.ps1'
& $generate -Version 2.0.0 -ManagedArchive $managed -NativeArchive $native -OutputDirectory (Join-Path $root 'first')
& $generate -Version 2.0.0 -ManagedArchive $managed -NativeArchive $native -OutputDirectory (Join-Path $root 'second')
foreach ($token in @('sharpts', 'sharpts-native')) {
    $first = Join-Path $root "first/Casks/$token.rb"
    $second = Join-Path $root "second/Casks/$token.rb"
    if ((Get-FileHash $first).Hash -cne (Get-FileHash $second).Hash) { throw 'Cask generation is not deterministic.' }
    $text = Get-Content -LiteralPath $first -Raw
    $archive = if ($token -eq 'sharpts') { $managed } else { $native }
    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $other = if ($token -eq 'sharpts') { 'sharpts-native' } else { 'sharpts' }
    foreach ($expected in @('version "2.0.0"', "sha256 `"$hash`"", 'depends_on arch: :arm64', "conflicts_with cask: `"$other`"", 'binary "sharpts"')) {
        if (-not $text.Contains($expected)) { throw "Cask lacks $expected" }
    }
    if ($text -match '@VERSION@|@SHA256@|depends_on formula:') { throw 'Unexpected placeholder or runtime dependency.' }
}
foreach ($invalid in @('2.0.0-preview.1', 'v2.0.0', '02.0.0', '2.0.0/other')) {
    $rejected = $false
    try { & $generate -Version $invalid -ManagedArchive $managed -NativeArchive $native -OutputDirectory (Join-Path $root 'invalid') }
    catch { $rejected = $_.Exception.Message -like '*stable major.minor.patch*' }
    if (-not $rejected) { throw "Accepted invalid stable version: $invalid" }
}
$rejected = $false
try { & $generate -Version 2.0.0 -ManagedArchive $native -NativeArchive $managed -OutputDirectory (Join-Path $root 'swapped') }
catch { $rejected = $_.Exception.Message -like '*Expected archive name:*' }
if (-not $rejected) { throw 'Accepted archives for the wrong SKU.' }
Write-Host "Passed cask hash, determinism, architecture, conflict, stable-version, and SKU checks. Evidence: $root"
