[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselineCompiler,
    [Parameter(Mandatory)][string]$CandidateCompiler,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1, 20)][int]$Launches = 5,
    [ValidateRange(100, 2000)][int]$WarmupMs = 500,
    [ValidateNotNullOrEmpty()][string[]]$CaseNames,
    [switch]$IncludeNode
)

# Frozen compiler DLLs include their adjacent dependencies/runtimeconfig files.
# Each case/runtime/launch runs in a fresh process. Only the copied harness's
# warmup changes; workload bodies, input sizes and checksum validation are intact.
$ErrorActionPreference = 'Stop'
$baseline = (Resolve-Path -LiteralPath $BaselineCompiler).Path
$candidate = (Resolve-Path -LiteralPath $CandidateCompiler).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) {
    throw "Use an empty output directory: $output"
}
$sourceDirectory = Join-Path $output 'sources'
New-Item -ItemType Directory -Force (Join-Path $sourceDirectory 'lib') | Out-Null
$originalSource = Join-Path $PSScriptRoot 'scripts/language-hot-paths.ts'
$originalHarness = Join-Path $PSScriptRoot 'scripts/lib/bench.ts'
$source = Join-Path $sourceDirectory 'language-hot-paths.ts'
$harness = Join-Path $sourceDirectory 'lib/bench.ts'
Copy-Item -LiteralPath $originalSource -Destination $source
$harnessText = [IO.File]::ReadAllText($originalHarness)
$warmupDeclaration = 'const WARMUP_CAP_MS: number = 100;'
if ([regex]::Matches($harnessText, [regex]::Escape($warmupDeclaration)).Count -ne 1) {
    throw 'The harness warmup declaration changed; review the comparison setup.'
}
$harnessText = $harnessText.Replace($warmupDeclaration, "const WARMUP_CAP_MS: number = $WarmupMs;")
[IO.File]::WriteAllText($harness, $harnessText)

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    $lines = @(& $Executable @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed ($LASTEXITCODE): $($lines -join [Environment]::NewLine)"
    }
    return $lines
}

$baselineAssembly = Join-Path $output 'baseline.dll'
$candidateAssembly = Join-Path $output 'candidate.dll'
Invoke-Checked 'dotnet' @($baseline, '--compile', $source, '-o', $baselineAssembly, '--quiet') | Out-Null
Invoke-Checked 'dotnet' @($candidate, '--compile', $source, '-o', $candidateAssembly, '--quiet', '--verify') | Out-Null
$manifest = [ordered]@{
    schemaVersion = 1
    startedAtUtc = [DateTime]::UtcNow.ToString('o')
    baselineCompilerSha256 = (Get-FileHash -LiteralPath $baseline).Hash
    candidateCompilerSha256 = (Get-FileHash -LiteralPath $candidate).Hash
    sourceSha256 = (Get-FileHash -LiteralPath $source).Hash
    originalHarnessSha256 = (Get-FileHash -LiteralPath $originalHarness).Hash
    measuredHarnessSha256 = (Get-FileHash -LiteralPath $harness).Hash
    baselineAssemblySha256 = (Get-FileHash -LiteralPath $baselineAssembly).Hash
    candidateAssemblySha256 = (Get-FileHash -LiteralPath $candidateAssembly).Hash
    baselineRuntimeConfiguration = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($baselineAssembly, 'runtimeconfig.json')) -Raw | ConvertFrom-Json
    candidateRuntimeConfiguration = Get-Content -LiteralPath ([IO.Path]::ChangeExtension($candidateAssembly, 'runtimeconfig.json')) -Raw | ConvertFrom-Json
    launches = $Launches
    warmupMs = $WarmupMs
    sampleBudgetMs = 300
    requestedCases = $CaseNames
    dotnetInfo = (Invoke-Checked 'dotnet' @('--info')) -join "`n"
    nodeVersion = if ($IncludeNode) { (Invoke-Checked 'node' @('--version')) -join '' } else { $null }
    revision = (Invoke-Checked 'git' @('-C', $PSScriptRoot, 'rev-parse', 'HEAD')) -join ''
    workingTreeChanges = @(Invoke-Checked 'git' @('-C', $PSScriptRoot, 'status', '--short'))
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'manifest.json')
$rows = [Collections.Generic.List[object]]::new()
$oldCase = $env:SHARPTS_BENCH_CASE
$oldList = $env:SHARPTS_BENCH_LIST_CASES
try {
    $env:SHARPTS_BENCH_LIST_CASES = '1'
    $env:SHARPTS_BENCH_CASE = $null
    $cases = @(Invoke-Checked 'dotnet' @($candidateAssembly) |
        Where-Object { $_ -like 'BENCH_CASE:*' } |
        ForEach-Object { ([string]$_).Substring(11) } | Select-Object -Unique)
    if ($cases.Count -eq 0) { throw 'No cases discovered.' }
    if ($PSBoundParameters.ContainsKey('CaseNames')) {
        foreach ($requested in $CaseNames) {
            if ($requested -notin $cases) { throw "Unknown benchmark case: $requested" }
        }
        $cases = @($cases | Where-Object { $_ -in $CaseNames })
    }
    $env:SHARPTS_BENCH_LIST_CASES = $null
    foreach ($case in $cases) {
        $env:SHARPTS_BENCH_CASE = $case
        for ($launch = 1; $launch -le $Launches; $launch++) {
            $variants = if ($launch % 2) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
            if ($IncludeNode) { $variants += 'node' }
            foreach ($variant in $variants) {
                Write-Host "$case / launch $launch / $variant"
                $lines = if ($variant -eq 'node') {
                    Invoke-Checked 'node' @('--experimental-strip-types', '--no-warnings', $source)
                } else {
                    $assembly = if ($variant -eq 'baseline') { $baselineAssembly } else { $candidateAssembly }
                    Invoke-Checked 'dotnet' @($assembly)
                }
                $records = @($lines | Where-Object { $_ -like 'BENCH:*' })
                if ($records.Count -ne 3) { throw "Expected three input sizes for $case / $variant; got $($records.Count)" }
                $sizes = @($records | ForEach-Object { [int](([string]$_).Split(':')[2]) } | Sort-Object -Unique)
                if (($sizes -join ',') -ne '1000,10000,100000') { throw "Unexpected input sizes for $case / $variant" }
                foreach ($record in $records) {
                    $parts = ([string]$record).Split(':')
                    if ($parts.Count -ne 9 -or $parts[1] -ne $case) { throw "Invalid result: $record" }
                    $rows.Add([pscustomobject]@{
                        case = $case; variant = $variant; launch = $launch; n = [int]$parts[2]
                        meanMs = [double]::Parse($parts[3], [Globalization.CultureInfo]::InvariantCulture)
                        minMs = [double]::Parse($parts[4], [Globalization.CultureInfo]::InvariantCulture)
                        stdDevMs = [double]::Parse($parts[5], [Globalization.CultureInfo]::InvariantCulture)
                        samples = [int]$parts[6]; inner = [int]$parts[7]
                        sampledMs = [double]::Parse($parts[8], [Globalization.CultureInfo]::InvariantCulture)
                    })
                }
                $rows | Export-Csv -LiteralPath (Join-Path $output 'measurements.csv') -NoTypeInformation
            }
        }
    }
    $manifest['completedAtUtc'] = [DateTime]::UtcNow.ToString('o')
    $manifest['measurements'] = $rows.Count
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'manifest.json')
} finally {
    $env:SHARPTS_BENCH_CASE = $oldCase
    $env:SHARPTS_BENCH_LIST_CASES = $oldList
}
