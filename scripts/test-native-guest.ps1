[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimeIdentifier,
    [string]$CompilerPath = 'src/SharpTS/bin/Release/net10.0/SharpTS.dll',
    [string]$ResultsDirectory = 'artifacts/native-guest',
    [string[]]$Cases = @('language', 'arguments', 'modules', 'collections', 'generators', 'async', 'parameter-defaults', 'exceptions', 'bcl')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = [IO.Path]::GetFullPath($CompilerPath, $root)
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "Compiler missing: $compiler" }
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$hostOs = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$hostArch = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
if ($RuntimeIdentifier -cne "$hostOs-$hostArch") { throw 'Native guest tests require the matching OS and process architecture.' }
$results = Join-Path ([IO.Path]::GetFullPath($ResultsDirectory, $root)) ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $results -Force | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$completed = $false
$linkerEnvironment = $null

function Invoke-OwnedTool([string]$Executable, [string[]]$Arguments, [string]$Log, [bool]$WithoutDotnet = $false, [hashtable]$EnvironmentOverrides = @{}, [int]$TimeoutMilliseconds = 600000) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = $root
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($name in $EnvironmentOverrides.Keys) { $start.Environment[$name] = $EnvironmentOverrides[$name] }
    if ($WithoutDotnet) {
        $start.Environment['PATH'] = ''
        foreach ($name in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT(x86)')) { [void]$start.Environment.Remove($name) }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $started = $false
    $stdout = $null
    $stderr = $null
    try {
        [void]$process.Start()
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) { throw "Owned native-guest process timed out: $Executable" }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 10000)) { throw "Owned native-guest process output did not drain: $Executable" }
        $out = $stdout.GetAwaiter().GetResult()
        $err = $stderr.GetAwaiter().GetResult()
        return @{ exitCode = $process.ExitCode; stdout = $out; stderr = $err; milliseconds = $timer.Elapsed.TotalMilliseconds; peakWorkingSetBytes = $process.PeakWorkingSet64 }
    }
    finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        if ($stdout -and $stderr) {
            [void][Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 10000)
            [IO.File]::WriteAllText("$Log.stdout.txt", $(if ($stdout.IsCompletedSuccessfully) { $stdout.GetAwaiter().GetResult() } else { '[stdout did not drain after process termination]' }))
            [IO.File]::WriteAllText("$Log.stderr.txt", $(if ($stderr.IsCompletedSuccessfully) { $stderr.GetAwaiter().GetResult() } else { '[stderr did not drain after process termination]' }))
        }
        $process.Dispose()
    }
}

function Assert-NativeImage([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($IsWindows) {
        $stream = [IO.MemoryStream]::new($bytes, $false)
        $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $machine = if ($RuntimeIdentifier.EndsWith('arm64')) { 'Arm64' } else { 'Amd64' }
            if ($pe.PEHeaders.CorHeader -or -not $pe.PEHeaders.PEHeader -or $pe.PEHeaders.CoffHeader.Machine.ToString() -cne $machine -or
                ($pe.PEHeaders.CoffHeader.Characteristics -band [Reflection.PortableExecutable.Characteristics]::Dll)) { throw 'Expected a matching native PE executable without CLR metadata.' }
        }
        finally { $pe.Dispose(); $stream.Dispose() }
    }
    elseif ($IsLinux) {
        $machine = if ($RuntimeIdentifier.EndsWith('arm64')) { 183 } else { 62 }
        if ($bytes.Length -lt 32 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0x464c457f -or $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or
            [BitConverter]::ToUInt16($bytes, 18) -ne $machine -or [BitConverter]::ToUInt16($bytes, 16) -notin @(2, 3)) { throw 'Expected a matching native ELF executable.' }
    }
    else {
        $machine = if ($RuntimeIdentifier.EndsWith('arm64')) { 0x0100000c } else { 0x01000007 }
        if ($bytes.Length -lt 32 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0xfeedfacfL -or
            [BitConverter]::ToUInt32($bytes, 4) -ne $machine -or [BitConverter]::ToUInt32($bytes, 12) -ne 2) { throw 'Expected a matching native Mach-O executable.' }
    }
}

$cohort = @{
    language = @{ source = 'language.ts'; expected = "6`n2,4,6`n{`"answer`":42,`"ok`":true}`n42`n"; arguments = @(); exitCode = 0 }
    arguments = @{ source = 'arguments.ts'; expected = "one|two words`n"; arguments = @('one', 'two words'); exitCode = 7 }
    modules = @{ source = 'modules/main.ts'; expected = "42`n"; arguments = @(); exitCode = 0 }
    collections = @{ source = 'collections.ts'; expected = "42`n2`ntrue`n"; arguments = @(); exitCode = 0 }
    generators = @{ source = 'generators.ts'; expected = "6`n"; arguments = @(); exitCode = 0 }
    async = @{ source = 'async.ts'; expected = "42`n"; arguments = @(); exitCode = 0 }
    'parameter-defaults' = @{ source = 'parameter-defaults.ts'; expected = "ReferenceError`nReferenceError`n"; arguments = @(); exitCode = 0 }
    exceptions = @{ source = 'exceptions.ts'; expected = "guest error 42`n"; arguments = @(); exitCode = 0 }
    bcl = @{ source = 'bcl.ts'; expected = "native 6`nfalse`n"; arguments = @(); exitCode = 0 }
}
try {
    if ($Cases.Count -eq 0 -or @($Cases | Select-Object -Unique).Count -ne $Cases.Count) { throw 'A nonempty distinct case selection is required.' }
    foreach ($name in $Cases) {
        if (-not $cohort.ContainsKey($name)) { throw "Unknown native guest case: $name" }
        $case = $cohort[$name]
        $source = Join-Path $root "tests/native-guest/$($case.source)"
        $directory = Join-Path $results $name
        New-Item -ItemType Directory -Path $directory | Out-Null
        $output = Join-Path $directory $(if ($IsWindows) { "$name.exe" } else { $name })
        [string[]]$prefix = @()
        if ([IO.Path]::GetExtension($compiler) -eq '.dll') { $prefix = @($compiler) }
        $command = if ($prefix.Count) { $dotnet } else { $compiler }
        $compile = Invoke-OwnedTool $command ($prefix + @('--no-tsconfig', '--noLib', '--compile', $source, '--target', 'native-aot', '--runtime', $RuntimeIdentifier, '-o', $output, '--verify', '--debug', '--msbuild-errors')) (Join-Path $directory 'compile')
        if ($compile.exitCode -ne 0 -or ($compile.stdout + $compile.stderr) -match '\b(?:warning|error) IL\d+\b|will always throw because:') { throw "Strict native guest publish failed: $name. See $directory" }
        Assert-NativeImage $output
        $execution = Invoke-OwnedTool $output $case.arguments (Join-Path $directory 'execute') $true
        if ($execution.exitCode -ne $case.exitCode -or $execution.stdout.Replace("`r`n", "`n") -cne $case.expected -or $execution.stderr.Length -ne 0) { throw "Native guest output/exit mismatch: $name. See $directory" }
        $rows.Add(@{ name = $name; sourceSha256 = (Get-FileHash -LiteralPath $source).Hash; binarySha256 = (Get-FileHash -LiteralPath $output).Hash; nativeImage = $true; dotnetAbsentFromPath = $true; exitCode = $execution.exitCode; output = $execution.stdout; bytes = (Get-Item -LiteralPath $output).Length; publishMilliseconds = $compile.milliseconds; executionMilliseconds = $execution.milliseconds; peakWorkingSetBytes = $execution.peakWorkingSetBytes })
        Write-Host "Native guest passed: $name ($RuntimeIdentifier)"
    }
    if ($IsWindows -and $Cases -contains 'language') {
        $directory = Join-Path $results 'linker-environment'
        New-Item -ItemType Directory -Path $directory | Out-Null
        $output = Join-Path $results 'language/language.exe'
        $prior = (Get-FileHash -LiteralPath $output).Hash
        $arguments = $prefix + @('--no-tsconfig', '--noLib', '--compile', (Join-Path $root 'tests/native-guest/language.ts'), '--target', 'native-aot', '--runtime', $RuntimeIdentifier, '-o', $output, '--verify', '--debug', '--msbuild-errors')
        # link.exe consumes _LINK_ after its command-line/response-file options.
        # A cache hit must not hide a real failure for these changed inputs.
        $changed = Invoke-OwnedTool $command $arguments (Join-Path $directory 'changed') $false @{
            '_LINK_' = '/DEF:' + (Join-Path $directory 'intentionally-absent.def')
        }
        if ($changed.exitCode -eq 0 -or ($changed.stdout + $changed.stderr) -notmatch 'LNK1104' -or
            $changed.stdout.Contains('(cache hit)') -or (Get-FileHash -LiteralPath $output).Hash -cne $prior) { throw 'Changed linker environment did not refuse publication and preserve prior output.' }
        $repeated = Invoke-OwnedTool $command $arguments (Join-Path $directory 'unchanged')
        $cacheHit = $repeated.stdout.Contains('(cache hit)')
        if ($repeated.exitCode -ne 0 -or ($cacheHit -and (Get-FileHash -LiteralPath $output).Hash -cne $prior)) { throw 'Unchanged native publication did not preserve its successful artifact.' }
        Assert-NativeImage $output
        $repeatedRun = Invoke-OwnedTool $output @() (Join-Path $directory 'execute-unchanged') $true
        if ($repeatedRun.exitCode -ne 0 -or $repeatedRun.stdout.Replace("`r`n", "`n") -cne $cohort.language.expected -or $repeatedRun.stderr.Length) { throw 'Repeated native publication did not execute correctly.' }
        $linkerEnvironment = @{ checked = $true; changedInputsRefused = $true; priorOutputPreserved = $true; unchangedCacheHit = $cacheHit; repeatedNativeVerified = $true; repeatedBinarySha256 = (Get-FileHash -LiteralPath $output).Hash }
        Write-Host 'Native linker environment invalidation passed.'
    }
    $completed = $true
}
finally {
    $sources = @{}
    foreach ($sourceFile in Get-ChildItem (Join-Path $root 'tests/native-guest') -Recurse -File -Filter '*.ts') { $sources[[IO.Path]::GetRelativePath($root, $sourceFile.FullName).Replace('\', '/')] = (Get-FileHash -LiteralPath $sourceFile.FullName).Hash }
    @{ runtimeIdentifier = $RuntimeIdentifier; compilerSha256 = (Get-FileHash -LiteralPath $compiler).Hash; completed = $completed; expectedCases = $Cases.Count; cases = $rows.ToArray(); sources = $sources; linkerEnvironment = $linkerEnvironment } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding utf8NoBOM
}
Write-Host "Native guest corpus passed: $($rows.Count) cases. Evidence: $results"
