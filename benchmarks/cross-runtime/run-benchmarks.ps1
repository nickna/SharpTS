[CmdletBinding()]
param(
    [switch]$Smoke,
    [switch]$NoBuild,
    [switch]$NoSnapshot,

    [string[]]$Workloads = @(),

    [string[]]$Runtimes = @('interpreter', 'compiled', 'node', 'bun'),

    [ValidateRange(1, 20)]
    [int]$Launches = 1,

    [string]$OutputDirectory,

    # Keep the harness on the candidate branch while measuring a frozen
    # baseline worktree that may predate these filtering options.
    [string]$RepositoryRoot,

    [string]$NodeExecutable = 'node'
)

$ErrorActionPreference = 'Stop'

$HarnessDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = if ($RepositoryRoot) {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
} else {
    (Resolve-Path (Join-Path $HarnessDir '../..')).Path
}
$ScriptDir = Join-Path $RepoRoot 'benchmarks/cross-runtime'
$OutputDir = if ($OutputDirectory) {
    [IO.Path]::GetFullPath($OutputDirectory)
} elseif ($env:OUTPUT_DIR) {
    [IO.Path]::GetFullPath($env:OUTPUT_DIR)
} else {
    Join-Path ([System.IO.Path]::GetTempPath()) 'bench-results'
}
$ScriptsDir = Join-Path $ScriptDir 'scripts'
$SharpTSProject = Join-Path $RepoRoot 'src/SharpTS/SharpTS.csproj'

if (-not (Test-Path -LiteralPath $SharpTSProject -PathType Leaf)) {
    throw "Could not find SharpTS.csproj from runner root '$RepoRoot'"
}

$availableScripts = @(Get-ChildItem -Path $ScriptsDir -Filter '*.ts' | Sort-Object Name)
if ($availableScripts.Count -eq 0) {
    throw "No benchmark workloads found in '$ScriptsDir'"
}

$Workloads = @($Workloads | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($Workloads.Count -gt 0) {
    $requested = [Collections.Generic.HashSet[string]]::new(
        $Workloads,
        [StringComparer]::OrdinalIgnoreCase)
    $scripts = @($availableScripts | Where-Object { $requested.Contains($_.BaseName) })
    $missing = @($Workloads | Where-Object {
        $name = $_
        -not ($scripts | Where-Object { $_.BaseName -eq $name })
    })
    if ($missing.Count -gt 0) {
        throw "Unknown workload(s): $($missing -join ', '). Available: $($availableScripts.BaseName -join ', ')"
    }
} else {
    $scripts = $availableScripts
}

$Runtimes = @($Runtimes | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ } | Select-Object -Unique)
$knownRuntimes = @('interpreter', 'compiled', 'node', 'bun')
$unknownRuntimes = @($Runtimes | Where-Object { $_ -notin $knownRuntimes })
if ($unknownRuntimes.Count -gt 0) {
    throw "Unknown runtime(s): $($unknownRuntimes -join ', '). Available: $($knownRuntimes -join ', ')"
}
if (-not $Smoke -and $Runtimes.Count -eq 0) {
    throw 'At least one runtime must be selected.'
}

$failures = [System.Collections.Generic.List[string]]::new()

function Invoke-Captured {
    param([scriptblock]$Command)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Native commands must be allowed to return a non-zero exit code so one
        # failed runtime does not discard results from the remaining workloads.
        $ErrorActionPreference = 'Continue'
        $output = @(& $Command 2>&1)
        $exitCode = $LASTEXITCODE
        return [pscustomobject]@{ Output = $output; ExitCode = $exitCode }
    } catch {
        return [pscustomobject]@{ Output = @($_); ExitCode = -1 }
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Show-Diagnostics {
    param([string]$Runtime, [object]$Output)

    $diag = @($Output | Where-Object { $_ -and ($_ -notmatch '^BENCH:') } | Select-Object -Last 5)
    foreach ($line in $diag) { Write-Host "      $Runtime> $line" }
}

function Add-Failure {
    param([string]$Message)

    $failures.Add($Message)
    Write-Warning $Message
}

# Build once in Release mode
if (-not $NoBuild) {
    Write-Host '=== Building SharpTS (Release) ==='
    $build = Invoke-Captured { dotnet build $SharpTSProject -c Release --nologo -v quiet }
    if ($build.ExitCode -ne 0) {
        Show-Diagnostics 'build' $build.Output
        throw "Build failed with exit code $($build.ExitCode)"
    }
}

# Detect Node.js version for --experimental-strip-types
$node = $null
$nodeFlags = @()
$nodeVersionForSnapshot = $null
if (-not $Smoke -and $Runtimes -contains 'node') {
    $node = Get-Command $NodeExecutable -ErrorAction SilentlyContinue
    if ($node) {
        $nodeVersion = Invoke-Captured { & $node.Source -v }
        if ($nodeVersion.ExitCode -eq 0) {
            $nodeVersionFull = ([string]$nodeVersion.Output[0]) -replace '^v', ''
            $nodeVersionForSnapshot = "v$nodeVersionFull"
            $nodeMajor = [int]($nodeVersionFull -split '\.')[0]
            if ($nodeMajor -lt 23) {
                $nodeFlags = @('--experimental-strip-types', '--no-warnings')
            }
            Write-Host "=== Node.js v$nodeVersionFull (flags: $(if ($nodeFlags) { $nodeFlags -join ' ' } else { 'none' })) ==="
        } else {
            $node = $null
            Write-Warning 'Node.js version detection failed; Node workloads will be reported as failures'
        }
    } else {
        Write-Warning 'Node.js is not installed; Node workloads will be reported as failures'
    }
}

$bun = if (-not $Smoke -and $Runtimes -contains 'bun') {
    Get-Command bun -ErrorAction SilentlyContinue
} else {
    $null
}
$bunVersionForSnapshot = if ($bun) {
    $detectedBunVersion = Invoke-Captured { & $bun.Source --version }
    if ($detectedBunVersion.ExitCode -eq 0) { [string]$detectedBunVersion.Output[0] } else { $null }
} else {
    $null
}

# Tag and persist a runtime's BENCH output. If none was produced (a crash, a
# compile/parse error, a missing API), warn loudly and echo the tail of the
# captured output instead of silently leaving a '-' in the results table.
function Emit-Runtime {
    param(
        [string]$Benchmark,
        [string]$Runtime,
        [int]$Launch,
        [object]$Output,
        [string]$ResultsFile
    )
    $lines = @($Output)
    $benchLines = @($lines | Where-Object { $_ -match '^BENCH:' })
    if ($benchLines.Count -eq 0) {
        return $false
    }
    $benchLines | ForEach-Object {
        $payload = $_ -replace '^BENCH:', ''
        '{0}|{1}:{2}:{3}' -f $Runtime, $payload, $Benchmark, $Launch
    } | Add-Content $ResultsFile
    return $true
}

function Complete-Runtime {
    param(
        [string]$Benchmark,
        [string]$Runtime,
        [int]$Launch,
        [object]$Result,
        [string]$ResultsFile
    )

    $emitted = Emit-Runtime $Benchmark $Runtime $Launch $Result.Output $ResultsFile
    if ($Result.ExitCode -ne 0) {
        Add-Failure "$Benchmark [$Runtime] exited with code $($Result.ExitCode)"
        Show-Diagnostics $Runtime $Result.Output
    } elseif (-not $emitted) {
        Add-Failure "$Benchmark [$Runtime] produced no BENCH output"
        Show-Diagnostics $Runtime $Result.Output
    }
}

$ResultsFile = $null
if (-not $Smoke) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $ResultsFile = Join-Path $OutputDir 'results.txt'
    '' | Set-Content $ResultsFile
}

$compileTmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "sharpts-bench-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $compileTmpDir -Force | Out-Null

try {
    foreach ($script in $scripts) {
        $benchName = $script.BaseName
        Write-Host ''
        Write-Host "--- $benchName ---"

        $compiledDir = Join-Path $compileTmpDir $benchName
        New-Item -ItemType Directory -Path $compiledDir -Force | Out-Null
        $dllPath = Join-Path $compiledDir "$benchName.dll"

        if ($Smoke) {
            Write-Host '  [smoke] compiling...'
            $compile = Invoke-Captured {
                dotnet run -c Release --no-build --project $SharpTSProject -- --compile $script.FullName -o $dllPath
            }
            if ($compile.ExitCode -eq 0 -and (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
                Write-Host '  [smoke] passed'
            } else {
                Add-Failure "$benchName [smoke] failed to compile (exit code $($compile.ExitCode))"
                Show-Diagnostics 'smoke' $compile.Output
            }
            continue
        }

        $compiledReady = $false
        if ($Runtimes -contains 'compiled') {
            Write-Host '  [compiled] compiling...'
            $compile = Invoke-Captured {
                dotnet run -c Release --no-build --project $SharpTSProject -- --compile $script.FullName -o $dllPath
            }

            if ($compile.ExitCode -eq 0 -and (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
                $compiledReady = $true
                $rcPath = Join-Path $compiledDir "$benchName.runtimeconfig.json"
                if (-not (Test-Path $rcPath)) {
                    @'
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "10.0.0"
    }
  }
}
'@ | Set-Content $rcPath
                }
            } else {
                Add-Failure "$benchName [compiled] failed to compile (exit code $($compile.ExitCode))"
                Show-Diagnostics 'compiled' $compile.Output
            }
        }

        for ($launch = 1; $launch -le $Launches; $launch++) {
            $offset = ($launch - 1) % $Runtimes.Count
            $orderedRuntimes = @($Runtimes[$offset..($Runtimes.Count - 1)])
            if ($offset -gt 0) { $orderedRuntimes += $Runtimes[0..($offset - 1)] }

            foreach ($runtime in $orderedRuntimes) {
                Write-Host "  [$runtime] launch $launch/$Launches..."
                switch ($runtime) {
                    'interpreter' {
                        $result = Invoke-Captured {
                            dotnet run -c Release --no-build --project $SharpTSProject -- $script.FullName
                        }
                        Complete-Runtime $benchName 'interpreter' $launch $result $ResultsFile
                    }
                    'compiled' {
                        if ($compiledReady) {
                            $result = Invoke-Captured { dotnet $dllPath }
                            Complete-Runtime $benchName 'compiled' $launch $result $ResultsFile
                        }
                    }
                    'node' {
                        if ($node) {
                            $nodeArgs = $nodeFlags + @($script.FullName)
                            $result = Invoke-Captured { & $node.Source @nodeArgs }
                            Complete-Runtime $benchName 'node' $launch $result $ResultsFile
                        } else {
                            Add-Failure "$benchName [node] could not run because Node.js is unavailable"
                        }
                    }
                    'bun' {
                        if ($bun) {
                            $result = Invoke-Captured { & bun run $script.FullName }
                            Complete-Runtime $benchName 'bun' $launch $result $ResultsFile
                        } else {
                            Write-Host '  [bun] not installed, skipping'
                        }
                    }
                }
            }
        }
    }
} finally {
    Remove-Item -Recurse -Force $compileTmpDir -ErrorAction SilentlyContinue
}

Write-Host ''
if ($Smoke) {
    Write-Host "=== Smoke-compiled $($scripts.Count) benchmark workload(s) ==="
} else {
    if (-not $NoSnapshot) {
        $SnapshotFile = Join-Path $OutputDir 'snapshot.json'
        & (Join-Path $HarnessDir 'export-snapshot.ps1') `
            -ResultsFile $ResultsFile `
            -OutputFile $SnapshotFile `
            -RepositoryRoot $RepoRoot `
            -SelectedRuntimes $Runtimes `
            -NodeVersion $nodeVersionForSnapshot `
            -BunVersion $bunVersionForSnapshot
        Write-Host "=== Structured snapshot written to $SnapshotFile ==="
    }
    Write-Host "=== Results written to $ResultsFile ==="
    Get-Content $ResultsFile
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Warning "=== $($failures.Count) benchmark failure(s) ==="
    foreach ($failure in $failures) { Write-Warning "  $failure" }
    throw "$($failures.Count) benchmark workload/runtime failure(s)"
}
