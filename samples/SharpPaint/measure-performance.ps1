param(
    [ValidateSet('compiled', 'interpreted')][string]$Mode = 'compiled',
    [string]$Configuration = 'Release',
    [string]$RuntimeDirectory
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $RuntimeDirectory) {
    dotnet build (Join-Path $PSScriptRoot 'SharpPaint.csproj') -c $Configuration -p:SharpTSEntryPoint=performance.tests.tsx
    if ($LASTEXITCODE -ne 0) { throw 'Build the current GUI SDK before measuring SharpPaint.' }
    $RuntimeDirectory = Join-Path $PSScriptRoot "bin/$Configuration/net10.0"
}
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$output = Join-Path $repository ('artifacts/sharpaint-performance/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.WorkingDirectory = $output
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @((Join-Path $runtime 'SharpTS.Gui.Host.dll'), '--mode', $Mode, '--headless')) {
    $start.ArgumentList.Add($argument)
}
$start.Environment['SHARPAINT_STORAGE_DIRECTORY'] = Join-Path $output 'storage'
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$peak = [long]0
$watch = [Diagnostics.Stopwatch]::StartNew()
try {
    while (-not $process.HasExited) {
        $process.Refresh()
        $peak = [Math]::Max($peak, $process.PeakWorkingSet64)
        if ($watch.Elapsed.TotalSeconds -gt 120) {
            $process.Kill($true)
            throw 'Performance harness exceeded 120 seconds.'
        }
        Start-Sleep -Milliseconds 50
    }
    $result = [ordered]@{
        mode = $Mode
        elapsedMilliseconds = $watch.ElapsedMilliseconds
        peakWorkingSetBytes = $peak
        stdout = $stdout.GetAwaiter().GetResult()
        stderr = $stderr.GetAwaiter().GetResult()
        exitCode = $process.ExitCode
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'result.json')
    $result | ConvertTo-Json -Depth 5
    Write-Host "Measurement files: $output"
    if ($result.exitCode -ne 0 -or -not $result.stdout.Contains('SharpPaint performance checks passed.')) {
        throw 'Performance measurement failed; inspect result.json.'
    }
}
finally { $process.Dispose() }
