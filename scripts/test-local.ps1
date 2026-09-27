# Complete local coverage with visible progress, independent stage budgets,
# and the same standalone method partition used by CI. Requires PowerShell 7.
[CmdletBinding()]
param(
    [ValidateSet('All', 'Core', 'Gui', 'Standalone')][string]$Suite = 'All',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateRange(1, 8)][int]$ShardCount = 3,
    [ValidateRange(1, 120)][int]$StageTimeoutMinutes = 30,
    [ValidateRange(1, 3600)][int]$HangTimeoutSeconds = 120,
    [switch]$NoBuild,
    [switch]$Hermetic,
    [switch]$DescribeOnly,
    [string]$ResultsDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path $repository ('artifacts/tests/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
$core = Join-Path $repository 'tests/SharpTS.Tests/SharpTS.Tests.csproj'
$gui = Join-Path $repository 'tests/gui-conformance/SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj'
$categoryFilter = if ($Hermetic) { 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm&' } else { '' }

function New-TestStage([string]$Name, [string]$Project, [string]$Filter) {
    $arguments = @('test', $Project, '--no-build', '--configuration', $Configuration,
        '--logger', 'console;verbosity=normal', '--logger', "trx;LogFileName=$Name.trx",
        '--results-directory', (Join-Path $ResultsDirectory $Name),
        '--blame-hang-timeout', "${HangTimeoutSeconds}s", '--blame-hang-dump-type', 'mini')
    if ($Filter) { $arguments += @('--filter', $Filter) }
    [pscustomobject]@{ Name = $Name; Arguments = $arguments }
}

function Invoke-Stages([object[]]$Stages) {
    $running = [Collections.Generic.List[object]]::new()
    $success = $true
    try {
        foreach ($stage in $Stages) {
            $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
            $start.WorkingDirectory = $repository
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            foreach ($argument in $stage.Arguments) { $start.ArgumentList.Add($argument) }
            $logPath = Join-Path $ResultsDirectory ($stage.Name + '.log')
            $errorPath = Join-Path $ResultsDirectory ($stage.Name + '.stderr.log')
            $stdout = [IO.FileStream]::new($logPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite, 1, $true)
            $stderr = [IO.FileStream]::new($errorPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite, 1, $true)
            $process = $null
            try {
                $process = [Diagnostics.Process]::Start($start)
                $cancellation = [Threading.CancellationTokenSource]::new()
                $copies = [Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[]]@(
                    $process.StandardOutput.BaseStream.CopyToAsync($stdout, $cancellation.Token),
                    $process.StandardError.BaseStream.CopyToAsync($stderr, $cancellation.Token)))
                $running.Add([pscustomobject]@{
                    Name = $stage.Name; Process = $process; Clock = [Diagnostics.Stopwatch]::StartNew()
                    Stdout = $stdout; Stderr = $stderr; Copies = $copies; Cancellation = $cancellation
                    LogPath = $logPath; ErrorPath = $errorPath; Finished = $false
                })
            } catch {
                if ($process -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
                if ($process) { $process.Dispose() }
                $stdout.Dispose(); $stderr.Dispose()
                throw
            }
            Write-Host "Started $($stage.Name); log: $logPath"
        }
        $nextProgress = [datetime]::UtcNow
        while (@($running | Where-Object { -not $_.Finished }).Count -gt 0) {
            foreach ($run in $running | Where-Object { -not $_.Finished }) {
                if ($run.Clock.Elapsed.TotalMinutes -ge $StageTimeoutMinutes -and -not $run.Process.HasExited) {
                    Write-Host "$($run.Name) exceeded its $StageTimeoutMinutes minute stage budget."
                    $run.Process.Kill($true)
                    if (-not $run.Process.WaitForExit(5000)) { throw "Could not reap $($run.Name); PID $($run.Process.Id)" }
                    $success = $false
                }
                if ($run.Process.HasExited) {
                    if (-not $run.Copies.Wait(5000)) { throw "$($run.Name) output pipes did not close." }
                    $run.Stdout.Flush(); $run.Stderr.Flush()
                    $run.Finished = $true
                    Write-Host "$($run.Name) finished in $([math]::Round($run.Clock.Elapsed.TotalSeconds, 1))s, exit $($run.Process.ExitCode)."
                    Get-Content -LiteralPath $run.LogPath -Tail 8 | ForEach-Object { Write-Host $_ }
                    if ($run.Process.ExitCode -ne 0) {
                        $success = $false
                        Get-Content -LiteralPath $run.ErrorPath -Tail 15 | ForEach-Object { Write-Host $_ }
                    }
                }
            }
            if ([datetime]::UtcNow -ge $nextProgress) {
                foreach ($run in $running | Where-Object { -not $_.Finished }) {
                    $lastLine = Get-Content -LiteralPath $run.LogPath -Tail 1
                    Write-Host "[$($run.Name), $([math]::Round($run.Clock.Elapsed.TotalSeconds))s] $lastLine"
                }
                $nextProgress = [datetime]::UtcNow.AddSeconds(10)
            }
            Start-Sleep -Milliseconds 250
        }
    } finally {
        foreach ($run in $running) {
            if (-not $run.Process.HasExited) { $run.Process.Kill($true); [void]$run.Process.WaitForExit(5000) }
            $run.Cancellation.Cancel()
            try { [void]$run.Copies.Wait(5000) } catch { Write-Warning "$($run.Name) pipe cleanup: $_" }
            $run.Process.Dispose(); $run.Stdout.Dispose(); $run.Stderr.Dispose(); $run.Cancellation.Dispose()
        }
    }
    return $success
}

if (-not $DescribeOnly) {
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    if (-not $NoBuild) {
        $buildProject = switch ($Suite) {
            'Core' { $core }
            'Gui' { $gui }
            'Standalone' { $core }
            default { Join-Path $repository 'SharpTS.sln' }
        }
        $build = [pscustomobject]@{ Name = 'build'; Arguments = @('build', $buildProject, '--configuration', $Configuration, '-m:1') }
        if (-not (Invoke-Stages @($build))) { exit 1 }
    }
}
$stages = [Collections.Generic.List[object]]::new()
if ($Suite -in @('All', 'Core')) {
    $stages.Add((New-TestStage 'core' $core ($categoryFilter + 'FullyQualifiedName!~SharpTS.Tests.CompilerTests.StandaloneDllTests.')))
}
if ($Suite -in @('All', 'Gui')) {
    $stages.Add((New-TestStage 'gui' $gui ($categoryFilter.TrimEnd('&'))))
}
if ($Suite -in @('All', 'Standalone')) {
    for ($shard = 0; $shard -lt $ShardCount; $shard++) {
        $plan = & (Join-Path $PSScriptRoot 'test-standalone-shard.ps1') -ShardIndex $shard -ShardCount $ShardCount `
            -Configuration $Configuration -IncludeExternal:(-not $Hermetic) -DescribeOnly | ConvertFrom-Json
        $stages.Add((New-TestStage "standalone-$shard" $core $plan.Filter))
    }
}
if ($DescribeOnly) { $stages | ConvertTo-Json -Depth 5; return }
Write-Host "Suite: $Suite; configuration: $Configuration; hermetic exclusions: $Hermetic. Results: $ResultsDirectory"
$passed = $true
# Keep GUI workflows out of competition with the large parallel core batch.
foreach ($stage in $stages | Where-Object Name -NotLike 'standalone-*') {
    if (-not (Invoke-Stages @($stage))) { $passed = $false }
}
$standalone = @($stages | Where-Object Name -Like 'standalone-*')
if ($standalone.Count -gt 0 -and -not (Invoke-Stages $standalone)) { $passed = $false }
if (-not $passed) { exit 1 }
exit 0
