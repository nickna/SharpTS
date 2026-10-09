param([string]$OutputDirectory = '', [string]$PackageCache = '')
$ErrorActionPreference = 'Stop'
$taskRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRepoRoot 'artifacts/member-reference-benchmark' }
$taskOutputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutputRoot -Force | Out-Null
$taskRestoreOptions = @('-p:NuGetAudit=false', '-p:MinVerVersionOverride=0.0.0-benchmark')
if ($PackageCache) {
    $taskEmptyFeed = Join-Path $taskOutputRoot 'empty-feed'
    New-Item -ItemType Directory -Path $taskEmptyFeed -Force | Out-Null
    $taskRestoreOptions += @("-p:RestorePackagesPath=$([IO.Path]::GetFullPath($PackageCache))", '--source', $taskEmptyFeed)
}
& dotnet build (Join-Path $taskRepoRoot 'src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj') -c Release -v minimal @taskRestoreOptions
if ($LASTEXITCODE) { throw 'Language server build failed.' }
$taskLibraryDirectory = Join-Path $taskRepoRoot 'src/SharpTS.LanguageServer/bin/Release/net10.0'
& dotnet build (Join-Path $PSScriptRoot 'MemberReferences.csproj') -c Release -v minimal "-p:LibraryDirectory=$taskLibraryDirectory" "-p:BaseOutputPath=$taskOutputRoot/bin/" "-p:BaseIntermediateOutputPath=$taskOutputRoot/obj/" @taskRestoreOptions
if ($LASTEXITCODE) { throw 'Benchmark build failed.' }
$taskRevision = & git -C $taskRepoRoot rev-parse --short HEAD
& dotnet (Join-Path $taskOutputRoot 'bin/Release/net10.0/SharpTS.Tests.dll') $taskOutputRoot results.json "$taskRevision+working-tree"
if ($LASTEXITCODE) { throw 'Member reference benchmark failed.' }
