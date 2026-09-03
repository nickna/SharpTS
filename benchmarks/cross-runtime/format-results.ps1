param(
    [string]$ResultsFile = (Join-Path ([System.IO.Path]::GetTempPath()) 'bench-results/results.txt'),
    [string]$DotNetVersion,
    [string]$NodeVersion,
    [string]$BunVersion
)

if (-not $DotNetVersion) {
    $DotNetVersion = try { dotnet --version } catch { 'unknown' }
}
if (-not $NodeVersion) {
    $NodeVersion = try { node -v } catch { 'unknown' }
}
if (-not $BunVersion) {
    $BunVersion = try { bun --version } catch { 'n/a' }
}

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ResultsFile)) {
    Write-Error "Results file not found: $ResultsFile"
    exit 1
}

$os = if ($IsLinux) { 'Linux' } elseif ($IsMacOS) { 'macOS' } else { 'Windows' }
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture

Write-Output @"
## SharpTS Cross-Runtime Benchmark Results

**Environment:** .NET $DotNetVersion | Node.js $NodeVersion | Bun $BunVersion | $os $arch
**Date:** $(Get-Date -Format 'yyyy-MM-dd')

Median per-launch mean ms with the median within-launch sample standard
deviation (lower is better). ``L#`` is the number of retained launches;
per-launch means and minimums remain in the raw results artifact.

| Benchmark | Param | Interpreter (ms) | Compiled (ms) | Node.js (ms) | Bun (ms) | Compiled vs Node |
|-----------|-------|------------------:|--------------:|--------------:|---------:|-----------------:|
"@

# Parse results into a dictionary keyed by "bench|param". Keep every launch;
# overwriting by runtime would make the displayed table silently show only the
# final launch now that repeated launches are the default.
# The first five payload fields remain
# <bench>:<param>:<mean>:<min>:<stdev>. Newer harnesses append sampling,
# workload-family, and launch metadata for the structured exporter.
$data = [ordered]@{}
foreach ($line in Get-Content $ResultsFile) {
    if (-not $line.Trim()) { continue }
    $parts = $line -split '\|', 2
    $runtime = $parts[0]
    $fields = $parts[1] -split ':'
    $bench = $fields[0]
    $param = $fields[1]
    $key = "$bench|$param"

    if (-not $data.Contains($key)) {
        $data[$key] = @{}
    }
    if (-not $data[$key].ContainsKey($runtime)) {
        $data[$key][$runtime] = [Collections.Generic.List[object]]::new()
    }
    [void]$data[$key][$runtime].Add([pscustomobject]@{
        mean  = $fields[2]
        stdev = if ($fields.Count -ge 5) { $fields[4] } else { $null }
    })
}

function Get-Median([double[]]$Values) {
    $ordered = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 1) { return [double]$ordered[$middle] }
    return ([double]$ordered[$middle - 1] + [double]$ordered[$middle]) / 2
}

function Format-Number([double]$Value) {
    return $Value.ToString('0.#######', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-RuntimeSummary($entry, $runtime) {
    if (-not $entry.ContainsKey($runtime)) { return $null }
    $launches = @($entry[$runtime])
    $means = @($launches | ForEach-Object { [double]$_.mean })
    $stdevs = @($launches | Where-Object { $null -ne $_.stdev -and $_.stdev -ne '' } |
        ForEach-Object { [double]$_.stdev })
    return [pscustomobject]@{
        mean = Get-Median $means
        stdev = if ($stdevs.Count -gt 0) { Get-Median $stdevs } else { $null }
        launches = $launches.Count
    }
}

# Render a runtime cell as "median-mean ±median-stdev (L#)".
function Format-Cell($entry, $runtime) {
    $summary = Get-RuntimeSummary $entry $runtime
    if ($null -eq $summary) { return '-' }
    $meanText = Format-Number $summary.mean
    if ($null -ne $summary.stdev) {
        return "$meanText ±$(Format-Number $summary.stdev) (L$($summary.launches))"
    }
    return "$meanText (L$($summary.launches))"
}

foreach ($key in $data.Keys) {
    $kp = $key -split '\|'
    $bench = $kp[0]
    $param = $kp[1]
    $entry = $data[$key]

    $interp = Format-Cell $entry 'interpreter'
    $comp   = Format-Cell $entry 'compiled'
    $njs    = Format-Cell $entry 'node'
    $bun    = Format-Cell $entry 'bun'

    $ratio = '-'
    if ($entry.ContainsKey('compiled') -and $entry.ContainsKey('node')) {
        $compiledSummary = Get-RuntimeSummary $entry 'compiled'
        $nodeSummary = Get-RuntimeSummary $entry 'node'
        $njsNum = $nodeSummary.mean
        if ($njsNum -gt 0) {
            $ratio = '{0:F2}x' -f ($compiledSummary.mean / $njsNum)
        }
    }

    Write-Output "| $bench | $param | $interp | $comp | $njs | $bun | $ratio |"
}
