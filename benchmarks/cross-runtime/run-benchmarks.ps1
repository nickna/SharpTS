[CmdletBinding()]
param(
    [switch]$Smoke,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir '../..')).Path
$OutputDir = if ($env:OUTPUT_DIR) { $env:OUTPUT_DIR } else { Join-Path ([System.IO.Path]::GetTempPath()) 'bench-results' }
$ScriptsDir = Join-Path $ScriptDir 'scripts'
$SharpTSProject = Join-Path $RepoRoot 'src/SharpTS/SharpTS.csproj'

if (-not (Test-Path -LiteralPath $SharpTSProject -PathType Leaf)) {
    throw "Could not find SharpTS.csproj from runner root '$RepoRoot'"
}

$scripts = @(Get-ChildItem -Path $ScriptsDir -Filter '*.ts' | Sort-Object Name)
if ($scripts.Count -eq 0) {
    throw "No benchmark workloads found in '$ScriptsDir'"
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
if (-not $Smoke) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        $nodeVersion = Invoke-Captured { node -v }
        if ($nodeVersion.ExitCode -eq 0) {
            $nodeVersionFull = ([string]$nodeVersion.Output[0]) -replace '^v', ''
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

# Tag and persist a runtime's BENCH output. If none was produced (a crash, a
# compile/parse error, a missing API), warn loudly and echo the tail of the
# captured output instead of silently leaving a '-' in the results table.
function Emit-Runtime {
    param([string]$Runtime, [object]$Output, [string]$ResultsFile)
    $lines = @($Output)
    $benchLines = @($lines | Where-Object { $_ -match '^BENCH:' })
    if ($benchLines.Count -eq 0) {
        return $false
    }
    $benchLines | ForEach-Object { "$Runtime|$($_ -replace '^BENCH:','')" } | Add-Content $ResultsFile
    return $true
}

function Complete-Runtime {
    param(
        [string]$Benchmark,
        [string]$Runtime,
        [object]$Result,
        [string]$ResultsFile
    )

    $emitted = Emit-Runtime $Runtime $Result.Output $ResultsFile
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

        # --- Interpreter ---
        Write-Host '  [interpreter] running...'
        $interp = Invoke-Captured {
            dotnet run -c Release --no-build --project $SharpTSProject -- $script.FullName
        }
        Complete-Runtime $benchName 'interpreter' $interp $ResultsFile

        # --- Compiled ---
        Write-Host '  [compiled] compiling...'
        $compile = Invoke-Captured {
            dotnet run -c Release --no-build --project $SharpTSProject -- --compile $script.FullName -o $dllPath
        }

        if ($compile.ExitCode -eq 0 -and (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
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

            Write-Host '  [compiled] running...'
            $compiled = Invoke-Captured { dotnet $dllPath }
            Complete-Runtime $benchName 'compiled' $compiled $ResultsFile
        } else {
            Add-Failure "$benchName [compiled] failed to compile (exit code $($compile.ExitCode))"
            Show-Diagnostics 'compiled' $compile.Output
        }

        # --- Node.js ---
        if ($node) {
            Write-Host '  [node] running...'
            $nodeArgs = $nodeFlags + @($script.FullName)
            $nodeResult = Invoke-Captured { & node @nodeArgs }
            Complete-Runtime $benchName 'node' $nodeResult $ResultsFile
        } else {
            Add-Failure "$benchName [node] could not run because Node.js is unavailable"
        }

        # --- Bun ---
        if (Get-Command bun -ErrorAction SilentlyContinue) {
            Write-Host '  [bun] running...'
            $bunResult = Invoke-Captured { & bun run $script.FullName }
            Complete-Runtime $benchName 'bun' $bunResult $ResultsFile
        } else {
            Write-Host '  [bun] not installed, skipping'
        }
    }
} finally {
    Remove-Item -Recurse -Force $compileTmpDir -ErrorAction SilentlyContinue
}

Write-Host ''
if ($Smoke) {
    Write-Host "=== Smoke-compiled $($scripts.Count) benchmark workload(s) ==="
} else {
    Write-Host "=== Results written to $ResultsFile ==="
    Get-Content $ResultsFile
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Warning "=== $($failures.Count) benchmark failure(s) ==="
    foreach ($failure in $failures) { Write-Warning "  $failure" }
    throw "$($failures.Count) benchmark workload/runtime failure(s)"
}
