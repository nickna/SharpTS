[CmdletBinding()]
param(
    [string]$PackagePath,
    [string]$PackageVersion = "0.0.0-sdk-smoke",
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$NativeRuntimeIdentifier,
    [string]$ResultsDirectory = 'artifacts/sdk-native'
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "sharpts-sdk-smoke-$([Guid]::NewGuid().ToString('N'))"
$feedPath = Join-Path $tempRoot "feed"
$consumerPath = Join-Path $tempRoot "consumer"
$packagesPath = Join-Path $tempRoot "packages"
$cliHomePath = Join-Path $tempRoot "cli-home"
$publishPath = Join-Path $tempRoot "published"
$nativeEvidence = $null
if ($NativeRuntimeIdentifier) {
    $hostOs = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $hostArch = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    if ($NativeRuntimeIdentifier -cne "$hostOs-$hostArch") { throw 'Native SDK smoke requires the matching OS and process architecture.' }
    $nativeEvidence = Join-Path ([IO.Path]::GetFullPath($ResultsDirectory, $repoRoot)) ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $nativeEvidence -Force | Out-Null
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [switch]$CaptureOutput,
        [string]$LogPath
    )

    Push-Location $WorkingDirectory
    try {
        Write-Host "> dotnet $($Arguments -join ' ')"
        if ($CaptureOutput) {
            $lines = & dotnet @Arguments 2>&1
            $exitCode = $LASTEXITCODE
            $text = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
            if ($LogPath) { [IO.File]::WriteAllText($LogPath, $text) }
            if ($text) {
                Write-Host $text
            }
            if ($exitCode -ne 0) {
                throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode."
            }
            return $text
        }

        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Assert-PathExists {
    param([Parameter(Mandatory)] [string]$LiteralPath)
    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Expected file was not created: $LiteralPath"
    }
}

function Assert-SmokeOutput {
    param([Parameter(Mandatory)] [string]$Output)
    if ($Output -notmatch '(?m)^sdk-smoke-ok\r?$') {
        throw "Packaged SDK output did not contain the expected TypeScript result."
    }
}

function Assert-AdaptiveGcProfile {
    param([Parameter(Mandatory)] [string]$RuntimeConfigPath)
    $runtimeConfig = Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json
    $properties = $runtimeConfig.runtimeOptions.configProperties
    if ($properties.'System.GC.Server' -ne $true -or
        $properties.'System.GC.Concurrent' -ne $true -or
        $properties.'System.GC.DynamicAdaptationMode' -ne 1) {
        throw "Runtimeconfig does not contain the adaptive SharpTS GC profile: $RuntimeConfigPath"
    }
}

$oldNuGetPackages = $env:NUGET_PACKAGES
$oldCliHome = $env:DOTNET_CLI_HOME
$oldPath = $env:PATH
$oldTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$oldFirstTimeExperience = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE

try {
    New-Item -ItemType Directory -Force -Path $feedPath, $consumerPath | Out-Null

    if ($PackagePath) {
        $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
        $localPackagePath = Join-Path $feedPath (Split-Path $resolvedPackagePath -Leaf)
        Copy-Item -LiteralPath $resolvedPackagePath -Destination $localPackagePath
    }
    else {
        Invoke-DotNet -WorkingDirectory $repoRoot -Arguments @("restore")
        Invoke-DotNet -WorkingDirectory $repoRoot -Arguments @(
            "build", "src/SharpTS.Sdk.Tasks/SharpTS.Sdk.Tasks.csproj",
            "--configuration", "Release", "--no-restore"
        )
        Invoke-DotNet -WorkingDirectory $repoRoot -Arguments @(
            "publish", "src/SharpTS/SharpTS.csproj",
            "--configuration", "Release", "--no-restore"
        )
        Invoke-DotNet -WorkingDirectory $repoRoot -Arguments @(
            "pack", "src/SharpTS.Sdk/SharpTS.Sdk.csproj",
            "--configuration", "Release", "--no-restore",
            "-p:MinVerVersionOverride=$PackageVersion",
            "--output", $feedPath
        )
        $localPackagePath = Join-Path $feedPath "SharpTS.Sdk.$PackageVersion.nupkg"
    }

    Assert-PathExists $localPackagePath

    $archive = [System.IO.Compression.ZipFile]::OpenRead($localPackagePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        foreach ($requiredEntry in @(
            "Sdk/Sdk.props",
            "Sdk/Sdk.targets",
            "build/SharpTS.Sdk.Tasks.dll",
            "tools/net10.0/any/SharpTS.dll"
        )) {
            if ($entries -notcontains $requiredEntry) {
                throw "SDK package is missing required entry '$requiredEntry'."
            }
        }

        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -like "*.nuspec" } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "SDK package does not contain a nuspec."
        }
        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            $nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        if ($nuspec -match "DotnetTool") {
            throw "SharpTS.Sdk is incorrectly marked as a DotnetTool package."
        }
    }
    finally {
        $archive.Dispose()
    }

    $escapedFeedPath = [System.Security.SecurityElement]::Escape($feedPath)
    Set-Content -LiteralPath (Join-Path $consumerPath "NuGet.Config") -Encoding utf8NoBOM -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeedPath" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath (Join-Path $consumerPath "Smoke.csproj") -Encoding utf8NoBOM -Value @"
<Project Sdk="SharpTS.Sdk/$PackageVersion">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>SharpTS.Sdk.Smoke</AssemblyName>
    <SharpTSEntryPoint>main.ts</SharpTSEntryPoint>
    <SharpTSGcProfile>adaptive</SharpTSGcProfile>
  </PropertyGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $consumerPath "main.ts") -Encoding utf8NoBOM -Value 'console.log("sdk-smoke-ok");'
    # The consumer lives outside the repository, so carry over the repository's
    # supported .NET 10 SDK selection instead of inheriting an unrelated preview
    # SDK that might also be installed on a developer or hosted runner.
    Copy-Item -LiteralPath (Join-Path $repoRoot "global.json") -Destination (Join-Path $tempRoot "global.json")

    $env:NUGET_PACKAGES = $packagesPath
    $env:DOTNET_CLI_HOME = $cliHomePath
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

    # A global dotnet tool normally lives in ~/.dotnet/tools. Excluding that
    # directory proves the SDK invokes its package-local compiler payload.
    $pathSeparator = [Regex]::Escape([System.IO.Path]::PathSeparator)
    $env:PATH = (($oldPath -split $pathSeparator) | Where-Object {
        $_ -and $_ -notmatch '[/\\]\.dotnet[/\\]tools[/\\]?$'
    }) -join [System.IO.Path]::PathSeparator
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "dotnet was not available after removing global-tool directories from PATH."
    }

    $projectPath = Join-Path $consumerPath "Smoke.csproj"
    $nugetConfigPath = Join-Path $consumerPath "NuGet.Config"
    $buildOutput = Join-Path $consumerPath "bin/Release/net10.0/SharpTS.Sdk.Smoke.dll"
    $runtimeConfig = Join-Path $consumerPath "bin/Release/net10.0/SharpTS.Sdk.Smoke.runtimeconfig.json"

    Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @(
        "restore", $projectPath,
        "--configfile", $nugetConfigPath,
        "--packages", $packagesPath
    )
    Assert-PathExists (Join-Path $packagesPath "sharpts.sdk/$PackageVersion/tools/net10.0/any/SharpTS.dll")

    Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @(
        "build", $projectPath, "--configuration", "Release", "--no-restore"
    )
    Assert-PathExists $buildOutput
    Assert-PathExists $runtimeConfig
    Assert-AdaptiveGcProfile $runtimeConfig
    Assert-SmokeOutput (Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @($buildOutput) -CaptureOutput)

    Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @(
        "build", $projectPath, "--configuration", "Release", "--no-restore", "--target:Rebuild"
    )
    Assert-SmokeOutput (Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @($buildOutput) -CaptureOutput)

    Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @(
        "clean", $projectPath, "--configuration", "Release"
    )
    if ((Test-Path -LiteralPath $buildOutput) -or (Test-Path -LiteralPath $runtimeConfig)) {
        throw "dotnet clean left SharpTS build outputs behind."
    }

    Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @(
        "publish", $projectPath, "--configuration", "Release", "--no-restore", "--output", $publishPath
    )
    $publishedAssembly = Join-Path $publishPath "SharpTS.Sdk.Smoke.dll"
    Assert-PathExists $publishedAssembly
    $publishedRuntimeConfig = Join-Path $publishPath "SharpTS.Sdk.Smoke.runtimeconfig.json"
    Assert-PathExists $publishedRuntimeConfig
    Assert-AdaptiveGcProfile $publishedRuntimeConfig
    Assert-SmokeOutput (Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @($publishedAssembly) -CaptureOutput)

    if ($NativeRuntimeIdentifier) {
        $nativeConsumer = Join-Path $tempRoot 'native-consumer'
        $nativeOutput = Join-Path $tempRoot 'native-published'
        New-Item -ItemType Directory -Path $nativeConsumer | Out-Null
        # NativeAOT framework/compiler packages come from NuGet; the candidate SDK
        # itself must still resolve from this isolated local feed and package cache.
        @"
<configuration><packageSources><clear />
<add key="candidate" value="$escapedFeedPath" />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
"@ | Set-Content -LiteralPath (Join-Path $nativeConsumer 'NuGet.Config') -Encoding utf8NoBOM
        @"
<Project Sdk="SharpTS.Sdk/$PackageVersion">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>SharpTS.Native.Smoke</AssemblyName>
    <SharpTSEntryPoint>main.ts</SharpTSEntryPoint>
    <SharpTSTarget>native-aot</SharpTSTarget>
    <RuntimeIdentifier>$NativeRuntimeIdentifier</RuntimeIdentifier>
    <SharpTSVerifyIL>true</SharpTSVerifyIL>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>portable</DebugType>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $nativeConsumer 'NativeSmoke.csproj') -Encoding utf8NoBOM
        'console.log(42); console.log(process.argv.slice(2).join("|")); process.exit(7);' |
            Set-Content -LiteralPath (Join-Path $nativeConsumer 'main.ts') -Encoding utf8NoBOM
        $nativeLog = Invoke-DotNet -WorkingDirectory $nativeConsumer -Arguments @(
            'publish', 'NativeSmoke.csproj', '--configuration', 'Release', '--verbosity', 'normal', '--output', $nativeOutput
        ) -CaptureOutput -LogPath (Join-Path $nativeEvidence 'publish.txt')
        if ($nativeLog -notmatch 'IL verification passed\.') { throw 'Native SDK guest did not pass saved-IL verification.' }
        if ($nativeLog -match '\b(?:warning|error)\s+IL\d{4}\b|ILC:\s*Method\s+.+will always throw because:') {
            throw 'Native SDK publication emitted trim/AOT or invalid-method diagnostics.'
        }
        $nativeExe = Join-Path $nativeOutput ('SharpTS.Native.Smoke' + $(if ($IsWindows) { '.exe' } else { '' }))
        Assert-PathExists $nativeExe
        $guestSymbols = @(Get-ChildItem -LiteralPath (Join-Path $nativeConsumer 'obj') -Recurse -File -Filter 'Guest.pdb')
        if ($guestSymbols.Count -ne 1 -or $guestSymbols[0].Length -lt 4 -or
            [BitConverter]::ToUInt32([IO.File]::ReadAllBytes($guestSymbols[0].FullName), 0) -ne 0x424a5342) {
            throw 'Native SDK publish did not preserve the guest portable PDB.'
        }
        $bytes = [IO.File]::ReadAllBytes($nativeExe)
        if ($IsWindows) {
            $stream = [IO.MemoryStream]::new($bytes, $false)
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            try {
                $machine = if ($NativeRuntimeIdentifier.EndsWith('arm64')) { 'Arm64' } else { 'Amd64' }
                if ($pe.PEHeaders.CorHeader -or -not $pe.PEHeaders.PEHeader -or
                    $pe.PEHeaders.CoffHeader.Machine.ToString() -cne $machine -or
                    ($pe.PEHeaders.CoffHeader.Characteristics -band [Reflection.PortableExecutable.Characteristics]::Dll)) {
                    throw 'Native SDK output is not a matching native PE executable.'
                }
            }
            finally { $pe.Dispose(); $stream.Dispose() }
        }
        elseif ($IsLinux) {
            $machine = if ($NativeRuntimeIdentifier.EndsWith('arm64')) { 183 } else { 62 }
            if ($bytes.Length -lt 32 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0x464c457f -or
                $bytes[4] -ne 2 -or $bytes[5] -ne 1 -or [BitConverter]::ToUInt16($bytes, 18) -ne $machine -or
                [BitConverter]::ToUInt16($bytes, 16) -notin @(2, 3)) { throw 'Native SDK output is not a matching ELF executable.' }
        }
        else {
            $machine = if ($NativeRuntimeIdentifier.EndsWith('arm64')) { 0x0100000c } else { 0x01000007 }
            if ($bytes.Length -lt 32 -or [BitConverter]::ToUInt32($bytes, 0) -ne 0xfeedfacfL -or
                [BitConverter]::ToUInt32($bytes, 4) -ne $machine -or [BitConverter]::ToUInt32($bytes, 12) -ne 2) {
                throw 'Native SDK output is not a matching Mach-O executable.'
            }
        }
        $start = [Diagnostics.ProcessStartInfo]::new($nativeExe)
        $start.WorkingDirectory = $nativeConsumer
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.ArgumentList.Add('one')
        $start.ArgumentList.Add('two words')
        $start.Environment['PATH'] = ''
        foreach ($name in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT_ARM64', 'DOTNET_ROOT(x86)')) { [void]$start.Environment.Remove($name) }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $start
        $started = $false
        try {
            [void]$process.Start(); $started = $true
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(30000) -or
                -not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 10000)) { throw 'Native SDK executable timed out.' }
            $out = $stdout.GetAwaiter().GetResult()
            $err = $stderr.GetAwaiter().GetResult()
            [IO.File]::WriteAllText((Join-Path $nativeEvidence 'execution.stdout.txt'), $out)
            [IO.File]::WriteAllText((Join-Path $nativeEvidence 'execution.stderr.txt'), $err)
            if ($process.ExitCode -ne 7 -or ($out.Replace("`r", '')) -cne "42`none|two words`n" -or $err) {
                throw "Native SDK execution mismatch: exit=$($process.ExitCode), stdout=$out, stderr=$err"
            }
            [ordered]@{ completed = $true; runtimeIdentifier = $NativeRuntimeIdentifier; packageVersion = $PackageVersion;
                packageSha256 = (Get-FileHash -LiteralPath $localPackagePath -Algorithm SHA256).Hash;
                nativeSha256 = (Get-FileHash -LiteralPath $nativeExe -Algorithm SHA256).Hash;
                nativeImage = $true; savedIlVerified = $true; guestPortablePdb = $true;
                dotnetAbsentFromPath = $true; exitCode = 7; stdout = $out } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $nativeEvidence 'summary.json') -Encoding utf8NoBOM
        }
        finally {
            if ($started -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
            $process.Dispose()
        }
        Write-Host "Packaged native SDK smoke passed: $NativeRuntimeIdentifier; evidence: $nativeEvidence"
    }

    Write-Host "Packaged SharpTS.Sdk smoke test passed for version $PackageVersion."
}
finally {
    $env:NUGET_PACKAGES = $oldNuGetPackages
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:PATH = $oldPath
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldTelemetry
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $oldFirstTimeExperience

    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
        $tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedTemp.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedTemp) -notmatch '^sharpts-sdk-smoke-[0-9a-f]{32}$') { throw "Refusing unexpected cleanup path: $resolvedTemp" }
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
