[CmdletBinding()]
param(
    [ValidateSet('windows', 'ubuntu')]
    [string[]]$Platforms = @('windows', 'ubuntu'),

    [string[]]$Workloads = @(),

    [ValidateRange(1, 20)]
    [int]$Launches = 3,

    [string]$OutputDirectory,

    [switch]$NoBuild,

    [switch]$KeepContainer
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../..')).Path
$scriptsRoot = Join-Path $repoRoot 'benchmarks/cross-runtime/scripts'
$sharpTsProject = Join-Path $repoRoot 'src/SharpTS/SharpTS.csproj'
$startupSource = Join-Path $scriptRoot 'startup.ts'

if (-not $OutputDirectory) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputDirectory = Join-Path $repoRoot ".perf-gc-profiles/$stamp"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$compiledRoot = Join-Path $OutputDirectory 'compiled'
$measurementsPath = Join-Path $OutputDirectory 'measurements.json'
$summaryPath = Join-Path $OutputDirectory 'summary.md'
$metadataPath = Join-Path $OutputDirectory 'metadata.json'
$dockerImage = 'sharpts-gc-profile-bench:local'

$profiles = [ordered]@{
    workstation = [ordered]@{
        'System.GC.Server' = $false
        'System.GC.Concurrent' = $true
    }
    adaptive = [ordered]@{
        'System.GC.Server' = $true
        'System.GC.Concurrent' = $true
        'System.GC.DynamicAdaptationMode' = 1
    }
    throughput = [ordered]@{
        'System.GC.Server' = $true
        'System.GC.Concurrent' = $true
        'System.GC.DynamicAdaptationMode' = 0
    }
}

function Invoke-Checked([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory = $repoRoot) {
    Push-Location $WorkingDirectory
    try {
        $output = @(& $FileName @Arguments 2>&1)
        if ($LASTEXITCODE -ne 0) {
            $tail = ($output | Select-Object -Last 25) -join [Environment]::NewLine
            throw "$FileName $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$tail"
        }
        return $output
    }
    finally {
        Pop-Location
    }
}

function Invoke-MeasuredProcess(
    [string]$FileName,
    [string[]]$Arguments,
    [string]$WorkingDirectory = $repoRoot
) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    [long]$peakWorkingSetBytes = 0
    try {
        while (-not $process.WaitForExit(10)) {
            $process.Refresh()
            $peakWorkingSetBytes = [Math]::Max($peakWorkingSetBytes, $process.WorkingSet64)
        }
        $process.Refresh()
        $peakWorkingSetBytes = [Math]::Max($peakWorkingSetBytes, $process.PeakWorkingSet64)
        $stopwatch.Stop()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            ElapsedMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
            PeakWorkingSetBytes = $peakWorkingSetBytes
            StandardOutput = $stdout
            StandardError = $stderr
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0) { return [double]::NaN }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function Write-RuntimeConfig([string]$Path, [Collections.IDictionary]$ConfigProperties) {
    $document = [ordered]@{
        runtimeOptions = [ordered]@{
            tfm = 'net10.0'
            framework = [ordered]@{
                name = 'Microsoft.NETCore.App'
                version = '10.0.0'
            }
            configProperties = $ConfigProperties
        }
    }
    [IO.File]::WriteAllText(
        $Path,
        ($document | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function Get-BenchLines([string]$Output) {
    return @($Output -split "`r?`n" | Where-Object { $_ -match '^BENCH:' })
}

function Assert-ProcessSucceeded($Result, [string]$Description) {
    if ($Result.ExitCode -eq 0) { return }
    $tail = (($Result.StandardOutput + "`n" + $Result.StandardError) -split "`r?`n" |
        Where-Object { $_ } | Select-Object -Last 25) -join [Environment]::NewLine
    throw "$Description failed with exit code $($Result.ExitCode).`n$tail"
}

$gitStatus = @(Invoke-Checked git @('status', '--porcelain=v1', '--untracked-files=all'))
if ($gitStatus.Count -gt 0) {
    $dirtyPaths = ($gitStatus | Select-Object -First 25) -join [Environment]::NewLine
    throw "GC profile results require a clean source tree so the recorded commit identifies the measured code.`n$dirtyPaths"
}
$gitCommit = @(Invoke-Checked git @('rev-parse', 'HEAD'))[0].ToString().Trim()
$gitBranch = @(Invoke-Checked git @('branch', '--show-current'))[0].ToString().Trim()

New-Item -ItemType Directory -Path $compiledRoot -Force | Out-Null

$availableScripts = @(Get-ChildItem -LiteralPath $scriptsRoot -Filter '*.ts' | Sort-Object Name)
if ($Workloads.Count -gt 0) {
    $requested = [Collections.Generic.HashSet[string]]::new(
        $Workloads,
        [StringComparer]::OrdinalIgnoreCase)
    $benchmarkScripts = @($availableScripts | Where-Object { $requested.Contains($_.BaseName) })
    $missing = @($Workloads | Where-Object {
        $name = $_
        -not ($benchmarkScripts | Where-Object { $_.BaseName -eq $name })
    })
    if ($missing.Count -gt 0) {
        throw "Unknown workload(s): $($missing -join ', '). Available: $($availableScripts.BaseName -join ', ')"
    }
}
else {
    $benchmarkScripts = $availableScripts
}

$sources = @($benchmarkScripts) + @([IO.FileInfo]$startupSource)

if (-not $NoBuild) {
    Write-Host '=== Building SharpTS (Release) ==='
    Invoke-Checked dotnet @(
        'build', $sharpTsProject, '-c', 'Release', '--nologo',
        '-p:NuGetAudit=false', '--no-restore', '-m:1', '-nr:false') | Out-Null
}

Write-Host "=== Compiling $($sources.Count) workload(s) ==="
foreach ($source in $sources) {
    $name = $source.BaseName
    $outputRoot = Join-Path $compiledRoot $name
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $dllPath = Join-Path $outputRoot "$name.dll"
    Invoke-Checked dotnet @(
        'run', '-c', 'Release', '--no-build', '--project', $sharpTsProject, '--',
        '--compile', $source.FullName, '-o', $dllPath, '--quiet') | Out-Null
    foreach ($entry in $profiles.GetEnumerator()) {
        Write-RuntimeConfig `
            (Join-Path $outputRoot "$name.$($entry.Key).runtimeconfig.json") `
            $entry.Value
    }
}

$dockerMetadata = $null
if ($Platforms -contains 'ubuntu') {
    Write-Host '=== Building pinned Ubuntu benchmark image ==='
    Invoke-Checked docker @(
        'build', '-t', $dockerImage, '-f', (Join-Path $scriptRoot 'Dockerfile'), $scriptRoot) | Out-Null
    $dockerVersion = ((Invoke-Checked docker @('version', '--format', '{{json .}}')) -join '') |
        ConvertFrom-Json
    $imageId = @(Invoke-Checked docker @(
        'image', 'inspect', $dockerImage, '--format', '{{.Id}}'))[0].ToString().Trim()
    $repoDigestOutput = @(Invoke-Checked docker @(
        'image', 'inspect', $dockerImage,
        '--format', '{{if .RepoDigests}}{{index .RepoDigests 0}}{{end}}'))
    $imageRepoDigest = if ($repoDigestOutput.Count -gt 0) {
        $repoDigestOutput[0].ToString().Trim()
    }
    else {
        $null
    }
    $dockerMetadata = [ordered]@{
        imageId = $imageId
        imageRepoDigest = $imageRepoDigest
        clientVersion = $dockerVersion.Client.Version
        serverVersion = $dockerVersion.Server.Version
        serverPlatform = $dockerVersion.Server.Platform.Name
        containerDotNetRuntimes = @(Invoke-Checked docker @(
            'run', '--rm', $dockerImage, 'dotnet', '--list-runtimes'))
        containerNode = @(Invoke-Checked docker @(
            'run', '--rm', $dockerImage, 'node', '--version'))[0].ToString().Trim()
        containerOs = @(Invoke-Checked docker @(
            'run', '--rm', $dockerImage, 'sh', '-lc',
            '. /etc/os-release && printf "%s" "$PRETTY_NAME"'))[0].ToString().Trim()
        containerArchitecture = @(Invoke-Checked docker @(
            'run', '--rm', $dockerImage, 'uname', '-m'))[0].ToString().Trim()
    }
}

$dotnetVersion = @(Invoke-Checked dotnet @('--version'))[0].ToString().Trim()
$nodeVersion = @(Invoke-Checked node @('--version'))[0].ToString().Trim()
$metadata = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTime]::UtcNow.ToString('O')
    commit = $gitCommit
    branch = $gitBranch
    worktreeClean = $true
    dotnet = $dotnetVersion
    node = $nodeVersion
    host = [ordered]@{
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processorCount = [Environment]::ProcessorCount
        dotnetRuntimes = @(Invoke-Checked dotnet @('--list-runtimes'))
    }
    docker = $dockerMetadata
    platforms = $Platforms
    launches = $Launches
    workloads = @($benchmarkScripts.BaseName)
    profiles = $profiles
}
[IO.File]::WriteAllText(
    $metadataPath,
    ($metadata | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

$measurements = [Collections.Generic.List[object]]::new()

function Add-Measurement(
    [string]$Platform,
    [string]$Runtime,
    [string]$Profile,
    [string]$Workload,
    [int]$Launch,
    $Result,
    [double]$ElapsedMilliseconds = [double]::NaN,
    [long]$PeakWorkingSetBytes = -1
) {
    if ([double]::IsNaN($ElapsedMilliseconds)) {
        $ElapsedMilliseconds = $Result.ElapsedMilliseconds
    }
    if ($PeakWorkingSetBytes -lt 0) {
        $PeakWorkingSetBytes = $Result.PeakWorkingSetBytes
    }
    $measurements.Add([pscustomobject]@{
        platform = $Platform
        runtime = $Runtime
        profile = $Profile
        workload = $Workload
        launch = $Launch
        elapsedMilliseconds = [Math]::Round($ElapsedMilliseconds, 4)
        peakWorkingSetBytes = $PeakWorkingSetBytes
        benchLines = @(Get-BenchLines $Result.StandardOutput)
    })
}

function Set-ActiveProfile([string]$Workload, [string]$Profile) {
    $root = Join-Path $compiledRoot $Workload
    Copy-Item -LiteralPath (Join-Path $root "$Workload.$Profile.runtimeconfig.json") `
        -Destination (Join-Path $root "$Workload.runtimeconfig.json") -Force
}

function Invoke-WindowsMatrix {
    Write-Host '=== Windows x64 matrix ==='
    foreach ($source in $sources) {
        $name = $source.BaseName
        $dllPath = Join-Path (Join-Path $compiledRoot $name) "$name.dll"
        for ($launch = 1; $launch -le $Launches; $launch++) {
            if ($source.FullName -eq $startupSource) {
                $nodeResult = Invoke-MeasuredProcess node @($source.FullName) $scriptRoot
            }
            else {
                $nodeResult = Invoke-MeasuredProcess node @($source.FullName) $scriptsRoot
            }
            Assert-ProcessSucceeded $nodeResult "Windows Node $name launch $launch"
            Add-Measurement 'windows' 'node' 'node' $name $launch $nodeResult

            $profileNames = @($profiles.Keys)
            $offset = ($launch - 1) % $profileNames.Count
            $orderedProfiles = @($profileNames[$offset..($profileNames.Count - 1)])
            if ($offset -gt 0) { $orderedProfiles += $profileNames[0..($offset - 1)] }
            foreach ($profile in $orderedProfiles) {
                Set-ActiveProfile $name $profile
                $result = Invoke-MeasuredProcess dotnet @($dllPath) (Split-Path -Parent $dllPath)
                Assert-ProcessSucceeded $result "Windows compiled/$profile $name launch $launch"
                Add-Measurement 'windows' 'compiled' $profile $name $launch $result
            }
        }
        Write-Host "  completed $name"
    }
}

function Get-LinuxTimeMetrics([string[]]$Lines) {
    $values = @{}
    foreach ($line in $Lines) {
        $pair = $line -split '=', 2
        if ($pair.Count -eq 2) { $values[$pair[0]] = $pair[1] }
    }
    return [pscustomobject]@{
        ElapsedMilliseconds = [double]::Parse(
            $values.elapsedSeconds,
            [Globalization.CultureInfo]::InvariantCulture) * 1000.0
        PeakWorkingSetBytes = [long]$values.maxRssKb * 1024L
    }
}

function Invoke-UbuntuMatrix {
    Write-Host '=== Ubuntu 24.04 x64 matrix (Docker) ==='
    $container = "sharpts-gc-$([Guid]::NewGuid().ToString('N').Substring(0, 10))"
    $compiledMount = "type=bind,source=$compiledRoot,target=/bench,readonly"
    $scriptsMount = "type=bind,source=$scriptsRoot,target=/scripts,readonly"
    $startupMount = "type=bind,source=$startupSource,target=/startup.ts,readonly"
    Invoke-Checked docker @(
        'run', '-d', '--rm', '--name', $container,
        '--mount', $compiledMount,
        '--mount', $scriptsMount,
        '--mount', $startupMount,
        $dockerImage) | Out-Null
    try {
        foreach ($source in $sources) {
            $name = $source.BaseName
            $sourcePath = if ($source.FullName -eq $startupSource) { '/startup.ts' } else { "/scripts/$($source.Name)" }
            $dllPath = "/bench/$name/$name.dll"
            for ($launch = 1; $launch -le $Launches; $launch++) {
                $nodeMetricName = "$name-ubuntu-node-$launch.time"
                $nodeMetricContainer = "/tmp/$nodeMetricName"
                $nodeResult = Invoke-MeasuredProcess docker @(
                    'exec', $container, '/usr/bin/time',
                    '-f', "elapsedSeconds=%e`nmaxRssKb=%M",
                    '-o', $nodeMetricContainer,
                    'node', $sourcePath)
                Assert-ProcessSucceeded $nodeResult "Ubuntu Node $name launch $launch"
                $nodeMetrics = Get-LinuxTimeMetrics @(Invoke-Checked docker @(
                    'exec', $container, 'cat', $nodeMetricContainer))
                Add-Measurement 'ubuntu' 'node' 'node' $name $launch $nodeResult `
                    $nodeMetrics.ElapsedMilliseconds $nodeMetrics.PeakWorkingSetBytes

                $profileNames = @($profiles.Keys)
                $offset = ($launch - 1) % $profileNames.Count
                $orderedProfiles = @($profileNames[$offset..($profileNames.Count - 1)])
                if ($offset -gt 0) { $orderedProfiles += $profileNames[0..($offset - 1)] }
                foreach ($profile in $orderedProfiles) {
                    Set-ActiveProfile $name $profile
                    $metricName = "$name-ubuntu-$profile-$launch.time"
                    $metricContainer = "/tmp/$metricName"
                    $result = Invoke-MeasuredProcess docker @(
                        'exec', $container, '/usr/bin/time',
                        '-f', "elapsedSeconds=%e`nmaxRssKb=%M",
                        '-o', $metricContainer,
                        'dotnet', $dllPath)
                    Assert-ProcessSucceeded $result "Ubuntu compiled/$profile $name launch $launch"
                    $metrics = Get-LinuxTimeMetrics @(Invoke-Checked docker @(
                        'exec', $container, 'cat', $metricContainer))
                    Add-Measurement 'ubuntu' 'compiled' $profile $name $launch $result `
                        $metrics.ElapsedMilliseconds $metrics.PeakWorkingSetBytes
                }
            }
            Write-Host "  completed $name"
        }
    }
    finally {
        if ($KeepContainer) {
            Write-Host "Container retained: $container"
        }
        else {
            & docker rm -f $container 2>&1 | Out-Null
        }
    }
}

foreach ($platform in $Platforms) {
    if ($platform -eq 'windows') { Invoke-WindowsMatrix }
    else { Invoke-UbuntuMatrix }
}

[IO.File]::WriteAllText(
    $measurementsPath,
    ($measurements | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

$benchRows = [Collections.Generic.List[object]]::new()
foreach ($measurement in $measurements) {
    foreach ($line in $measurement.benchLines) {
        $parts = $line -split ':'
        if ($parts.Count -lt 6) { continue }
        $benchRows.Add([pscustomobject]@{
            platform = $measurement.platform
            runtimeProfile = if ($measurement.runtime -eq 'node') { 'node' } else { $measurement.profile }
            name = $parts[1]
            parameter = [double]::Parse($parts[2], [Globalization.CultureInfo]::InvariantCulture)
            meanMilliseconds = [double]::Parse($parts[3], [Globalization.CultureInfo]::InvariantCulture)
            minimumMilliseconds = [double]::Parse($parts[4], [Globalization.CultureInfo]::InvariantCulture)
            standardDeviationMilliseconds = [double]::Parse(
                $parts[5], [Globalization.CultureInfo]::InvariantCulture)
            elapsedMilliseconds = [double]$measurement.elapsedMilliseconds
            peakWorkingSetBytes = [long]$measurement.peakWorkingSetBytes
        })
    }
}

$lines = [Collections.Generic.List[string]]::new()
$invariantCulture = [Globalization.CultureInfo]::InvariantCulture
$lines.Add('# GC profile benchmark matrix')
$lines.Add('')
$lines.Add("Generated from commit ``$($metadata.commit)`` with $Launches launches per cell.")
$lines.Add('')
$lines.Add('## Largest-input benchmark results')
$lines.Add('')
$lines.Add('| Platform | Benchmark | Input | Runtime/profile | Median mean | Median minimum | Median stdev | Median process | Median peak RSS |')
$lines.Add('|---|---|---:|---|---:|---:|---:|---:|---:|')

$namedGroups = $benchRows | Group-Object platform, name
foreach ($namedGroup in $namedGroups | Sort-Object Name) {
    $maximum = ($namedGroup.Group | Measure-Object parameter -Maximum).Maximum
    $largest = @($namedGroup.Group | Where-Object { $_.parameter -eq $maximum })
    foreach ($runtimeGroup in $largest | Group-Object runtimeProfile | Sort-Object Name) {
        $first = $runtimeGroup.Group[0]
        $mean = Get-Median @($runtimeGroup.Group.meanMilliseconds)
        $minimum = Get-Median @($runtimeGroup.Group.minimumMilliseconds)
        $stdev = Get-Median @($runtimeGroup.Group.standardDeviationMilliseconds)
        $elapsed = Get-Median @($runtimeGroup.Group.elapsedMilliseconds)
        $peakMb = (Get-Median @($runtimeGroup.Group.peakWorkingSetBytes)) / 1MB
        $parameter = ([double]$maximum).ToString('G17', $invariantCulture)
        $lines.Add("| $($first.platform) | $($first.name) | $parameter | $($first.runtimeProfile) | $($mean.ToString('F4', $invariantCulture)) ms | $($minimum.ToString('F4', $invariantCulture)) ms | $($stdev.ToString('F4', $invariantCulture)) ms | $($elapsed.ToString('F1', $invariantCulture)) ms | $($peakMb.ToString('F1', $invariantCulture)) MB |")
    }
}

$lines.Add('')
$lines.Add('## Cold startup')
$lines.Add('')
$lines.Add('| Platform | Runtime/profile | Median elapsed | Median peak RSS |')
$lines.Add('|---|---|---:|---:|')
$startupMeasurements = @($measurements | Where-Object { $_.workload -eq 'startup' })
foreach ($group in $startupMeasurements | Group-Object platform, runtime, profile | Sort-Object Name) {
    $first = $group.Group[0]
    $label = if ($first.runtime -eq 'node') { 'node' } else { $first.profile }
    $elapsed = Get-Median @($group.Group.elapsedMilliseconds)
    $peakMb = (Get-Median @($group.Group.peakWorkingSetBytes)) / 1MB
    $lines.Add("| $($first.platform) | $label | $($elapsed.ToString('F2', $invariantCulture)) ms | $($peakMb.ToString('F1', $invariantCulture)) MB |")
}

[IO.File]::WriteAllLines($summaryPath, $lines, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host "Measurements: $measurementsPath"
Write-Host "Summary:      $summaryPath"
