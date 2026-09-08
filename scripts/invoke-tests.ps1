[CmdletBinding()]
param(
    [ValidateSet('Fast', 'Full', 'Coverage')][string]$Suite = 'Fast',
    [string[]]$Layer = @(),
    [switch]$NoBuild,
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '../artifacts/tests'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Suite -eq 'Coverage' -and $NoBuild) { throw 'Coverage requires a fresh isolated build.' }
if ($Suite -ne 'Full' -and $Layer.Count -gt 0) { throw 'Use -Suite Full with -Layer for a custom selection.' }
$selected = @(if ($Suite -in @('Fast', 'Coverage')) { 'Unit' } else { $Layer })
$scope = if ($selected.Count -eq 0) { 'Full hermetic core suite' } else { $selected -join ', ' }
$filter = & "$PSScriptRoot/get-test-filter.ps1" -Layer $selected
$run = Join-Path ([IO.Path]::GetFullPath($ResultsDirectory)) ($Suite.ToLowerInvariant() + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$project = Join-Path $PSScriptRoot '../tests/SharpTS.Tests/SharpTS.Tests.csproj'
$arguments = @('test', $project, '--configuration', $Configuration, '--filter', $filter,
    '--logger', 'trx;LogFileName=tests.trx', '--results-directory', $run, '--verbosity', 'minimal')
if ($NoBuild) { $arguments += @('--no-build', '--no-restore') }
if ($Suite -eq 'Coverage') {
    # A canceled collector may leave instrumented assemblies behind. Never share them
    # with the normal build or reuse a previous coverage run's output.
    $arguments += @('--artifacts-path', (Join-Path $run 'build'), '--settings',
        (Join-Path $PSScriptRoot '../tests/coverage.runsettings'), '--collect', 'XPlat Code Coverage')
}
Write-Host "Test scope: $scope. Artifacts: $run"
& dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $run 'run.log') | Out-Host
$testExitCode = $LASTEXITCODE
$summaryFailure = $null
try {
    & "$PSScriptRoot/write-test-summary.ps1" -ResultsDirectory $run -Scope $scope -RequireCoverage:($Suite -eq 'Coverage')
} catch { $summaryFailure = $_ }
if ($testExitCode -ne 0) { throw "dotnet test failed (exit $testExitCode). See $run/run.log. $summaryFailure" }
if ($summaryFailure) { throw $summaryFailure }
