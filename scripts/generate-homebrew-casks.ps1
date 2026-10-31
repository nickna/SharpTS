[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ManagedArchive,
    [Parameter(Mandatory)][string]$NativeArchive,
    [Parameter(Mandatory)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Homebrew casks require a stable major.minor.patch release version.'
}
$archives = [ordered]@{ sharpts = $ManagedArchive; 'sharpts-native' = $NativeArchive }
$hashes = [ordered]@{}
foreach ($token in $archives.Keys) {
    $path = (Resolve-Path -LiteralPath $archives[$token]).Path
    if ([IO.Path]::GetFileName($path) -cne "$token-$Version-osx-arm64.tar.gz") {
        throw "Expected archive name: $token-$Version-osx-arm64.tar.gz"
    }
    $hashes[$token] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
$root = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path (Join-Path $root 'Casks') -Force | Out-Null
foreach ($token in $archives.Keys) {
    $template = Get-Content -LiteralPath (Join-Path $PSScriptRoot "../distribution/homebrew/Casks/$token.rb.in") -Raw
    $content = $template.Replace('@VERSION@', $Version).Replace('@SHA256@', $hashes[$token]) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $root "Casks/$token.rb"), $content, [Text.UTF8Encoding]::new($false))
}
Write-Host "Generated both stable casks in $root from the supplied archive files."
