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

Per-call mean ms with sample standard deviation (lower is better). Per-call
minimums are kept in the raw results artifact for deeper analysis.

| Benchmark | Param | Interpreter (ms) | Compiled (ms) | Node.js (ms) | Bun (ms) | Compiled vs Node |
|-----------|-------|------------------:|--------------:|--------------:|---------:|-----------------:|
"@

# Parse results into a dictionary keyed by "bench|param".
# Each line is: <runtime>|<bench>:<param>:<mean>:<min>:<stdev>
# (older <mean>-only lines are still accepted: min/stdev default to absent).
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
    $data[$key][$runtime] = @{
        mean  = $fields[2]
        stdev = if ($fields.Count -ge 5) { $fields[4] } else { $null }
    }
}

# Render a runtime cell as "mean ±stdev" (or just "mean", or "-" if absent).
function Format-Cell($entry, $runtime) {
    if (-not $entry.ContainsKey($runtime)) { return '-' }
    $m = $entry[$runtime]
    if ($null -ne $m.stdev -and $m.stdev -ne '') {
        return "$($m.mean) ±$($m.stdev)"
    }
    return "$($m.mean)"
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
        $njsNum = [double]$entry['node'].mean
        if ($njsNum -gt 0) {
            $ratio = '{0:F2}x' -f ([double]$entry['compiled'].mean / $njsNum)
        }
    }

    Write-Output "| $bench | $param | $interp | $comp | $njs | $bun | $ratio |"
}
