param(
    [string]$ReportPath = "BenchmarkDotNet.Artifacts\results\SharpTS.Gui.Benchmarks.GuiRendererBenchmarks-report.csv",
    [string]$BudgetPath = (Join-Path $PSScriptRoot "PerformanceBudgets.json")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-InputPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath($Path, $repositoryRoot)
}

function Convert-Measurement([string]$Value, [hashtable]$Units, [string]$Label) {
    if ($Value -notmatch '^\s*([0-9]+(?:\.[0-9]+)?)\s*([^\s]+)\s*$') {
        throw "Invalid $Label measurement '$Value'."
    }
    $unit = $Matches[2]
    if (-not $Units.ContainsKey($unit)) {
        throw "Unsupported $Label unit '$unit' in '$Value'."
    }
    return [double]$Matches[1] * $Units[$unit]
}

$resolvedReportPath = Resolve-InputPath $ReportPath
$resolvedBudgetPath = Resolve-InputPath $BudgetPath
if (-not (Test-Path -LiteralPath $resolvedReportPath -PathType Leaf)) {
    throw "Benchmark report does not exist: $resolvedReportPath"
}
if (-not (Test-Path -LiteralPath $resolvedBudgetPath -PathType Leaf)) {
    throw "Performance budget does not exist: $resolvedBudgetPath"
}

$budgets = Get-Content -LiteralPath $resolvedBudgetPath -Raw | ConvertFrom-Json
if ($budgets.schemaVersion -ne 1) {
    throw "Unsupported performance budget schema version '$($budgets.schemaVersion)'."
}
$results = @(Import-Csv -LiteralPath $resolvedReportPath)
$durationUnits = @{ "ns" = 1.0; "μs" = 1000.0; "us" = 1000.0; "ms" = 1000000.0; "s" = 1000000000.0 }
$allocationUnits = @{ "B" = 1.0; "KB" = 1024.0; "MB" = 1048576.0; "GB" = 1073741824.0 }

$failures = [Collections.Generic.List[string]]::new()
foreach ($budgetProperty in $budgets.benchmarks.PSObject.Properties) {
    $method = $budgetProperty.Name
    $budget = $budgetProperty.Value
    $matches = @($results | Where-Object Method -eq $method)
    if ($matches.Count -ne 1) {
        $failures.Add("${method}: expected exactly one result, found $($matches.Count).")
        continue
    }

    $meanNanoseconds = Convert-Measurement $matches[0].Mean $durationUnits "duration"
    $allocatedBytes = Convert-Measurement $matches[0].Allocated $allocationUnits "allocation"
    if ($meanNanoseconds -gt $budget.maxMeanNanoseconds) {
        $failures.Add("${method} mean: $meanNanoseconds ns > $($budget.maxMeanNanoseconds) ns.")
    }
    if ($allocatedBytes -gt $budget.maxAllocatedBytes) {
        $failures.Add("${method} allocation: $allocatedBytes bytes > $($budget.maxAllocatedBytes) bytes.")
    }
    Write-Host "${method}: $meanNanoseconds / $($budget.maxMeanNanoseconds) ns; $allocatedBytes / $($budget.maxAllocatedBytes) bytes."
}

if ($failures.Count -ne 0) {
    throw "GUI performance budgets failed:`n$($failures -join "`n")"
}
Write-Host "GUI timing, allocation, and throughput budgets passed."
