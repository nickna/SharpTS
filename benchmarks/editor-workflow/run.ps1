param([string]$OutputDirectory = '', [string]$LibraryDirectory = '', [string]$PackageCache = '',
    [switch]$BuildOnly, [switch]$NoBuild, [switch]$NoStdio)
$ErrorActionPreference = 'Stop'
$taskRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRepoRoot 'artifacts/editor-workflow-benchmark' }
if (!$LibraryDirectory) { $LibraryDirectory = Join-Path $taskRepoRoot 'src/SharpTS.LanguageServer/bin/Release/net10.0' }
$taskOutputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$taskLibraryRoot = [IO.Path]::GetFullPath($LibraryDirectory)
New-Item -ItemType Directory -Path $taskOutputRoot -Force | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $taskLibraryRoot 'SharpTS.LanguageServer.dll'))) { throw 'Build the production Release language server first.' }
$taskRestoreOptions = @('-p:NuGetAudit=false', '-p:MinVerVersionOverride=0.0.0-benchmark')
function Invoke-WorkflowProcess([string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds, [string]$Name) {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $taskRepoRoot
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    $stdoutTask = $null
    $stderrTask = $null
    try {
        $started = $process.Start()
        if (!$started) { throw "Cannot start $Executable." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit(10000) | Out-Null
            throw "$Executable exceeded the $TimeoutSeconds-second cleanup deadline. This is a hang deadline, not a performance threshold."
        }
        if ($process.ExitCode) { throw "$Executable failed with exit code $($process.ExitCode)." }
    }
    finally {
        if ($started -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit(10000) | Out-Null }
        if ($null -ne $stdoutTask -and $stdoutTask.Wait(5000)) {
            $text = $stdoutTask.GetAwaiter().GetResult()
            [IO.File]::WriteAllText((Join-Path $taskOutputRoot "$Name.stdout.log"), $text)
            [Console]::Out.Write($text)
        }
        if ($null -ne $stderrTask -and $stderrTask.Wait(5000)) {
            $text = $stderrTask.GetAwaiter().GetResult()
            [IO.File]::WriteAllText((Join-Path $taskOutputRoot "$Name.stderr.log"), $text)
            [Console]::Error.Write($text)
        }
        $process.Dispose()
    }
}
if ($PackageCache) {
    $taskEmptyFeed = Join-Path $taskOutputRoot 'empty-feed'
    New-Item -ItemType Directory -Path $taskEmptyFeed -Force | Out-Null
    $taskRestoreOptions += @("-p:RestorePackagesPath=$([IO.Path]::GetFullPath($PackageCache))", '--source', $taskEmptyFeed)
}
if (!$NoBuild) {
    $taskBuildArguments = @('build', (Join-Path $PSScriptRoot 'EditorWorkflow.csproj'), '-c', 'Release', '-v', 'minimal',
        "-p:LibraryDirectory=$taskLibraryRoot", "-p:BaseOutputPath=$taskOutputRoot/bin/",
        "-p:BaseIntermediateOutputPath=$taskOutputRoot/obj/") + $taskRestoreOptions
    Invoke-WorkflowProcess 'dotnet' $taskBuildArguments 120 'build'
}
if ($BuildOnly) { return }
$taskRunnerDirectory = Join-Path $taskOutputRoot 'bin/Release/net10.0'
foreach ($name in @('SharpTS.LanguageServer.dll', 'SharpTS.dll')) {
    $expected = (Get-FileHash -LiteralPath (Join-Path $taskLibraryRoot $name) -Algorithm SHA256).Hash
    $copied = (Get-FileHash -LiteralPath (Join-Path $taskRunnerDirectory $name) -Algorithm SHA256).Hash
    if ($copied -cne $expected) { throw "Stale benchmark copy of $name; rebuild against this LibraryDirectory before running." }
}
$taskRevision = & git -C $taskRepoRoot rev-parse HEAD
Invoke-WorkflowProcess 'dotnet' @((Join-Path $taskRunnerDirectory 'SharpTS.Tests.dll'), $taskOutputRoot, 'results.json', $taskRevision) 240 'services'
if (!$NoStdio) {
    Invoke-WorkflowProcess 'node' @((Join-Path $PSScriptRoot 'stdio.mjs'), (Join-Path $taskLibraryRoot 'SharpTS.LanguageServer.dll'), $taskOutputRoot) 240 'stdio'
}
