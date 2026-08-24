[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('sync', 'measure', 'gate', 'all')]
    [string]$Action,

    [string[]]$Platforms = @('windows', 'wsl'),

    [string[]]$Workloads = @('int-arrays', 'brainfuck', 'accumulate'),

    [ValidateRange(1, 20)]
    [int]$Runs = 3,

    [ValidateSet('checkpoint', 'full')]
    [string]$GateLevel = 'checkpoint',

    [string]$BaselinePath,

    [string]$OutputDirectory,

    [string]$WslDistro = 'Ubuntu-26.04',

    [string]$WslBareRepository,

    [string]$WslBaselinePath,

    [string]$WslCandidatePath,

    [ValidateRange(0, 1000)]
    [double]$RegressionThresholdPercent = 10,

    [ValidateRange(0, 1000)]
    [double]$RegressionMinimumMilliseconds = 0.05,

    [switch]$Enforce,

    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$RepoParent = Split-Path -Parent $RepoRoot
$Runner = Join-Path $RepoRoot 'benchmarks/cross-runtime/run-benchmarks.ps1'
$BaselinePath = if ($BaselinePath) {
    (Resolve-Path -LiteralPath $BaselinePath).Path
} else {
    (Resolve-Path -LiteralPath (Join-Path $RepoParent '.perf-baseline-worktree')).Path
}
$OutputDirectory = if ($OutputDirectory) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    Join-Path $RepoRoot ".perf-local/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
}

$Platforms = @($Platforms | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ } | Select-Object -Unique)
$Workloads = @($Workloads | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
$unknownPlatforms = @($Platforms | Where-Object { $_ -notin @('windows', 'wsl') })
if ($unknownPlatforms.Count -gt 0) {
    throw "Unknown platform(s): $($unknownPlatforms -join ', '). Available: windows, wsl"
}
if ($Platforms.Count -eq 0) {
    throw 'At least one platform must be selected.'
}
if ($Workloads.Count -eq 0 -and $Action -in @('measure', 'all')) {
    throw 'At least one workload must be selected for measurement.'
}

Import-Module (Join-Path $PSScriptRoot 'PerfLocal.psm1') -Force

function Quote-BashArgument {
    param([AllowEmptyString()] [string]$Value)
    $escaped = $Value.Replace('\', '\\').Replace('"', '\"').Replace('$', '\$').Replace('`', '\`')
    return '"' + $escaped + '"'
}

function Join-BashCommand {
    param([Parameter(Mandatory)] [string[]]$Arguments)
    return ($Arguments | ForEach-Object { Quote-BashArgument $_ }) -join ' '
}

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [switch]$Capture
    )

    Push-Location $WorkingDirectory
    try {
        if ($Capture) {
            $output = @(& $FilePath @Arguments 2>&1)
        } else {
            & $FilePath @Arguments
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
        }
        if ($Capture) { return $output }
    } finally {
        Pop-Location
    }
}

function Invoke-GitCapture {
    param(
        [Parameter(Mandatory)] [string]$Repository,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    $output = @(& git -C $Repository @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git failed in '$Repository': $($output -join [Environment]::NewLine)"
    }
    return ($output -join "`n").Trim()
}

function Get-WslHome {
    $output = @(& wsl.exe -d $WslDistro --exec bash -c 'printf %s "$HOME"' 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not query HOME in WSL distro '$WslDistro': $($output -join [Environment]::NewLine)"
    }
    return ($output -join '').Trim()
}

function Initialize-WslPaths {
    $wslUserHome = Get-WslHome
    if (-not $script:WslBareRepository) { $script:WslBareRepository = "$wslUserHome/src/SharpTS-perf.git" }
    if (-not $script:WslBaselinePath) { $script:WslBaselinePath = "$wslUserHome/src/SharpTS-perf-baseline" }
    if (-not $script:WslCandidatePath) { $script:WslCandidatePath = "$wslUserHome/src/SharpTS-perf-candidate" }
}

function Get-WslEnvironmentPrefix {
    return @'
set -euo pipefail
export DOTNET_ROOT="$HOME/.dotnet"
export BUN_INSTALL="$HOME/.bun"
export NVM_DIR="$HOME/.nvm"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$BUN_INSTALL/bin:$PATH"
. "$NVM_DIR/nvm.sh"
nvm use 22.23.2 >/dev/null
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
'@
}

function Invoke-WslChecked {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [switch]$Capture,
        [switch]$WithoutToolchain
    )

    $scriptText = if ($WithoutToolchain) { "set -euo pipefail`n$Command" } else { "$(Get-WslEnvironmentPrefix)`n$Command" }
    if ($Capture) {
        $output = @(& wsl.exe -d $WslDistro --exec bash -c $scriptText 2>&1)
    } else {
        & wsl.exe -d $WslDistro --exec bash -c $scriptText
    }
    if ($LASTEXITCODE -ne 0) {
        throw "WSL command failed with exit code $LASTEXITCODE in '$WslDistro'."
    }
    if ($Capture) { return $output }
}

function Get-RepositoryMetadata {
    param([Parameter(Mandatory)] [string]$Repository)

    $status = Invoke-GitCapture $Repository @('status', '--porcelain')
    return [ordered]@{
        commit = Invoke-GitCapture $Repository @('rev-parse', 'HEAD')
        branch = Invoke-GitCapture $Repository @('branch', '--show-current')
        dirty = [bool]$status
    }
}

function Sync-WslWorktrees {
    Initialize-WslPaths
    $candidate = Get-RepositoryMetadata $RepoRoot
    $baseline = Get-RepositoryMetadata $BaselinePath
    if ($candidate.dirty) {
        throw 'The Windows candidate worktree must be clean before WSL sync. Commit the checkpoint first.'
    }
    if (-not $candidate.branch) {
        throw 'The Windows candidate must be on a branch before WSL sync.'
    }

    $trackedStatusCommand = "git -C $(Quote-BashArgument $WslCandidatePath) status --porcelain --untracked-files=no"
    $trackedStatus = @(Invoke-WslChecked $trackedStatusCommand -Capture -WithoutToolchain)
    if (($trackedStatus -join '').Trim()) {
        throw "The WSL candidate has tracked changes. Preserve or discard them before syncing '$WslCandidatePath'."
    }

    $fetch = Join-BashCommand @(
        'git', "--git-dir=$WslBareRepository", 'fetch', 'windows',
        "refs/heads/$($candidate.branch):refs/remotes/windows/candidate", '--force'
    )
    $checkoutBaseline = Join-BashCommand @('git', '-C', $WslBaselinePath, 'checkout', '--detach', $baseline.commit)
    $checkoutCandidate = Join-BashCommand @('git', '-C', $WslCandidatePath, 'checkout', '--detach', $candidate.commit)
    Invoke-WslChecked "$fetch`n$checkoutBaseline`n$checkoutCandidate" -WithoutToolchain

    Write-Host "WSL baseline:  $($baseline.commit.Substring(0, 12))"
    Write-Host "WSL candidate: $($candidate.commit.Substring(0, 12))"
}

function Invoke-BuildForMeasurement {
    param(
        [Parameter(Mandatory)] [ValidateSet('windows', 'wsl')] [string]$Platform,
        [Parameter(Mandatory)] [string]$Repository
    )

    Write-Host "=== Building $Platform measurement tree: $Repository ==="
    if ($Platform -eq 'windows') {
        Invoke-NativeChecked dotnet @('build', 'src/SharpTS/SharpTS.csproj', '--configuration', 'Release', '--nologo') $Repository
    } else {
        $command = "cd $(Quote-BashArgument $Repository)`n" +
            (Join-BashCommand @('dotnet', 'build', 'src/SharpTS/SharpTS.csproj', '--configuration', 'Release', '--nologo'))
        Invoke-WslChecked $command
    }
}

function Invoke-OneMeasurement {
    param(
        [Parameter(Mandatory)] [ValidateSet('windows', 'wsl')] [string]$Platform,
        [Parameter(Mandatory)] [ValidateSet('baseline', 'candidate')] [string]$Variant,
        [Parameter(Mandatory)] [int]$Launch,
        [Parameter(Mandatory)] [string]$SourceRepository,
        [Parameter(Mandatory)] [string]$HarnessRepository,
        [Parameter(Mandatory)] [string]$Destination
    )

    $runtimeList = if ($Variant -eq 'candidate') { 'compiled,node' } else { 'compiled' }
    $workloadList = $Workloads -join ','
    if ($Platform -eq 'windows') {
        $scratch = Join-Path $OutputDirectory "scratch/windows-$Variant-$Launch"
        $arguments = @(
            '-NoProfile', '-File', (Join-Path $HarnessRepository 'benchmarks/cross-runtime/run-benchmarks.ps1'),
            '-RepositoryRoot', $SourceRepository,
            '-Workloads', $workloadList,
            '-Runtimes', $runtimeList,
            '-Launches', '1',
            '-OutputDirectory', $scratch,
            '-NoBuild'
        )
        if ($Variant -eq 'candidate') {
            $arguments += @('-NodeExecutable', (Get-WindowsNodeExecutable))
        }
        Invoke-NativeChecked pwsh $arguments $HarnessRepository
        Copy-Item -LiteralPath (Join-Path $scratch 'results.txt') -Destination $Destination
        return
    }

    $scratch = "$SourceRepository/.perf-local-results/$($script:MeasurementId)/$Variant-$Launch"
    $runner = "$HarnessRepository/benchmarks/cross-runtime/run-benchmarks.ps1"
    $arguments = @(
        'pwsh', '-NoProfile', '-File', $runner,
        '-RepositoryRoot', $SourceRepository,
        '-Workloads', $workloadList,
        '-Runtimes', $runtimeList,
        '-Launches', '1',
        '-OutputDirectory', $scratch,
        '-NoBuild'
    )
    Invoke-WslChecked (Join-BashCommand $arguments)
    $raw = @(Invoke-WslChecked (Join-BashCommand @('cat', "$scratch/results.txt")) -Capture -WithoutToolchain)
    [IO.File]::WriteAllLines($Destination, [string[]]$raw)
}

function Get-HostFacts {
    param([Parameter(Mandatory)] [ValidateSet('windows', 'wsl')] [string]$Platform)

    if ($Platform -eq 'windows') {
        $nodeExecutable = Get-WindowsNodeExecutable
        return [ordered]@{
            os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            dotnet = ((& dotnet --version) -join '').Trim()
            node = ((& $nodeExecutable --version) -join '').Trim()
        }
    }

    $facts = @(Invoke-WslChecked @'
printf 'os=%s\n' "$(uname -sr)"
printf 'architecture=%s\n' "$(uname -m)"
printf 'dotnet=%s\n' "$(dotnet --version)"
printf 'node=%s\n' "$(node --version)"
'@ -Capture)
    $map = [ordered]@{}
    foreach ($line in $facts) {
        $pair = ([string]$line) -split '=', 2
        if ($pair.Count -eq 2) { $map[$pair[0]] = $pair[1] }
    }
    return $map
}

function Get-WindowsNodeExecutable {
    $requiredVersion = (Get-Content -LiteralPath (Join-Path $RepoRoot '.node-version') -Raw).Trim()
    $currentNode = Get-Command node -ErrorAction SilentlyContinue
    if ($currentNode) {
        $currentVersion = ((& $currentNode.Source --version) -join '').Trim().TrimStart('v')
        if ($currentVersion -eq $requiredVersion) { return $currentNode.Source }
    }

    if ($env:NVM_HOME) {
        $nvmNode = Join-Path $env:NVM_HOME "v$requiredVersion/node.exe"
        if (Test-Path -LiteralPath $nvmNode -PathType Leaf) { return $nvmNode }
    }
    throw "Node $requiredVersion is required for Windows measurements. Install it side-by-side with 'nvm install $requiredVersion'; the harness will invoke it directly without changing the global Node selection."
}

function Invoke-PairedMeasurement {
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $OutputDirectory 'raw') | Out-Null
    $script:MeasurementId = [Guid]::NewGuid().ToString('N').Substring(0, 12)

    $baseline = Get-RepositoryMetadata $BaselinePath
    $candidate = Get-RepositoryMetadata $RepoRoot
    $samples = [Collections.Generic.List[object]]::new()
    $hostFacts = [ordered]@{}

    foreach ($platform in $Platforms) {
        if ($platform -eq 'windows') {
            $platformBaseline = $BaselinePath
            $platformCandidate = $RepoRoot
            $harness = $RepoRoot
        } else {
            Initialize-WslPaths
            $wslHead = @(
                Invoke-WslChecked (Join-BashCommand @('git', '-C', $WslCandidatePath, 'rev-parse', 'HEAD')) -Capture -WithoutToolchain
            ) -join ''
            if ($wslHead.Trim() -ne $candidate.commit) {
                throw "WSL candidate is not synced to $($candidate.commit). Run -Action sync after committing the checkpoint."
            }
            $platformBaseline = $WslBaselinePath
            $platformCandidate = $WslCandidatePath
            $harness = $WslCandidatePath
        }

        if (-not $NoBuild) {
            Invoke-BuildForMeasurement $platform $platformBaseline
            Invoke-BuildForMeasurement $platform $platformCandidate
        }
        $hostFacts[$platform] = Get-HostFacts $platform

        for ($launch = 1; $launch -le $Runs; $launch++) {
            $variants = if (($launch % 2) -eq 1) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
            foreach ($variant in $variants) {
                $source = if ($variant -eq 'baseline') { $platformBaseline } else { $platformCandidate }
                $rawPath = Join-Path $OutputDirectory "raw/$platform-$variant-$launch.txt"
                Write-Host "=== $platform $variant launch $launch/$Runs ==="
                Invoke-OneMeasurement $platform $variant $launch $source $harness $rawPath
                $text = [IO.File]::ReadAllText($rawPath)
                foreach ($sample in @(ConvertFrom-SharpTSBenchmarkResults $text $platform $variant $launch)) {
                    $samples.Add($sample)
                }
            }
        }
    }

    $comparisons = @(Get-SharpTSPerfComparisons $samples `
        -RegressionThresholdPercent $RegressionThresholdPercent `
        -RegressionMinimumMilliseconds $RegressionMinimumMilliseconds)
    $metadata = [ordered]@{
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        baselineCommit = $baseline.commit
        candidateCommit = $candidate.commit
        candidateDirty = $candidate.dirty
        runs = $Runs
        workloads = $Workloads
        platforms = $Platforms
        regressionThresholdPercent = $RegressionThresholdPercent
        regressionMinimumMilliseconds = $RegressionMinimumMilliseconds
        hosts = $hostFacts
    }
    $metadata | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'metadata.json')
    $comparisons | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'comparison.json')
    ConvertTo-SharpTSPerfMarkdown $comparisons $metadata |
        Set-Content (Join-Path $OutputDirectory 'summary.md')

    Write-Host "Performance report: $OutputDirectory"
    Get-Content (Join-Path $OutputDirectory 'summary.md')
    $regressions = @($comparisons | Where-Object status -eq 'regression')
    if ($Enforce -and $regressions.Count -gt 0) {
        throw "$($regressions.Count) material performance regression(s) exceeded the configured thresholds."
    }
}

function Invoke-WindowsGate {
    Write-Host "=== Windows $GateLevel gate ==="
    $oldAvaloniaOptOut = $env:AVALONIA_TELEMETRY_OPTOUT
    $env:AVALONIA_TELEMETRY_OPTOUT = '1'
    try {
        # Serial graph traversal avoids opaque child-MSBuild exits observed on
        # high-core-count developer machines and makes the failure log useful.
        Invoke-NativeChecked dotnet @(
            'restore', 'SharpTS.sln', '-m:1', '-p:RestoreDisableParallel=true', '-p:NuGetAudit=false'
        ) $RepoRoot
        Invoke-NativeChecked dotnet @(
            'build', 'SharpTS.sln', '--configuration', 'Release', '--nologo', '--no-restore',
            '-m:1', '-p:NuGetAudit=false'
        ) $RepoRoot
        Invoke-NativeChecked pwsh @(
            '-NoProfile', '-File', (Join-Path $PSScriptRoot 'test-perf-local.ps1')
        ) $RepoRoot
        Invoke-NativeChecked pwsh @(
            '-NoProfile', '-File', $Runner, '-RepositoryRoot', $RepoRoot,
            '-Workloads', ($Workloads -join ','), '-Smoke', '-NoBuild'
        ) $RepoRoot
        Invoke-NativeChecked dotnet @(
            'run', '--configuration', 'Release', '--no-build',
            '--project', 'benchmarks/micro/SharpTS.Microbenchmarks', '--', '--smoke'
        ) $RepoRoot
        $testFilter = 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm'
        if ($GateLevel -eq 'checkpoint') {
            $testFilter += '&FullyQualifiedName~SharpTS.Tests.CompilerTests'
        }
        Invoke-NativeChecked dotnet @(
            'test', 'tests/SharpTS.Tests/SharpTS.Tests.csproj', '--no-build', '--configuration', 'Release',
            '--filter', $testFilter
        ) $RepoRoot

        if ($GateLevel -eq 'full') {
            Invoke-NativeChecked dotnet @(
                'test', 'tests/gui-conformance/SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj',
                '--no-build', '--configuration', 'Release',
                '--filter', 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm'
            ) $RepoRoot
            foreach ($project in @(
                'tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj',
                'tests/conformance/SharpTS.Test262.Worker/SharpTS.Test262.Worker.csproj',
                'tests/conformance/SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj'
            )) {
                Invoke-NativeChecked dotnet @('build', $project, '--configuration', 'Release', '-p:NuGetAudit=false') $RepoRoot
            }
            foreach ($script in @('test-nuget-release.ps1', 'test-gui-version.ps1', 'test-packaged-sdk.ps1')) {
                Invoke-NativeChecked pwsh @('-NoProfile', '-File', (Join-Path $PSScriptRoot $script)) $RepoRoot
            }
            Invoke-NativeChecked pwsh @(
                '-NoProfile', '-File', (Join-Path $PSScriptRoot 'aot-analyzer-report.ps1'), '-EnforceBaseline'
            ) $RepoRoot
        }
    } finally {
        $env:AVALONIA_TELEMETRY_OPTOUT = $oldAvaloniaOptOut
    }
}

function Invoke-WslGate {
    Initialize-WslPaths
    $candidate = Get-RepositoryMetadata $RepoRoot
    $wslHead = @(Invoke-WslChecked (Join-BashCommand @(
        'git', '-C', $WslCandidatePath, 'rev-parse', 'HEAD'
    )) -Capture -WithoutToolchain) -join ''
    if ($wslHead.Trim() -ne $candidate.commit) {
        throw 'The WSL candidate does not match the Windows candidate. Commit and run -Action sync first.'
    }

    Write-Host "=== WSL $GateLevel gate ==="
    $repo = Quote-BashArgument $WslCandidatePath
    $commands = [Collections.Generic.List[string]]::new()
    $commands.Add("cd $repo")
    $commands.Add('export AVALONIA_TELEMETRY_OPTOUT=1')
    $commands.Add((Join-BashCommand @(
        'dotnet', 'restore', 'SharpTS.sln', '-m:1', '-p:RestoreDisableParallel=true', '-p:NuGetAudit=false'
    )))
    $commands.Add((Join-BashCommand @(
        'dotnet', 'build', 'SharpTS.sln', '--configuration', 'Release', '--nologo', '--no-restore',
        '-m:1', '-p:NuGetAudit=false'
    )))
    $commands.Add((Join-BashCommand @('pwsh', '-NoProfile', '-File', 'scripts/test-perf-local.ps1')))
    $commands.Add((Join-BashCommand @(
        'pwsh', '-NoProfile', '-File', 'benchmarks/cross-runtime/run-benchmarks.ps1',
        '-RepositoryRoot', $WslCandidatePath, '-Workloads', ($Workloads -join ','), '-Smoke', '-NoBuild'
    )))
    $commands.Add((Join-BashCommand @(
        'dotnet', 'run', '--configuration', 'Release', '--no-build',
        '--project', 'benchmarks/micro/SharpTS.Microbenchmarks', '--', '--smoke'
    )))
    $testFilter = 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm'
    if ($GateLevel -eq 'checkpoint') {
        $testFilter += '&FullyQualifiedName~SharpTS.Tests.CompilerTests'
    }
    $commands.Add((Join-BashCommand @(
        'dotnet', 'test', 'tests/SharpTS.Tests/SharpTS.Tests.csproj', '--no-build', '--configuration', 'Release',
        '--filter', $testFilter
    )))

    if ($GateLevel -eq 'full') {
        $commands.Add((Join-BashCommand @(
            'dotnet', 'test', 'tests/gui-conformance/SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj',
            '--no-build', '--configuration', 'Release',
            '--filter', 'Category!=LiveNetwork&Category!=LoadSensitive&Category!=npm'
        )))
        foreach ($project in @(
            'tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj',
            'tests/conformance/SharpTS.Test262.Worker/SharpTS.Test262.Worker.csproj',
            'tests/conformance/SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj'
        )) {
            $commands.Add((Join-BashCommand @('dotnet', 'build', $project, '--configuration', 'Release')))
        }
        foreach ($script in @('test-nuget-release.ps1', 'test-gui-version.ps1', 'test-packaged-sdk.ps1')) {
            $commands.Add((Join-BashCommand @('pwsh', '-NoProfile', '-File', "scripts/$script")))
        }
        $commands.Add((Join-BashCommand @('pwsh', '-NoProfile', '-File', 'scripts/aot-analyzer-report.ps1', '-EnforceBaseline')))

        # Linux is the local architecture that can exercise the native compiler.
        # This is intentionally a compact publish/execute gate; CI retains the
        # exhaustive native feature corpus and the ARM/macOS checks.
        $aotRoot = "$WslCandidatePath/.perf-local-aot"
        $commands.Add((Join-BashCommand @(
            'dotnet', 'publish', 'src/SharpTS/SharpTS.csproj', '--configuration', 'Release',
            '-p:PlatformTarget=AnyCPU', '-p:DebugType=None', '-p:DebugSymbols=false',
            '-o', "$aotRoot/managed-runtime"
        )))
        $commands.Add((Join-BashCommand @(
            'dotnet', 'publish', 'src/SharpTS/SharpTS.csproj', '--configuration', 'Release', '-r', 'linux-x64',
            '-p:PublishAot=true', '-p:DebugType=None', '-p:DebugSymbols=false',
            "-p:SharpTSManagedRuntimePayloadPath=$aotRoot/managed-runtime/SharpTS.dll",
            '-o', "$aotRoot/native"
        )))
        $commands.Add((Join-BashCommand @("$aotRoot/native/SharpTS", 'samples/AotCompileGate/main.ts')) +
            " | grep -qx '42'")
    }
    Invoke-WslChecked ($commands -join "`n")
}

function Invoke-LocalGate {
    foreach ($platform in $Platforms) {
        if ($platform -eq 'windows') { Invoke-WindowsGate } else { Invoke-WslGate }
    }
}

switch ($Action) {
    'sync' {
        if ($Platforms -notcontains 'wsl') {
            Write-Host 'Nothing to sync: the selected platforms do not include WSL.'
        } else {
            Sync-WslWorktrees
        }
    }
    'measure' { Invoke-PairedMeasurement }
    'gate' { Invoke-LocalGate }
    'all' {
        if ($Platforms -contains 'wsl') { Sync-WslWorktrees }
        Invoke-LocalGate
        Invoke-PairedMeasurement
    }
}
