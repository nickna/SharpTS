[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$ArtifactsPath,
    [string]$ResultsDirectory = 'artifacts/core-partitions'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'core-test-partitions.ps1')
$filters = Get-CoreTestFilters
$inventories = @{}
foreach ($partition in $filters.Keys) {
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.WorkingDirectory = $repository
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $arguments = @('test', 'tests/SharpTS.Tests/SharpTS.Tests.csproj', '--no-build', '--configuration', $Configuration,
        '--list-tests', '--verbosity', 'quiet', '--filter', $filters[$partition])
    if ($ArtifactsPath) { $arguments += @('--artifacts-path', [IO.Path]::GetFullPath($ArtifactsPath)) }
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $reads = [Threading.CancellationTokenSource]::new()
    $stdout = $process.StandardOutput.ReadToEndAsync($reads.Token)
    $stderr = $process.StandardError.ReadToEndAsync($reads.Token)
    try {
        if (-not $process.WaitForExit(60000)) { throw "Core $partition discovery exceeded 60 seconds." }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000)) {
            throw "Core $partition discovery output did not close within 5 seconds."
        }
        if ($process.ExitCode -ne 0) { throw "Core $partition discovery failed: $($stdout.Result)`n$($stderr.Result)" }
        $listing = $false
        $entries = [Collections.Generic.List[string]]::new()
        foreach ($line in [regex]::Split($stdout.Result, '\r?\n')) {
            if ($line -eq 'The following Tests are available:') { $listing = $true; continue }
            if (-not $listing -or [string]::IsNullOrWhiteSpace($line)) { continue }
            if ($line -notmatch '^\s+(SharpTS\.Tests\..+)$') { throw "Unrecognized discovery output: $line" }
            $entries.Add($Matches[1])
        }
        $inventories[$partition] = $entries.ToArray()
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        $reads.Cancel()
        try { [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000) } catch { }
        $process.Dispose()
        $reads.Dispose()
    }
}
$proof = New-CoreTestPartitionProof -BaseEntries $inventories.Base -InProcessEntries $inventories.InProcess -ProcessEntries $inventories.Process
$results = [IO.Path]::GetFullPath($ResultsDirectory, $repository)
[void][IO.Directory]::CreateDirectory($results)
$proof | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $results 'core-partitions.json')
Write-Host "Core exact-once partition verified: $($proof.BaseEntries) = $($proof.InProcessEntries) in-process + $($proof.ProcessEntries) process entries."
