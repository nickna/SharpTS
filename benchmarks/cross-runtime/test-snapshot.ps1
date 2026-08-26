[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$harnessDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
Import-Module (Join-Path $harnessDirectory 'Snapshot.psm1') -Force

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw "Expected an error matching '$Pattern', but no error was thrown." }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "Expected error matching '$Pattern', got: $($caught.Exception.Message)"
    }
}

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "sharpts-snapshot-tests-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
try {
    $resultsA = Join-Path $temporaryDirectory 'results-a.txt'
    $resultsB = Join-Path $temporaryDirectory 'results-b.txt'
    $lines = @(
        'node|zeta:10:5.5:5.0:0.2:12:2:310.5:family-z:2',
        'compiled|alpha:1:2.0:1.9:0.1:20:4:300.2:family-a:1',
        'node|alpha:1:1.0:0.9:0.05:25:8:301.7:family-a:1',
        'node|zeta:10:5.0:4.8:0.15:10:2:305.0:family-z:1',
        'compiled|zeta:10:6.0:5.7:0.3:11:2:302.0:family-z:1'
    )
    Set-Content -LiteralPath $resultsA -Value $lines
    Set-Content -LiteralPath $resultsB -Value @($lines[4], $lines[2], $lines[0], $lines[3], $lines[1])

    $fixed = @{
        RepositoryRoot = (Resolve-Path (Join-Path $harnessDirectory '../..')).Path
        SelectedRuntimes = @('node', 'compiled')
        TimestampUtc = '2026-08-26T12:34:56Z'
        DotNetVersion = '10.0.100'
        NodeVersion = 'v24.0.0'
        BunVersion = $null
        RunnerIdentity = 'fixture-runner'
        OperatingSystem = 'Fixture OS'
        Architecture = 'x64'
        Cpu = 'Fixture CPU'
        Revision = [ordered]@{ commit = '1111111111111111111111111111111111111111'; dirty = $false }
    }
    $snapshotA = New-SharpTSPublicBenchmarkSnapshot -ResultsFile $resultsA @fixed
    $snapshotB = New-SharpTSPublicBenchmarkSnapshot -ResultsFile $resultsB @fixed
    $jsonA = $snapshotA | ConvertTo-Json -Depth 12
    $jsonB = $snapshotB | ConvertTo-Json -Depth 12
    Assert-True ($jsonA -ceq $jsonB) 'Snapshot output changed when raw input ordering changed.'
    Assert-True ($snapshotA.cases.Count -eq 2) 'Expected two benchmark cases.'
    Assert-True ($snapshotA.cases[0].id -ceq 'family-a/alpha?n=1') 'Cases were not sorted by stable ID.'
    Assert-True ($snapshotA.cases[1].runtimes[2].measurements[0].launch -eq 1) 'Measurements were not sorted by launch.'
    Assert-True ($snapshotA.cases[1].runtimes[2].measurements[1].launch -eq 2) 'Second launch was not retained.'
    Assert-True ($snapshotA.cases[0].runtimes.Count -eq 4) 'Missing runtimes were not explicit.'
    Assert-True ($snapshotA.cases[0].runtimes[0].status -ceq 'missing') 'Interpreter should be explicitly missing.'
    Assert-True ($snapshotA.cases[0].runtimes[0].reason -ceq 'notSelected') 'Interpreter missing reason was notSelected.'
    Assert-True ($snapshotA.cases[0].runtimes[3].status -ceq 'missing') 'Bun should be explicitly missing.'
    Assert-True (-not $snapshotA.run.tools.runtimes[3].available) 'Unavailable Bun was marked available.'

    $snapshotFile = Join-Path $temporaryDirectory 'snapshot.json'
    [void](Export-SharpTSPublicBenchmarkSnapshot -ResultsFile $resultsA -OutputFile $snapshotFile @fixed)
    Assert-True (Test-SharpTSPublicBenchmarkSnapshotFile $snapshotFile) 'Written snapshot did not validate.'

    $schema = Get-Content -LiteralPath (Join-Path $harnessDirectory 'snapshot-v1.schema.json') -Raw | ConvertFrom-Json
    Assert-True ($schema.'$schema' -ceq 'https://json-schema.org/draft/2020-12/schema') 'Schema is not JSON Schema 2020-12.'

    $malformed = Join-Path $temporaryDirectory 'malformed.txt'
    Set-Content -LiteralPath $malformed -Value 'compiled|not-enough-fields:1:2'
    Assert-Throws { ConvertFrom-SharpTSRawBenchmarkResults $malformed } 'expected 10 payload fields'

    $notFinite = Join-Path $temporaryDirectory 'not-finite.txt'
    Set-Content -LiteralPath $notFinite -Value 'compiled|alpha:1:NaN:1:0:8:1:300:family-a:1'
    Assert-Throws { ConvertFrom-SharpTSRawBenchmarkResults $notFinite } 'must be finite'

    $duplicateMeasurement = Join-Path $temporaryDirectory 'duplicate-measurement.txt'
    Set-Content -LiteralPath $duplicateMeasurement -Value @($lines[1], $lines[1])
    Assert-Throws { ConvertFrom-SharpTSRawBenchmarkResults $duplicateMeasurement } 'Duplicate benchmark measurement'

    $unknownSchema = $jsonA | ConvertFrom-Json
    $unknownSchema.schemaVersion = 2
    Assert-Throws { Assert-SharpTSPublicBenchmarkSnapshot $unknownSchema } 'Unsupported benchmark snapshot schema version'

    $duplicateCase = $jsonA | ConvertFrom-Json
    $clonedCase = $duplicateCase.cases[0] | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $duplicateCase.cases = @($duplicateCase.cases[0], $clonedCase, $duplicateCase.cases[1])
    Assert-Throws { Assert-SharpTSPublicBenchmarkSnapshot $duplicateCase } 'Duplicate benchmark case ID'

    $invalidUnit = $jsonA | ConvertFrom-Json
    $invalidUnit.cases[0].unit = 'seconds'
    Assert-Throws { Assert-SharpTSPublicBenchmarkSnapshot $invalidUnit } 'unsupported unit'

    $invalidRuntime = $jsonA | ConvertFrom-Json
    $invalidRuntime.cases[0].runtimes[0].status = 'skipped'
    Assert-Throws { Assert-SharpTSPublicBenchmarkSnapshot $invalidRuntime } 'invalid status'

    Write-Host 'Cross-runtime snapshot contract tests passed.'
} finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
