[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PerfLocal.psm1') -Force

foreach ($relativePath in @(
    'perf-local.ps1',
    '../benchmarks/cross-runtime/run-benchmarks.ps1'
)) {
    $path = (Resolve-Path (Join-Path $PSScriptRoot $relativePath)).Path
    $tokens = $null
    $errors = $null
    $syntaxTree = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        throw "PowerShell parser errors in '$path': $($errors.Message -join '; ')"
    }
    $reservedHomeAssignments = @($syntaxTree.FindAll({
        param($node)
        $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ieq 'HOME'
    }, $true))
    if ($reservedHomeAssignments.Count -gt 0) {
        throw "'$path' assigns PowerShell's reserved HOME variable."
    }
}

$baselineText = @'
compiled|kernel:1000:10:9:1
compiled|kernel:1000:12:10:1
compiled|tiny:1:0.010:0.009:0.001
'@
$candidateText = @'
compiled|kernel:1000:8:7:1
node|kernel:1000:6:5:1
compiled|kernel:1000:9:8:1
node|kernel:1000:7:6:1
compiled|tiny:1:0.0115:0.010:0.001
node|tiny:1:0.005:0.004:0.001
'@

$samples = @(
    ConvertFrom-SharpTSBenchmarkResults $baselineText windows baseline 1
    ConvertFrom-SharpTSBenchmarkResults $candidateText windows candidate 1
)
$comparisons = @(Get-SharpTSPerfComparisons $samples -RegressionThresholdPercent 10 -RegressionMinimumMilliseconds 0.05)
$kernel = $comparisons | Where-Object benchmark -eq 'kernel'
$tiny = $comparisons | Where-Object benchmark -eq 'tiny'

if ($kernel.baselineMedianMilliseconds -ne 11 -or
    $kernel.candidateMedianMilliseconds -ne 8.5 -or
    $kernel.nodeMedianMilliseconds -ne 6.5 -or
    $kernel.status -ne 'improvement') {
    throw "Unexpected kernel comparison: $($kernel | ConvertTo-Json -Compress)"
}
if ($tiny.status -ne 'neutral') {
    throw "The absolute-noise floor did not suppress the tiny percentage change: $($tiny | ConvertTo-Json -Compress)"
}

$regressionSamples = @(
    ConvertFrom-SharpTSBenchmarkResults 'compiled|kernel:1000:10:9:1' linux baseline 1
    ConvertFrom-SharpTSBenchmarkResults 'compiled|kernel:1000:12:11:1' linux candidate 1
)
$regression = @(Get-SharpTSPerfComparisons $regressionSamples)[0]
if ($regression.status -ne 'regression' -or $regression.changePercent -ne 20) {
    throw "Material regression was not detected: $($regression | ConvertTo-Json -Compress)"
}

$markdown = ConvertTo-SharpTSPerfMarkdown $comparisons ([ordered]@{
    baselineCommit = 'baseline'
    candidateCommit = 'candidate'
    runs = 2
})
if (($markdown -join "`n") -notmatch '\| windows \| kernel \| 1000 .* improvement \|') {
    throw 'Markdown summary did not contain the expected invariant comparison row.'
}

Write-Host 'Local performance comparison tests passed.'
