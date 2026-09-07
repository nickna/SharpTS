[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$Scope = 'Unspecified',
    [switch]$RequireCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse -File)
if ($files.Count -eq 0) { throw "No TRX results in $ResultsDirectory." }
$results = [Collections.Generic.List[object]]::new()
$output = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($test in $xml.SelectNodes("//*[local-name()='UnitTestResult']")) {
        $duration = $test.GetAttribute('duration')
        $seconds = if ($duration) { [TimeSpan]::Parse($duration, $culture).TotalSeconds } else { 0.0 }
        $results.Add([pscustomobject]@{ Name = $test.GetAttribute('testName'); Outcome = $test.GetAttribute('outcome'); Seconds = $seconds })
    }
    foreach ($node in $xml.SelectNodes("//*[local-name()='StdOut']")) { $output.Add($node.InnerText) }
}
if ($results.Count -eq 0) { throw 'Test discovery produced zero cases.' }
$executed = @($results | Where-Object Outcome -in @('Passed','Failed')).Count
if ($executed -eq 0) { throw 'The selection executed zero cases (all cases were skipped or not run).' }
$sorted = @($results.Seconds | Sort-Object)
$slowest = @($results | Sort-Object Seconds -Descending | Select-Object -First 20)
$counts = [ordered]@{ Total = $results.Count; Executed = $executed; Passed = @($results | Where-Object Outcome -eq 'Passed').Count;
    Failed = @($results | Where-Object Outcome -eq 'Failed').Count; NotExecuted = $results.Count - $executed }
$timings = [ordered]@{ P50Seconds = $sorted[[int][Math]::Ceiling($sorted.Count * 0.50) - 1]; P95Seconds = $sorted[[int][Math]::Ceiling($sorted.Count * 0.95) - 1]; Slowest = $slowest }
$assemblies = [Collections.Generic.List[object]]::new()
$subsystems = [Collections.Generic.List[object]]::new()
# VSTest can deploy a byte-identical attachment copy under TestRun/In as well as
# the collector's GUID directory. Count the report once, but reject distinct runs
# when one isolated coverage run was requested.
$coverageFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter 'coverage.cobertura.xml' -Recurse -File |
    Group-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } |
    ForEach-Object { $_.Group[0] })
if ($RequireCoverage -and $coverageFiles.Count -ne 1) { throw "Expected one coverage report; found $($coverageFiles.Count)." }
function Percent([int]$covered, [int]$total) {
    if ($total -eq 0) { return $null }
    return [Math]::Round(100.0 * $covered / $total, 2)
}
foreach ($file in $coverageFiles) {
    [xml]$coverage = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($package in $coverage.SelectNodes('/coverage/packages/package')) {
        $lines = @{}
        foreach ($class in $package.SelectNodes('classes/class')) {
            $filename = $class.GetAttribute('filename').Replace('\','/')
            foreach ($line in $class.SelectNodes('lines/line')) {
                $key = $filename + ':' + $line.GetAttribute('number')
                $hit = [int]$line.GetAttribute('hits') -gt 0
                # Nested/generated classes can share source lines. A source line counts once.
                if (-not $lines.ContainsKey($key)) { $lines[$key] = @{ File = $filename; Covered = $hit } }
                elseif ($hit) { $lines[$key].Covered = $true }
            }
        }
        $covered = @($lines.Values | Where-Object Covered).Count
        # Use the collector's assembly branch summary rather than reconstructing
        # it from per-line condition coverage.
        $branchPercent = [Math]::Round(100 * [double]::Parse($package.GetAttribute('branch-rate'), $culture), 2)
        $assemblies.Add([pscustomobject]@{ Assembly = $package.GetAttribute('name'); LinesCovered = $covered; LinesTotal = $lines.Count;
            LinePercent = (Percent $covered $lines.Count); BranchPercent = $branchPercent })
        foreach ($group in ($lines.Values | Group-Object {
            $relative = $_.File -replace '^.*(?:^|/)src/', ''
            $parts = $relative.Split('/')
            if ($parts.Length -ge 3) { "$($parts[0])/$($parts[1])" }
            elseif ($parts.Length -eq 2) { "$($parts[0])/(root)" }
            else { '(root)' }
        })) {
            $covered = @($group.Group | Where-Object Covered).Count
            $subsystems.Add([pscustomobject]@{ Assembly = $package.GetAttribute('name'); Subsystem = $group.Name; LinesCovered = $covered; LinesTotal = $group.Count; LinePercent = (Percent $covered $group.Count) })
        }
    }
}
if ($RequireCoverage -and ($assemblies.Count -eq 0 -or ($assemblies.LinesTotal | Measure-Object -Sum).Sum -eq 0)) { throw 'Coverage contains no production source lines.' }
$corpusSummaries = @($output | ForEach-Object { [regex]::Matches($_, '(?m)^summary: [^\r\n]+') } | ForEach-Object Value)
$summary = [ordered]@{ Scope = $Scope; Counts = $counts; Timings = $timings; CorpusSummaries = $corpusSummaries; Assemblies = @($assemblies.ToArray()); Subsystems = @($subsystems.ToArray()) }
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'summary.json')
$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add("## Test results: $Scope")
$markdown.Add('')
$markdown.Add("$($counts.Total) cases: $($counts.Passed) passed, $($counts.Failed) failed, $($counts.NotExecuted) skipped/not executed. Test duration p50: $($timings.P50Seconds)s; p95: $($timings.P95Seconds)s.")
if ($corpusSummaries.Count -gt 0) {
    $markdown.Add(''); $markdown.Add('Corpus counts inside baseline facts (separate from xUnit case counts):'); $markdown.Add('')
    foreach ($line in $corpusSummaries) { $markdown.Add("- $line") }
}
if ($assemblies.Count -gt 0) {
    $markdown.Add(''); $markdown.Add('Coverage scope is the selected managed-source tests. Generated guest IL correctness requires compiler contracts, standalone execution and conformance tests.')
    $markdown.Add(''); $markdown.Add('| Assembly | Lines | Branches |'); $markdown.Add('| --- | ---: | ---: |')
    foreach ($item in $assemblies) { $markdown.Add("| $($item.Assembly) | $($item.LinesCovered)/$($item.LinesTotal) ($($item.LinePercent)%) | $($item.BranchPercent)% |") }
    $markdown.Add(''); $markdown.Add('| Subsystem | Lines |'); $markdown.Add('| --- | ---: |')
    foreach ($item in ($subsystems | Sort-Object Assembly,Subsystem)) { $markdown.Add("| $($item.Subsystem) | $($item.LinesCovered)/$($item.LinesTotal) ($($item.LinePercent)%) |") }
}
$markdown.Add(''); $markdown.Add('### Slowest cases'); $markdown.Add(''); $markdown.Add('| Case | Seconds |'); $markdown.Add('| --- | ---: |')
foreach ($test in $slowest) { $markdown.Add("| $($test.Name.Replace('|','&#124;')) | $($test.Seconds) |") }
$summaryPath = Join-Path $ResultsDirectory 'summary.md'
$markdown | Set-Content -LiteralPath $summaryPath
# Baseline facts aggregate thousands of corpus cases. Preserve their executed/skip
# summaries and progress separately; a TRX fact count is not a corpus case count.
$output | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'test-output.txt')
if ($env:GITHUB_STEP_SUMMARY) { $markdown | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY }
Write-Host "Wrote $summaryPath"
