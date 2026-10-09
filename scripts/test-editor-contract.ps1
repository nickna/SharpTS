[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'artifacts/editor-contract',
    [string]$NodeExecutable = 'node',
    [switch]$NoBuild,
    [switch]$NoRestore,
    [switch]$NoFormatterInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resultsRoot = [IO.Path]::GetFullPath($ResultsDirectory, $repositoryRoot)
$runDirectory = Join-Path $resultsRoot (Get-Date -Format 'yyyyMMddTHHmmss')
$runDirectory += '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$indexPath = Join-Path $runDirectory 'index.json'
$nodeCommand = (Get-Command $NodeExecutable -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pinnedNode = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.node-version') -Raw).Trim()
$sdkPolicy = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
$commands = [Collections.Generic.List[object]]::new()
$suites = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$cleanup = [Collections.Generic.List[object]]::new()
$script:evidence = [ordered]@{
    schemaVersion = 1
    completed = $false
    startedUtc = [DateTime]::UtcNow.ToString('o')
    repository = $repositoryRoot
    results = $runDirectory
    configuration = $Configuration
    platform = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    node = [ordered]@{ executable = $nodeCommand; pinned = $pinnedNode; actual = $null }
    sdkPolicy = $sdkPolicy
    sourceCommit = $null
    sourceDirty = $null
    commands = $commands
    suites = $suites
    cleanup = $cleanup
    failures = $failures
}

function Save-Evidence {
    $script:evidence | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $indexPath -Encoding utf8NoBOM
}

function Invoke-BoundedProcess {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Executable,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $repositoryRoot,
        [ValidateRange(1, 1200)][int]$TimeoutSeconds = 180,
        [hashtable]$Environment = @{}
    )
    $stdoutPath = Join-Path $runDirectory "$Name.stdout.log"
    $stderrPath = Join-Path $runDirectory "$Name.stderr.log"
    $record = [ordered]@{
        name = $Name; executable = $Executable; arguments = $Arguments; cwd = $WorkingDirectory
        timeoutSeconds = $TimeoutSeconds; startedUtc = [DateTime]::UtcNow.ToString('o')
        stdout = $stdoutPath; stderr = $stderrPath; exitCode = $null
        timedOut = $false; passed = $false; elapsedSeconds = 0; error = $null
    }
    $commands.Add($record)
    Save-Evidence
    Write-Host "> $Executable $($Arguments -join ' ')"
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $started = $false
    $stdoutTask = $null
    $stderrTask = $null
    try {
        $started = $process.Start()
        if (-not $started) { throw 'Process did not start.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $record.timedOut = $true
            $process.Kill($true)
            if (-not $process.WaitForExit(10000)) { throw 'Timed-out process tree did not terminate.' }
        }
        $record.exitCode = $process.ExitCode
        $record.passed = -not $record.timedOut -and $process.ExitCode -eq 0
    }
    catch { $record.error = $_.Exception.Message }
    finally {
        if ($started -and -not $process.HasExited) {
            try {
                $process.Kill($true)
                if (-not $process.WaitForExit(10000)) { throw 'Failed process tree did not settle during cleanup.' }
            }
            catch { $record.passed = $false; $record.error = $_.Exception.Message }
        }
        if ($null -ne $stdoutTask -and $stdoutTask.Wait(5000)) {
            $stdoutTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stdoutPath -Encoding utf8NoBOM
        }
        else { 'Output pipe did not settle before the cleanup deadline.' | Set-Content -LiteralPath $stdoutPath }
        if ($null -ne $stderrTask -and $stderrTask.Wait(5000)) {
            $stderrTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderrPath -Encoding utf8NoBOM
        }
        else { 'Error pipe did not settle before the cleanup deadline.' | Set-Content -LiteralPath $stderrPath }
        $record.elapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        $process.Dispose()
        Save-Evidence
    }
    if (-not $record.passed) {
        Write-Host "FAILED $Name (exit=$($record.exitCode), timeout=$($record.timedOut)); $stderrPath"
        Get-Content -LiteralPath $stderrPath -Tail 20 | Write-Host
    }
    return $record
}

function Invoke-RequiredProcess {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [int]$TimeoutSeconds = 180,
        [string]$WorkingDirectory = $repositoryRoot, [hashtable]$Environment = @{})
    $record = Invoke-BoundedProcess -Name $Name -Executable $Executable -Arguments $Arguments `
        -TimeoutSeconds $TimeoutSeconds -WorkingDirectory $WorkingDirectory -Environment $Environment
    if (-not $record.passed) { throw "$Name failed; see $($record.stderr)." }
    return $record
}

function Remove-IsolatedDirectory([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $runDirectory 'packaged')) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $absolute.StartsWith($expectedParent, $comparison)) {
        throw "Refusing cleanup outside this run's isolated packaged directory: $absolute"
    }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
    $cleanup.Add([ordered]@{ path = $absolute; removed = $true })
}

$originalPath = $env:PATH
$packagedRoot = Join-Path $runDirectory 'packaged'
try {
    # The formatter wrapper invokes node by name. Keep that child invocation pinned too.
    $env:PATH = (Split-Path -Parent $nodeCommand) + [IO.Path]::PathSeparator + $originalPath
    $version = Invoke-RequiredProcess 'node-version' $nodeCommand @('--version')
    $actualNode = (Get-Content -LiteralPath $version.stdout -Raw).Trim().TrimStart('v')
    $script:evidence.node.actual = $actualNode
    if ($actualNode -cne $pinnedNode) { throw "Node $pinnedNode is required; pass -NodeExecutable for an isolated pinned runtime (found $actualNode)." }
    $commit = Invoke-RequiredProcess 'source-commit' 'git' @('rev-parse', 'HEAD')
    $script:evidence.sourceCommit = (Get-Content -LiteralPath $commit.stdout -Raw).Trim()
    $dirty = Invoke-RequiredProcess 'source-status' 'git' @('status', '--porcelain')
    $script:evidence.sourceDirty = -not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $dirty.stdout -Raw))
    $null = Invoke-RequiredProcess 'dotnet-info' 'dotnet' @('--info')
    $null = Invoke-RequiredProcess 'dotnet-runtimes' 'dotnet' @('--list-runtimes')

    if (-not $NoFormatterInstall) {
        $nodeDirectory = Split-Path -Parent $nodeCommand
        $npmCandidates = @((Join-Path $nodeDirectory 'node_modules/npm/bin/npm-cli.js'),
            (Join-Path $nodeDirectory '../lib/node_modules/npm/bin/npm-cli.js'))
        $npmCli = $npmCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (-not $npmCli) { throw 'Cannot find npm-cli.js beside the pinned Node runtime; install with npm ci and use -NoFormatterInstall.' }
        $null = Invoke-RequiredProcess 'acquire-formatter' $nodeCommand @($npmCli, 'ci', '--prefix',
            (Join-Path $repositoryRoot 'tools/formatter-interop'), '--ignore-scripts', '--no-audit', '--no-fund') -TimeoutSeconds 300
    }

    $projects = [ordered]@{
        'language-server' = 'src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj'
        'formatter-evidence' = 'tests/fixtures/FormatterInterop/SharpTS.FormatterInteropEvidence.csproj'
    }
    if (-not $NoBuild) {
        foreach ($name in $projects.Keys) {
            if (-not $NoRestore) {
                $null = Invoke-RequiredProcess "restore-$name" 'dotnet' @('restore', $projects[$name]) -TimeoutSeconds 300
            }
            $null = Invoke-RequiredProcess "build-$name" 'dotnet' @('build', $projects[$name], '--no-restore',
                '--configuration', $Configuration, '--nologo', '--verbosity', 'minimal') -TimeoutSeconds 480
        }
    }
    $server = Join-Path $repositoryRoot "src/SharpTS.LanguageServer/bin/$Configuration/net10.0/SharpTS.LanguageServer.dll"
    $formatterEvidence = Join-Path $repositoryRoot "tests/fixtures/FormatterInterop/bin/$Configuration/net10.0/SharpTS.FormatterInteropEvidence.dll"
    foreach ($binary in @($server, $formatterEvidence)) {
        if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Required build output is missing: $binary" }
    }
    $serverCompiler = Join-Path (Split-Path -Parent $server) 'SharpTS.dll'
    $evidenceCompiler = Join-Path (Split-Path -Parent $formatterEvidence) 'SharpTS.dll'
    $script:evidence['binaries'] = @($server, $serverCompiler, $formatterEvidence, $evidenceCompiler) | ForEach-Object {
        [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    if ((Get-FileHash -LiteralPath $serverCompiler -Algorithm SHA256).Hash -cne
        (Get-FileHash -LiteralPath $evidenceCompiler -Algorithm SHA256).Hash) {
        throw 'Formatter evidence has a stale compiler copy. Build its project with -p:BuildProjectReferences=false before using -NoBuild.'
    }

    foreach ($suite in @('analysis-snapshots', 'semantic-hover', 'semantic-completion',
        'semantic-signatures', 'member-references', 'private-rename')) {
        $record = Invoke-BoundedProcess "protocol-$suite" $nodeCommand @((Join-Path $PSScriptRoot "test-$suite.mjs"), $server) -TimeoutSeconds 180
        $suiteResult = [ordered]@{ name = $suite; passed = $record.passed; report = $null; log = $record.stdout }
        if ($record.passed) {
            try {
                $suiteResult.report = Get-Content -LiteralPath $record.stdout -Raw | ConvertFrom-Json
                $report = $suiteResult.report
                if (-not $report.workspace -or -not (Test-Path -LiteralPath $report.workspace -PathType Container)) {
                    throw 'Result must identify the executed disposable workspace.'
                }
                if ($suite -in @('analysis-snapshots', 'member-references')) {
                    if (@($report.full.PSObject.Properties).Count -eq 0 -or @($report.interopOnly.PSObject.Properties).Count -eq 0) {
                        throw 'Both feature modes must have nonempty results.'
                    }
                }
                elseif (@($report.clients).Count -lt 2 -or
                    @($report.clients | Where-Object { $_.mode -eq 'full' }).Count -eq 0 -or
                    @($report.clients | Where-Object { $_.mode -eq 'interop-only' }).Count -eq 0) {
                    throw 'Both feature modes must have nonempty client results.'
                }
            }
            catch { $suiteResult.passed = $false; $failures.Add("$suite emitted no valid result manifest: $($_.Exception.Message)") }
        }
        if (-not $suiteResult.passed) { $failures.Add("Protocol suite failed: $suite") }
        $suites.Add($suiteResult)
        Save-Evidence
    }

    # The optional formatter and its compiler evidence project remain outside product builds.
    $formatterResults = Join-Path $runDirectory 'formatter'
    $pwsh = (Get-Process -Id $PID).Path
    $record = Invoke-BoundedProcess 'formatter-interop' $pwsh @('-NoProfile', '-NonInteractive', '-File',
        (Join-Path $PSScriptRoot 'test-formatter-interop.ps1'), '-Configuration', $Configuration,
        '-ResultsDirectory', $formatterResults, '-NoBuild') -TimeoutSeconds 720
    $suites.Add([ordered]@{ name = 'formatter-interop'; passed = $record.passed; results = $formatterResults; log = $record.stdout })
    if (-not $record.passed) { $failures.Add('Formatter interoperability failed.') }

    $feed = Join-Path $packagedRoot 'feed'
    $install = Join-Path $packagedRoot 'tool'
    $packages = Join-Path $packagedRoot 'package-cache'
    $cliHome = Join-Path $packagedRoot 'cli-home'
    $httpCache = Join-Path $packagedRoot 'http-cache'
    $workspace = Join-Path $packagedRoot 'installation workspace'
    New-Item -ItemType Directory -Path $feed, $workspace -Force | Out-Null
    $packageVersion = '0.0.0-editor-contract'
    $null = Invoke-RequiredProcess 'pack-language-server' 'dotnet' @('pack', $projects['language-server'],
        '--no-build', '--no-restore', '--configuration', $Configuration, '--output', $feed,
        "-p:MinVerVersionOverride=$packageVersion") -TimeoutSeconds 180
    $package = Join-Path $feed "SharpTS.LanguageServer.$packageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Expected exact package not found: $package" }
    $nugetConfig = Join-Path $packagedRoot 'NuGet.Config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    "<configuration><packageSources><clear /><add key=`"isolated-local`" value=`"$escapedFeed`" /></packageSources></configuration>" |
        Set-Content -LiteralPath $nugetConfig -Encoding utf8NoBOM
    $isolatedEnvironment = @{
        NUGET_PACKAGES = $packages; NUGET_HTTP_CACHE_PATH = $httpCache; DOTNET_CLI_HOME = $cliHome
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    }
    $null = Invoke-RequiredProcess 'install-exact-language-server' 'dotnet' @('tool', 'install',
        'SharpTS.LanguageServer', '--version', $packageVersion, '--tool-path', $install,
        '--configfile', $nugetConfig, '--no-cache') -WorkingDirectory $workspace -Environment $isolatedEnvironment -TimeoutSeconds 180
    $shimName = if ($IsWindows) { 'sharpts-lsp.exe' } else { 'sharpts-lsp' }
    $shim = Join-Path $install $shimName
    $packagedResults = Join-Path $packagedRoot 'protocol'
    $record = Invoke-BoundedProcess 'packaged-tool-protocol' $nodeCommand @((Join-Path $PSScriptRoot 'test-packaged-lsp.mjs'),
        $shim, $packagedResults, $package, $packageVersion) -Environment $isolatedEnvironment -TimeoutSeconds 180
    $suites.Add([ordered]@{ name = 'packaged-tool'; passed = $record.passed; results = $packagedResults
        package = $package; sha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
        version = $packageVersion; shim = $shim; localOnlyConfig = $nugetConfig; log = $record.stdout })
    if (-not $record.passed) { $failures.Add('Installed language-server tool smoke failed.') }
    $script:evidence.completed = $failures.Count -eq 0
}
catch {
    $failures.Add($_.Exception.Message)
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runDirectory 'failure.txt') -Encoding utf8NoBOM
}
finally {
    foreach ($name in @('tool', 'package-cache', 'cli-home', 'http-cache')) {
        try { Remove-IsolatedDirectory (Join-Path $packagedRoot $name) }
        catch { $failures.Add("Isolated tool cleanup failed: $($_.Exception.Message)") }
    }
    $env:PATH = $originalPath
    $script:evidence.completed = $script:evidence.completed -and $failures.Count -eq 0
    $script:evidence['finishedUtc'] = [DateTime]::UtcNow.ToString('o')
    Save-Evidence
}
if (-not $script:evidence.completed) { throw "Editor contract failed. Evidence: $indexPath`n$($failures -join [Environment]::NewLine)" }
Write-Host "Editor contract passed. Evidence: $indexPath"
