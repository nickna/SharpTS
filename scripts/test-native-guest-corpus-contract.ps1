[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$importOutput = @(. (Join-Path $PSScriptRoot 'native-guest-corpus-contract.ps1'))
if ($importOutput.Count) { throw 'Importing the native corpus checker must not emit output.' }
$root = Split-Path -Parent $PSScriptRoot
$harness = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-native-guest.ps1') -Raw
$workflow = Get-Content -LiteralPath (Join-Path $root '.github/workflows/native-guest.yml') -Raw
$corpus = Join-Path $root 'tests/native-guest'
$contract = Test-NativeGuestCorpusContract $harness $workflow $corpus

function Assert-NativeCorpusRejection([scriptblock]$Action, [string]$Pattern) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw "Wrong native corpus rejection: $($_.Exception.Message)" }
        return
    }
    throw "Expected native corpus rejection matching $Pattern"
}

function New-NativeCorpusProbeWorkflow([string[]]$Selected) {
    $literal = '@(' + (($Selected | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', ') + ')'
    $command = $contract.Commands[0]
    $text = $command.Text.Remove($command.CasesStart, $command.CasesLength).Insert($command.CasesStart, $literal)
    return "jobs:`n  probe:`n    steps:`n      - run: >-`n          $text`n"
}

# Remove a real selected case from the actual workflow command. Neither the
# omitted case nor the corpus cardinality is duplicated in this regression.
$omitted = $contract.Cases[-1]
$reduced = @($contract.Cases | Where-Object { $_ -cne $omitted })
$missingWorkflow = New-NativeCorpusProbeWorkflow $reduced
Assert-NativeCorpusRejection { Test-NativeGuestCorpusContract $harness $missingWorkflow $corpus } ("Native workflow Cases coverage differs: missing \[" + [regex]::Escape($omitted) + '\]')
$duplicatedWorkflow = New-NativeCorpusProbeWorkflow @($contract.Cases + $contract.Cases[0])
Assert-NativeCorpusRejection { Test-NativeGuestCorpusContract $harness $duplicatedWorkflow $corpus } 'Native workflow Cases repeats case'
$extraWorkflow = New-NativeCorpusProbeWorkflow @($contract.Cases + 'unknown-contract-probe')
Assert-NativeCorpusRejection { Test-NativeGuestCorpusContract $harness $extraWorkflow $corpus } 'extra \[unknown-contract-probe\]'
$reorderedWorkflow = (New-NativeCorpusProbeWorkflow @($contract.Cases | Sort-Object -Descending)).Replace('run: >-', 'run: |')
$null = Test-NativeGuestCorpusContract $harness $reorderedWorkflow $corpus

# A real absent source must fail even when all three case-name sets agree.
$emptyCorpus = Join-Path $root ('artifacts/native-corpus-contract/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $emptyCorpus -Force | Out-Null
Assert-NativeCorpusRejection { Test-NativeGuestCorpusContract $harness $workflow $emptyCorpus } 'source is missing from the corpus'
Write-Host "Validated native corpus contract: $($contract.Cases.Count) literal cases, explicit workflow coverage, existing sources, and missing/duplicate/extra/source negative controls."
