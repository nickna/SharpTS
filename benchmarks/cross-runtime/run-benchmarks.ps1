$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot = Split-Path -Parent $ScriptDir
$OutputDir = if ($env:OUTPUT_DIR) { $env:OUTPUT_DIR } else { Join-Path ([System.IO.Path]::GetTempPath()) 'bench-results' }
$ScriptsDir = Join-Path $ScriptDir 'scripts'

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$ResultsFile = Join-Path $OutputDir 'results.txt'
'' | Set-Content $ResultsFile

# Build once in Release mode
Write-Host '=== Building SharpTS (Release) ==='
dotnet build (Join-Path $RepoRoot 'src/SharpTS/SharpTS.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# Detect Node.js version for --experimental-strip-types
$nodeVersionFull = (node -v) -replace '^v', ''
$nodeMajor = [int]($nodeVersionFull -split '\.')[0]
$nodeFlags = @()
if ($nodeMajor -lt 23) {
    $nodeFlags = @('--experimental-strip-types', '--no-warnings')
}
Write-Host "=== Node.js v$nodeVersionFull (flags: $(if ($nodeFlags) { $nodeFlags -join ' ' } else { 'none' })) ==="

# Tag and persist a runtime's BENCH output. If none was produced (a crash, a
# compile/parse error, a missing API), warn loudly and echo the tail of the
# captured output instead of silently leaving a '-' in the results table.
function Emit-Runtime {
    param([string]$Runtime, [object]$Output, [string]$ResultsFile)
    $lines = @($Output)
    $benchLines = @($lines | Where-Object { $_ -match '^BENCH:' })
    if ($benchLines.Count -eq 0) {
        Write-Warning "[$Runtime] produced no BENCH output (treated as failure)"
        $diag = @($lines | Where-Object { $_ -and ($_ -notmatch '^BENCH:') } | Select-Object -Last 5)
        foreach ($d in $diag) { Write-Host "      $Runtime> $d" }
        return
    }
    $benchLines | ForEach-Object { "$Runtime|$($_ -replace '^BENCH:','')" } | Add-Content $ResultsFile
}

$compileTmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "sharpts-bench-$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $compileTmpDir -Force | Out-Null

try {
    foreach ($script in Get-ChildItem -Path $ScriptsDir -Filter '*.ts') {
        $benchName = $script.BaseName
        Write-Host ''
        Write-Host "--- $benchName ---"

        # --- Interpreter ---
        Write-Host '  [interpreter] running...'
        $interpOutput = dotnet run -c Release --no-build --project (Join-Path $RepoRoot 'src/SharpTS/SharpTS.csproj') -- $script.FullName 2>&1
        Emit-Runtime 'interpreter' $interpOutput $ResultsFile

        # --- Compiled ---
        Write-Host '  [compiled] compiling...'
        $compiledDir = Join-Path $compileTmpDir $benchName
        New-Item -ItemType Directory -Path $compiledDir -Force | Out-Null
        $dllPath = Join-Path $compiledDir "$benchName.dll"
        $compileOutput = dotnet run -c Release --no-build --project (Join-Path $RepoRoot 'src/SharpTS/SharpTS.csproj') -- --compile $script.FullName -o $dllPath 2>&1

        if (Test-Path $dllPath) {
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
            $compiledOutput = dotnet $dllPath 2>&1
            Emit-Runtime 'compiled' $compiledOutput $ResultsFile
        } else {
            Write-Warning "[compiled] FAILED to compile $benchName"
            $diag = @($compileOutput | Select-Object -Last 5)
            foreach ($d in $diag) { Write-Host "      compiled> $d" }
        }

        # --- Node.js ---
        Write-Host '  [node] running...'
        $nodeArgs = $nodeFlags + @($script.FullName)
        $nodeOutput = & node @nodeArgs 2>&1
        Emit-Runtime 'node' $nodeOutput $ResultsFile

        # --- Bun ---
        if (Get-Command bun -ErrorAction SilentlyContinue) {
            Write-Host '  [bun] running...'
            $bunOutput = & bun run $script.FullName 2>&1
            Emit-Runtime 'bun' $bunOutput $ResultsFile
        } else {
            Write-Host '  [bun] not installed, skipping'
        }
    }
} finally {
    Remove-Item -Recurse -Force $compileTmpDir -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "=== Results written to $ResultsFile ==="
Get-Content $ResultsFile
