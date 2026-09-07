[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-test-report-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
# Never publish synthetic fixtures into a real job summary.
$previousSummary = $env:GITHUB_STEP_SUMMARY
$env:GITHUB_STEP_SUMMARY = $null
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    try { & $Action; throw 'Expected failure was not raised.' }
    catch { if ($_.Exception.Message -notlike "*$Message*") { throw } }
}
try {
    Expect-Failure { & "$PSScriptRoot/write-test-summary.ps1" -ResultsDirectory $scratch } 'No TRX results'
    '<TestRun><Results /></TestRun>' | Set-Content -LiteralPath (Join-Path $scratch 'sample.trx')
    Expect-Failure { & "$PSScriptRoot/write-test-summary.ps1" -ResultsDirectory $scratch } 'zero cases'
    @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="fast" outcome="Passed" duration="00:00:00.1000000" />
    <UnitTestResult testName="slow" outcome="Failed" duration="00:00:02.0000000" />
    <UnitTestResult testName="optional" outcome="NotExecuted" duration="00:00:00" />
  </Results>
</TestRun>
'@ | Set-Content -LiteralPath (Join-Path $scratch 'sample.trx')
    Expect-Failure { & "$PSScriptRoot/write-test-summary.ps1" -ResultsDirectory $scratch -RequireCoverage } 'Expected one coverage report'
    @'
<coverage><packages><package name="SharpTS" branch-rate="0.5"><classes>
  <class filename="src/SharpTS/Parsing/Lexer.cs"><lines>
    <line number="1" hits="1" branch="true" condition-coverage="50% (1/2)" />
    <line number="2" hits="0" />
  </lines></class>
  <class filename="src/SharpTS/Parsing/Lexer.cs"><lines><line number="1" hits="0" /></lines></class>
  <class filename="src/SharpTS/Runtime/Value.cs"><lines><line number="5" hits="1" /></lines></class>
</classes></package></packages></coverage>
'@ | Set-Content -LiteralPath (Join-Path $scratch 'coverage.cobertura.xml')
    $deployment = Join-Path $scratch 'TestRun/In/host'
    New-Item -ItemType Directory -Path $deployment -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $scratch 'coverage.cobertura.xml') -Destination $deployment
    & "$PSScriptRoot/write-test-summary.ps1" -ResultsDirectory $scratch -RequireCoverage -Scope 'Fixture'
    $summary = Get-Content -LiteralPath (Join-Path $scratch 'summary.json') -Raw | ConvertFrom-Json
    if ($summary.Counts.Total -ne 3 -or $summary.Counts.Executed -ne 2 -or $summary.Counts.NotExecuted -ne 1) { throw 'Incorrect executed/skip counts.' }
    if ($summary.Timings.Slowest[0].Name -ne 'slow' -or $summary.Timings.P95Seconds -ne 2) { throw 'Incorrect durations.' }
    $assembly = $summary.Assemblies[0]
    if ($assembly.LinesTotal -ne 3 -or $assembly.LinesCovered -ne 2 -or $assembly.LinePercent -ne 66.67 -or $assembly.BranchPercent -ne 50) { throw 'Incorrect coverage accounting or duplicate source lines.' }
    if ($summary.Subsystems.Count -ne 2) { throw 'Incorrect subsystem grouping.' }
    '<TestRun><Results><UnitTestResult testName="skipped" outcome="NotExecuted" /></Results></TestRun>' | Set-Content -LiteralPath (Join-Path $scratch 'sample.trx')
    Expect-Failure { & "$PSScriptRoot/write-test-summary.ps1" -ResultsDirectory $scratch } 'executed zero cases'
    Expect-Failure { & "$PSScriptRoot/get-test-filter.ps1" -Layer 'Typo' } 'Unknown test layer'
    Expect-Failure { & "$PSScriptRoot/invoke-tests.ps1" -Suite Coverage -NoBuild } 'fresh isolated build'
    $filter = & "$PSScriptRoot/get-test-filter.ps1" -Layer Unit
    if ($filter -notmatch 'FullyQualifiedName!~SharpTS.Tests.ParserTests.NegativeTests\.' -or $filter -notmatch 'Category!=LiveNetwork') { throw 'Unit selection includes process probes or opt-in categories.' }
    $filter = & "$PSScriptRoot/get-test-filter.ps1" -Layer Semantics
    if ($filter -notmatch 'FullyQualifiedName!~SharpTS.Tests.SharedTests.BuiltInModules\.' -or $filter -notmatch 'FullyQualifiedName~SharpTS.Tests.SharedTests.BuiltInModules.CryptoKDFTests\.') { throw 'Nested namespace/type override precedence was lost.' }
    Write-Host 'Test selection and reporting checks passed.'
} finally {
    $env:GITHUB_STEP_SUMMARY = $previousSummary
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedScratch.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup path: $resolvedScratch" }
    Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
}
