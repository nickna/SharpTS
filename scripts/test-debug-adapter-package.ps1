[CmdletBinding()]
param(
    [string] $PackagePath,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $ResultsDirectory = './artifacts/dap-package'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resultsRoot = [IO.Path]::GetFullPath($ResultsDirectory)
$runDirectory = Join-Path $resultsRoot "run-$([Guid]::NewGuid().ToString('N'))"
$feed = Join-Path $runDirectory 'feed'
$install = Join-Path $runDirectory 'tool'
New-Item -ItemType Directory -Path $feed -Force | Out-Null

Import-Module (Join-Path $PSScriptRoot 'SmokeProcess.psm1') -Force
$processOptions = @{ WorkingDirectory = $runDirectory; LogDirectory = $runDirectory }

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $project = Join-Path $repositoryRoot 'src/SharpTS.DebugAdapter/SharpTS.DebugAdapter.csproj'
    $null = Invoke-SmokeProcess 'pack' 'dotnet' @('pack', $project, '--no-build', '--no-restore',
        '--configuration', $Configuration, '--output', $feed) @processOptions
    $candidates = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File)
    if ($candidates.Count -ne 1) { throw "Expected exactly one candidate package in $feed." }
    $PackagePath = $candidates[0].FullName
}
$candidate = (Resolve-Path -LiteralPath $PackagePath).Path
Import-Module (Join-Path $PSScriptRoot 'NuGetRelease.psm1') -Force
$identity = Get-NuGetPackageIdentity -Path $candidate
if ($identity.Id -cne 'SharpTS.DebugAdapter') {
    throw "Expected SharpTS.DebugAdapter, found '$($identity.Id)' in $candidate."
}
$localPackage = Join-Path $feed "$($identity.Id).$($identity.Version).nupkg"
if ($candidate -ne $localPackage) { Copy-Item -LiteralPath $candidate -Destination $localPackage }
$nugetConfig = Join-Path $runDirectory 'NuGet.Config'
$escapedFeed = [Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear /><add key=`"isolated-local`" value=`"$escapedFeed`" /></packageSources></configuration>" |
    Set-Content -LiteralPath $nugetConfig -Encoding utf8NoBOM

$environment = @{
    NUGET_PACKAGES = (Join-Path $runDirectory 'package-cache')
    NUGET_HTTP_CACHE_PATH = (Join-Path $runDirectory 'http-cache')
    DOTNET_CLI_HOME = (Join-Path $runDirectory 'cli-home')
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
}
$previousEnvironment = @{}
try {
    foreach ($name in $environment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $environment[$name])
    }
    $null = Invoke-SmokeProcess 'install' 'dotnet' @('tool', 'install', $identity.Id,
        '--version', $identity.Version, '--tool-path', $install, '--configfile', $nugetConfig, '--no-cache') @processOptions
    $shimName = if ($IsWindows) { 'sharpts-dap.exe' } else { 'sharpts-dap' }
    $shim = Join-Path $install $shimName
    $store = Join-Path $install ".store/sharpts.debugadapter/$($identity.Version)"
    $installedAssemblies = @(Get-ChildItem -LiteralPath $store -Filter 'SharpTS.DebugAdapter.dll' -File -Recurse)
    if ($installedAssemblies.Count -ne 1) {
        throw "Expected one adapter assembly in the exact installed package store: $store"
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($localPackage)
    try {
        $entries = @($archive.Entries | Where-Object FullName -eq 'tools/net10.0/any/SharpTS.DebugAdapter.dll')
        if ($entries.Count -ne 1) { throw 'Candidate package has no unique managed adapter payload.' }
        $stream = $entries[0].Open()
        try { $payloadHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $archive.Dispose() }
    $installedHash = (Get-FileHash -LiteralPath $installedAssemblies[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($payloadHash -cne $installedHash) { throw 'Installed adapter payload does not match the candidate package.' }
    $versionOutput = Invoke-SmokeProcess 'version' $shim @('--version') @processOptions
    if ($versionOutput.Split('+')[0] -cne $identity.Version) {
        throw "Adapter version '$versionOutput' does not match candidate package version '$($identity.Version)'. Rebuild before packing."
    }
    $protocol = & (Join-Path $PSScriptRoot 'test-debug-adapter-launch.ps1') -AdapterPath $shim `
        -ResultsDirectory (Join-Path $runDirectory 'protocol')
    [ordered]@{
        completed = $true
        package = $candidate
        packageVersion = $identity.Version
        packageSha256 = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
        adapterSha256 = $payloadHash
        shim = $shim
        adapterVersion = $versionOutput
        localOnlyConfig = $nugetConfig
        protocol = $protocol
    } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding utf8NoBOM
    Write-Host "Packaged debug adapter smoke passed. Evidence: $runDirectory"
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        Set-Item -LiteralPath "Env:$name" -Value $previousEnvironment[$name]
    }
}
