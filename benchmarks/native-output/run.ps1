[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Compiler,
    [Parameter(Mandatory)][string]$RuntimeIdentifier,
    [ValidateRange(3, 100)][int]$Launches = 15,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RuntimeIdentifier -cne [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier) {
    throw 'All three output variants must use this process host RID.'
}
$compilerPath = (Resolve-Path -LiteralPath $Compiler).Path
$dotnetPath = @(Get-Command dotnet -CommandType Application)[0].Source
$source = Join-Path $PSScriptRoot 'workload.ts'
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$destination = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $repository ('artifacts/native-output-benchmark/' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $destination) {
    if (@(Get-ChildItem -LiteralPath $destination -Force).Count) { throw 'OutputDirectory must be new or empty.' }
} else { New-Item -ItemType Directory -Path $destination | Out-Null }
$executableSuffix = if ($RuntimeIdentifier.StartsWith('win-')) { '.exe' } else { '' }
$compilerPrefix = @(if ([IO.Path]::GetExtension($compilerPath) -eq '.dll') { $compilerPath })
$compilerExecutable = if ($compilerPrefix.Count) { $dotnetPath } else { $compilerPath }

function Invoke-MeasuredProcess {
    param([string]$Executable, [string[]]$Arguments, [string]$LogPrefix, [switch]$WithoutDotnet,
        [int]$TimeoutMilliseconds = 600000)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    if ($WithoutDotnet) {
        $start.Environment['PATH'] = ''
        foreach ($key in @($start.Environment.Keys | Where-Object { $_ -like 'DOTNET_ROOT*' })) {
            [void]$start.Environment.Remove($key)
        }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $stdoutTask = $null
    $stderrTask = $null
    $peak = 0L
    $started = $false
    try {
        if (-not $process.Start()) { throw "Could not start $Executable" }
        $started = $true
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        while (-not $process.WaitForExit(10)) {
            try { $process.Refresh(); $peak = [Math]::Max($peak, $process.PeakWorkingSet64) } catch {}
            if ($watch.ElapsedMilliseconds -ge $TimeoutMilliseconds) { throw "Process exceeded $TimeoutMilliseconds ms" }
        }
        $watch.Stop()
        try { $process.Refresh(); $peak = [Math]::Max($peak, $process.PeakWorkingSet64) } catch {}
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), 10000)) {
            throw 'Process output did not drain within ten seconds.'
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Process exited $($process.ExitCode): $stdout$stderr" }
        return [pscustomobject]@{ milliseconds = $watch.Elapsed.TotalMilliseconds
            peakWorkingSetBytes = $(if ($peak -gt 0) { $peak } else { $null })
            stdout = $stdout.Replace("`r`n", "`n"); stderr = $stderr }
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        if ($LogPrefix -and $stdoutTask -and $stderrTask) {
            [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdoutTask, $stderrTask), 10000)
            [IO.File]::WriteAllText($LogPrefix + '.stdout.log', $(if ($stdoutTask.IsCompletedSuccessfully) {
                $stdoutTask.GetAwaiter().GetResult() } else { '[stdout did not drain after process termination]' }))
            [IO.File]::WriteAllText($LogPrefix + '.stderr.log', $(if ($stderrTask.IsCompletedSuccessfully) {
                $stderrTask.GetAwaiter().GetResult() } else { '[stderr did not drain after process termination]' }))
        }
        $process.Dispose()
    }
}

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function Get-FileBytes([IO.FileInfo[]]$Files) {
    $totalBytes = 0L
    foreach ($file in $Files) { $totalBytes += $file.Length }
    return $totalBytes
}

function Assert-NativeExecutable([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($IsWindows) {
        $stream = [IO.MemoryStream]::new($bytes, $false)
        $reader = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $machine = if ($RuntimeIdentifier.EndsWith('arm64')) { 'Arm64' } else { 'Amd64' }
            if ($reader.PEHeaders.CorHeader -or -not $reader.PEHeaders.PEHeader -or
                $reader.PEHeaders.CoffHeader.Machine.ToString() -cne $machine) {
                throw 'Expected a matching native PE image without a CLR header.'
            }
        } finally { $reader.Dispose(); $stream.Dispose() }
    } elseif ($IsLinux) {
        $machine = if ($RuntimeIdentifier.EndsWith('arm64')) { 183 } else { 62 }
        if ($bytes.Length -lt 32 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0x464c457f -or
            $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or [BitConverter]::ToUInt16($bytes, 18) -ne $machine) {
            throw 'Expected a matching native ELF image.'
        }
    } else {
        $machine = if ($RuntimeIdentifier.EndsWith('arm64')) { 0x0100000c } else { 0x01000007 }
        if ($bytes.Length -lt 32 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0xfeedfacfL -or
            [BitConverter]::ToUInt32($bytes, 4) -ne $machine) { throw 'Expected a matching native Mach-O image.' }
    }
}

$variants = @()
foreach ($target in @('dll', 'exe', 'native-aot')) {
    $folder = Join-Path $destination $target
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $output = Join-Path $folder ('workload' + $(if ($target -eq 'dll') { '.dll' } else { $executableSuffix }))
    $arguments = $compilerPrefix + @('--no-tsconfig', '--noLib', '--compile', $source, '--target', $target, '-o', $output, '--verify')
    if ($target -eq 'native-aot') { $arguments += @('--runtime', $RuntimeIdentifier) }
    $publication = Invoke-MeasuredProcess $compilerExecutable $arguments (Join-Path $folder 'compile')
    if ($target -eq 'native-aot') { Assert-NativeExecutable $output }
    $executable = if ($target -eq 'dll') { $dotnetPath } else { $output }
    $prefix = @(if ($target -eq 'dll') { $output })
    $standalone = $target -ne 'dll'
    $startup = @()
    # Warm filesystem/runtime caches; retained samples are warm fresh-process launches.
    [void](Invoke-MeasuredProcess $executable $prefix '' -WithoutDotnet:$standalone)
    for ($launch = 0; $launch -lt $Launches; $launch++) {
        $sample = Invoke-MeasuredProcess $executable $prefix (Join-Path $folder "startup-$launch") -WithoutDotnet:$standalone
        if ($sample.stdout -cne "ready`n" -or $sample.stderr) { throw "Unexpected $target startup result" }
        $startup += $sample
    }
    $steady = Invoke-MeasuredProcess $executable ($prefix + @('steady')) (Join-Path $folder 'steady') -WithoutDotnet:$standalone
    $samples = @($steady.stdout.Trim().Split("`n") | ForEach-Object { $_ | ConvertFrom-Json })
    if ($samples.Count -ne 9 -or @($samples | Where-Object { $_.checksum -ne 21 }).Count -or $steady.stderr) {
        throw "Unexpected $target steady-state checksum/output"
    }
    $outputFiles = @(Get-ChildItem -LiteralPath $folder -File -Recurse -Force | Where-Object { $_.Extension -ne '.log' })
    $symbols = @($outputFiles | Where-Object { $_.Extension -in '.pdb', '.dbg' -or $_.FullName -match '\.dSYM[\\/]' })
    $deployment = @($outputFiles | Where-Object { $_ -notin $symbols -and $_.Name -notlike '*.sharpts-native.json' })
    $variants += [ordered]@{
        target = $target; publicationMilliseconds = $publication.milliseconds
        executableBytes = (Get-Item -LiteralPath $output).Length
        outputDirectoryBytes = Get-FileBytes $outputFiles
        deploymentBytes = Get-FileBytes $deployment
        symbolBytes = Get-FileBytes $symbols
        outputSha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
        startupMedianMilliseconds = Get-Median @($startup.milliseconds)
        startupSamples = $startup | Select-Object milliseconds, peakWorkingSetBytes
        steadyStateMedianMilliseconds = Get-Median @($samples.milliseconds)
        steadyStateSamples = $samples
        steadyProcessPeakWorkingSetBytes = $steady.peakWorkingSetBytes
        dotnetAbsentFromPath = $standalone
        nativeImageVerified = $target -eq 'native-aot'
    }
}
$result = [ordered]@{
    completed = $true; runtimeIdentifier = $RuntimeIdentifier; recordedAt = (Get-Date).ToString('o')
    compilerSha256 = (Get-FileHash -LiteralPath $compilerPath -Algorithm SHA256).Hash
    sourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    dotnet = (& $dotnetPath --version); os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    processorCount = [Environment]::ProcessorCount; launches = $Launches
    runtimeEnvironment = @{
        DOTNET_PROCESSOR_COUNT = $env:DOTNET_PROCESSOR_COUNT
        DOTNET_TieredCompilation = $env:DOTNET_TieredCompilation
        DOTNET_ReadyToRun = $env:DOTNET_ReadyToRun
        COMPlus_TieredCompilation = $env:COMPlus_TieredCompilation
        COMPlus_ReadyToRun = $env:COMPlus_ReadyToRun
    }
    methodology = 'Warm fresh-process wall time includes loading and minimal output; steady samples are measured inside one warmed process. Memory is observed process peak working set, null when unavailable. Native publication retains strict warning enforcement.'
    variants = $variants
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'summary.json')
Write-Host "Native/managed output benchmark completed: $destination"
