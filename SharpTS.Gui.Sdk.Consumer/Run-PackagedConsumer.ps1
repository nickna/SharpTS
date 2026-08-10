param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$CandidatePackage,
    [switch]$PackageOnly,
    [switch]$RealWindow,
    [switch]$PublishOnly,
    [switch]$NativeAot,
    [switch]$EnforcePerformanceBudgets
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$supportedPlatformsPath = Join-Path $repositoryRoot "SharpTS.Gui.Sdk\Sdk\SupportedPlatforms.props"
[xml]$supportedPlatforms = Get-Content -LiteralPath $supportedPlatformsPath -Raw
$supportedRuntimeAssetDirectories = @{}
$supportedRuntimeIdentifiers = @($supportedPlatforms.SelectNodes("//SharpTSGuiSupportedRuntimeIdentifier") |
    ForEach-Object {
        $declaredRuntimeIdentifier = $_.GetAttribute("Include")
        $supportedRuntimeAssetDirectories[$declaredRuntimeIdentifier] = [string]$_.RuntimeAssetDirectory
        $declaredRuntimeIdentifier
    })
if ($RuntimeIdentifier -notin $supportedRuntimeIdentifiers) {
    throw "SharpTS.Gui.Sdk supports only $($supportedRuntimeIdentifiers -join ', '); got '$RuntimeIdentifier'."
}
$versionInfo = & (Join-Path $repositoryRoot "scripts\get-gui-preview-version.ps1")
$version = $versionInfo.Version
$isWindowsRid = $RuntimeIdentifier.StartsWith("win-", [StringComparison]::Ordinal)
$isMacOsRid = $RuntimeIdentifier.StartsWith("osx-", [StringComparison]::Ordinal)
$platformArtifactName = if ($isWindowsRid) { "windows-preview" } else { "macos-preview" }
$artifactRoot = Join-Path $repositoryRoot "artifacts\$platformArtifactName\$RuntimeIdentifier"
$feed = Join-Path $artifactRoot "feed"
$packageCache = Join-Path $artifactRoot "packages"
$consumerRoot = Join-Path $artifactRoot "consumer with spaces"
$directoryPublishRoot = Join-Path $artifactRoot "published directory"
$singleFilePublishRoot = Join-Path $artifactRoot "published single file"
$nativeAotPublishRoot = Join-Path $artifactRoot "published native aot"
$aotHostPublishRoot = Join-Path $repositoryRoot "SharpTS.Gui.Host\bin\Release\net10.0\aot-publish"
$templateHive = Join-Path $artifactRoot "template hive"
$templateRoot = Join-Path $artifactRoot "template app with spaces"
$templatePublishRoot = Join-Path $artifactRoot "template compiled-only publish"
$cliRoot = Join-Path $artifactRoot "cli app with spaces"
$cliDirectoryPublishRoot = Join-Path $artifactRoot "cli published directory"
$cliSingleFilePublishRoot = Join-Path $artifactRoot "cli published single file"
$osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
$canExecute = -not $PublishOnly -and (
    ($isWindowsRid -and [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) -or
    ($isMacOsRid -and [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX))) -and (
    ($RuntimeIdentifier.EndsWith("-x64", [StringComparison]::Ordinal) -and $osArchitecture -eq "X64") -or
    ($RuntimeIdentifier.EndsWith("-arm64", [StringComparison]::Ordinal) -and $osArchitecture -eq "Arm64"))
$consumerExecutableName = "SharpTS.Gui.Sdk.Consumer" + $(if ($isWindowsRid) { ".exe" } else { "" })
$cliExecutableName = "cli_app_with_spaces" + $(if ($isWindowsRid) { ".exe" } else { "" })
$candidatePackagePath = $null
if (-not [string]::IsNullOrWhiteSpace($CandidatePackage)) {
    $candidatePackagePath = [IO.Path]::GetFullPath($CandidatePackage, $repositoryRoot)
    if (-not (Test-Path -LiteralPath $candidatePackagePath -PathType Leaf)) {
        throw "Candidate GUI SDK package does not exist: $candidatePackagePath"
    }
    if ($candidatePackagePath.StartsWith(
        [IO.Path]::GetFullPath($artifactRoot) + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "CandidatePackage must be outside the per-RID artifact directory, which the harness recreates."
    }
}

function Invoke-DotNet([string[]]$Arguments, [string]$WorkingDirectory = $repositoryRoot) {
    Push-Location $WorkingDirectory
    try {
        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-Application([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    try {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "$Executable $($Arguments -join ' ') failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }
}

function RendererTrace($Events) {
    @($Events | Where-Object {
        $_.Stage -like "reconcile-*" -or
        $_.Stage -like "view-render-*" -or
        $_.Stage -in @(
            "mount", "render-commit", "subscribe", "unsubscribe", "ref-attach", "ref-detach",
            "coalesced-update-complete", "dependency-switch-complete", "reactive-update-complete",
            "forms-events-complete", "transient-ref-cleaned")
    } | ForEach-Object { "$($_.Stage)|$($_.Detail)" })
}

if (Test-Path $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $feed, $consumerRoot | Out-Null

Invoke-DotNet @("restore", "SharpTS.Sdk.Tasks\SharpTS.Sdk.Tasks.csproj")
Invoke-DotNet @("restore", "SharpTS.Gui.Host\SharpTS.Gui.Host.csproj")
Invoke-DotNet @("build", "SharpTS.Sdk.Tasks\SharpTS.Sdk.Tasks.csproj", "-c", "Release", "--no-restore")
Invoke-DotNet @("publish", "SharpTS.Gui.Host\SharpTS.Gui.Host.csproj", "-c", "Release", "--self-contained", "false", "--no-restore")
if (Test-Path -LiteralPath $aotHostPublishRoot) {
    Remove-Item -LiteralPath $aotHostPublishRoot -Recurse -Force
}

function Measure-Application([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($startInfo)
    [long]$peakWorkingSetBytes = 0
    try {
        while (-not $process.WaitForExit(10)) {
            $process.Refresh()
            $peakWorkingSetBytes = [Math]::Max($peakWorkingSetBytes, $process.WorkingSet64)
        }
        $stopwatch.Stop()
        if ($process.ExitCode -ne 0) {
            throw "$Executable $($Arguments -join ' ') failed with exit code $($process.ExitCode)."
        }
        return [pscustomobject]@{
            ElapsedMilliseconds = $stopwatch.Elapsed.TotalMilliseconds
            PeakWorkingSetBytes = $peakWorkingSetBytes
        }
    }
    finally {
        $process.Dispose()
    }
}
Invoke-DotNet @(
    "publish", "SharpTS.Gui.Host\SharpTS.Gui.Host.csproj", "-c", "Release",
    "--self-contained", "false", "--no-restore", "-p:SharpTSGuiHostLibrary=true",
    "-o", $aotHostPublishRoot)
$packagePath = Join-Path $feed "SharpTS.Gui.Sdk.$version.nupkg"
if ($null -ne $candidatePackagePath) {
    Copy-Item -LiteralPath $candidatePackagePath -Destination $packagePath
}
else {
    Invoke-DotNet @("restore", "SharpTS.Gui.Sdk\SharpTS.Gui.Sdk.csproj")
    Invoke-DotNet @("pack", "SharpTS.Gui.Sdk\SharpTS.Gui.Sdk.csproj", "-c", "Release", "--no-restore", "-o", $feed, "-p:MinVerVersionOverride=$version")
}
if (-not (Test-Path $packagePath)) {
    throw "GUI SDK package was not created: $packagePath"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entryNames = @($archive.Entries | ForEach-Object FullName)
    foreach ($required in @(
        "Sdk/Sdk.props",
        "Sdk/Sdk.targets",
        "Sdk/SupportedPlatforms.props",
        "build/SharpTS.Sdk.Tasks.dll",
        "gui/index.ts",
        "gui/jsx-runtime.ts",
        "gui/testing.ts",
        "launcher/Launcher.cs",
        "content/Templates/sharpts-gui/.template.config/template.json",
        "content/Templates/sharpts-gui/SharpTSGuiApp.csproj",
        "content/Templates/sharpts-gui/headless.tests.tsx",
        "tools/net10.0/any/host/SharpTS.Gui.Host.dll",
        "tools/net10.0/any/host/SharpTS.dll",
        "tools/net10.0/any/host/SharpTS.Gui.dll",
        "tools/net10.0/any/aot-host/SharpTS.Gui.Host.dll",
        "tools/net10.0/any/aot-host/SharpTS.Hosting.Abstractions.dll",
        "tools/net10.0/any/aot-host/SharpTS.Gui.dll"
    )) {
        if ($entryNames -notcontains $required) {
            throw "GUI SDK package is missing '$required'."
        }
    }
    $expectedGuiEntries = @(
        "gui/control-docs.generated.json",
        "gui/control-surface.generated.ts",
        "gui/devtools.ts",
        "gui/index.ts",
        "gui/jsx-dev-runtime.ts",
        "gui/jsx-runtime.ts",
        "gui/package.json",
        "gui/runtime-types.ts",
        "gui/runtime.ts",
        "gui/testing.ts"
    ) | Sort-Object
    $actualGuiEntries = @($entryNames | Where-Object { $_.StartsWith("gui/", [StringComparison]::Ordinal) } | Sort-Object)
    if (Compare-Object $expectedGuiEntries $actualGuiEntries -SyncWindow 0) {
        throw "GUI SDK package contains an unexpected gui/ payload."
    }
    if ($entryNames -match "(^|/)Fixtures/" -or
        $entryNames -match "SharpTS.Gui.Conformance.Tests" -or
        $entryNames -match "SharpTS.Gui.ConformanceSupport") {
        throw "GUI SDK package contains repository-only conformance support."
    }
    if ($entryNames -match "native/runtimes/") {
        throw "GUI SDK package contains a duplicated native-runtime path."
    }
    if ($entryNames -match "/runtimes/(linux|browser|maccatalyst|ios|android)(-|/)") {
        throw "GUI SDK package contains an unsupported native runtime asset."
    }
    foreach ($rid in $supportedRuntimeIdentifiers) {
        $runtimeAssetDirectory = $supportedRuntimeAssetDirectories[$rid]
        if (-not ($entryNames -match "/runtimes/$runtimeAssetDirectory/native/")) {
            throw "GUI SDK package is missing native runtime assets for '$rid'."
        }
    }
    foreach ($asset in @("Sdk/Sdk.props", "Sdk/Sdk.targets")) {
        $entry = $archive.GetEntry($asset)
        $reader = [IO.StreamReader]::new($entry.Open())
        try {
            $text = $reader.ReadToEnd()
            if ($text -match "[A-Za-z]:\\" -or $text -match "\.\.\\SharpTS.Gui.Host") {
                throw "GUI SDK build asset '$asset' contains a repository path."
            }
        }
        finally {
            $reader.Dispose()
        }
    }
}
finally {
    $archive.Dispose()
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
Write-Host "GUI SDK candidate: $packagePath ($((Get-Item -LiteralPath $packagePath).Length) bytes, SHA-256 $packageHash)"
if ($PackageOnly) {
    Write-Host "SharpTS.Gui.Sdk candidate package audit passed."
    return
}

$env:NUGET_PACKAGES = $packageCache

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "SharpTS.Gui.Sdk.Consumer.csproj") -Destination $consumerRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "main.tsx") -Destination $consumerRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "tsconfig.json") -Destination $consumerRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Directory.Build.props") -Destination $consumerRoot
$escapedFeed = [Security.SecurityElement]::Escape($feed)
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="desktop-preview" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
[IO.File]::WriteAllText((Join-Path $consumerRoot "NuGet.config"), $nugetConfig)

Invoke-DotNet @("new", "--debug:custom-hive", $templateHive, "install", $packagePath)
Invoke-DotNet @(
    "new", "--debug:custom-hive", $templateHive, "sharpts-gui",
    "-n", "PackagedTemplateApp", "-o", $templateRoot)
[IO.File]::WriteAllText((Join-Path $templateRoot "Directory.Build.props"), "<Project />")
[IO.File]::WriteAllText((Join-Path $templateRoot "NuGet.config"), $nugetConfig)
$templateProject = Join-Path $templateRoot "PackagedTemplateApp.csproj"
Invoke-DotNet @("restore", $templateProject, "--configfile", (Join-Path $templateRoot "NuGet.config")) $templateRoot
Invoke-DotNet @("build", $templateProject, "-c", $Configuration, "--no-restore") $templateRoot
foreach ($mode in @("interpreted", "compiled")) {
    Invoke-DotNet @(
        "run", "--project", $templateProject, "-c", $Configuration, "--no-restore",
        "-p:SharpTSEntryPoint=headless.tests.tsx", "--", "--mode", $mode, "--headless") $templateRoot
}

function Assert-GuestTrace([string]$Path, [string]$Scenario) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Scenario did not produce an execution trace."
    }
    $events = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if (@($events | Where-Object Stage -eq "guest-init-end").Count -ne 1) {
        throw "$Scenario did not complete guest initialization."
    }
}
Invoke-DotNet @("clean", $templateProject, "-c", $Configuration) $templateRoot
Invoke-DotNet @(
    "restore", $templateProject, "-r", $RuntimeIdentifier,
    "--configfile", (Join-Path $templateRoot "NuGet.config")) $templateRoot
Invoke-DotNet @(
    "publish", $templateProject, "-c", $Configuration, "-r", $RuntimeIdentifier,
    "--self-contained", "false", "--no-restore", "-p:SharpTSGuiPublishMode=Directory",
    "-p:SharpTSGuiIncludeSourcePayload=false", "-p:SharpTSEntryPoint=headless.tests.tsx",
    "-o", $templatePublishRoot) $templateRoot
$templateSourcePayload = @(Get-ChildItem -LiteralPath $templatePublishRoot -Recurse -File | Where-Object {
    $_.Extension -in @(".ts", ".tsx") -or $_.FullName -match "node_modules"
})
if ($templateSourcePayload.Count -ne 0) {
    throw "Compiled-only template publish retained source payload: $($templateSourcePayload.FullName -join ', ')"
}
if ($canExecute) {
    $templateInstallSnapshot = @(Get-ChildItem -LiteralPath $templatePublishRoot -Recurse -File | ForEach-Object {
        "$($_.FullName.Substring($templatePublishRoot.Length))|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
    })
    Invoke-DotNet @((Join-Path $templatePublishRoot "PackagedTemplateApp.dll"), "--headless") $templatePublishRoot
    $templateInstallAfter = @(Get-ChildItem -LiteralPath $templatePublishRoot -Recurse -File | ForEach-Object {
        "$($_.FullName.Substring($templatePublishRoot.Length))|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
    })
    if (Compare-Object $templateInstallSnapshot $templateInstallAfter -SyncWindow 0) {
        throw "Published template application wrote into its installation directory at runtime."
    }
}

$consumerProject = Join-Path $consumerRoot "SharpTS.Gui.Sdk.Consumer.csproj"
Invoke-DotNet @("restore", $consumerProject, "--configfile", (Join-Path $consumerRoot "NuGet.config")) $consumerRoot
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot
$guestAssembly = Join-Path $consumerRoot "bin\$Configuration\net10.0\SharpTS.Gui.Guest.dll"
$firstGuestWrite = (Get-Item -LiteralPath $guestAssembly).LastWriteTimeUtc
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot
if ((Get-Item -LiteralPath $guestAssembly).LastWriteTimeUtc -ne $firstGuestWrite) {
    throw "Incremental GUI build recreated an unchanged guest assembly."
}
$consumerSource = Join-Path $consumerRoot "main.tsx"
[IO.File]::AppendAllText($consumerSource, [Environment]::NewLine)
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot
$sourceGuestWrite = (Get-Item -LiteralPath $guestAssembly).LastWriteTimeUtc
if ($sourceGuestWrite -le $firstGuestWrite) {
    throw "Incremental GUI build ignored a changed TypeScript source."
}
Invoke-DotNet @(
    "build", $consumerProject, "-c", $Configuration, "--no-restore",
    "-p:SharpTSVerifyIL=false") $consumerRoot
if ((Get-Item -LiteralPath $guestAssembly).LastWriteTimeUtc -le $sourceGuestWrite) {
    throw "Incremental GUI build ignored a changed SDK compilation property."
}
Invoke-DotNet @("clean", $consumerProject, "-c", $Configuration) $consumerRoot
if (Test-Path (Join-Path $consumerRoot "bin\$Configuration\net10.0\SharpTS.Gui.Host.dll")) {
    throw "GUI SDK clean left the packaged host in the output directory."
}
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot

$buildOutput = Join-Path $consumerRoot "bin\$Configuration\net10.0"
$buildLauncher = Join-Path $buildOutput "SharpTS.Gui.Sdk.Consumer.dll"
$dotnetRunTrace = Join-Path $artifactRoot "dotnet-run-interpreted-trace.json"
Invoke-DotNet @("run", "--project", $consumerProject, "-c", $Configuration, "--no-build", "--", "--mode", "interpreted", "--headless", "--trace", $dotnetRunTrace, "--", "--smoke-close") $consumerRoot
$buildTraces = @{}
foreach ($mode in @("interpreted", "compiled")) {
    $tracePath = Join-Path $artifactRoot "build-$mode-trace.json"
    Invoke-DotNet @($buildLauncher, "--mode", $mode, "--headless", "--trace", $tracePath, "--", "--smoke-close") $buildOutput
    $buildTraces[$mode] = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
}
if (Compare-Object (RendererTrace $buildTraces["interpreted"]) (RendererTrace $buildTraces["compiled"]) -SyncWindow 0) {
    throw "Packaged interpreted and compiled renderer traces differ."
}

Invoke-DotNet @("restore", $consumerProject, "-r", $RuntimeIdentifier, "--configfile", (Join-Path $consumerRoot "NuGet.config")) $consumerRoot
Invoke-DotNet @(
    "publish", $consumerProject, "-c", $Configuration, "-r", $RuntimeIdentifier,
    "--self-contained", "false", "--no-restore", "-p:SharpTSGuiPublishMode=Directory", "-o", $directoryPublishRoot) $consumerRoot

$directoryLauncher = Join-Path $directoryPublishRoot "SharpTS.Gui.Sdk.Consumer.dll"
if ($canExecute) {
    foreach ($mode in @("interpreted", "compiled")) {
        $tracePath = Join-Path $artifactRoot "published-directory-$mode-trace.json"
        Invoke-DotNet @($directoryLauncher, "--mode", $mode, "--headless", "--trace", $tracePath, "--", "--smoke-close") $directoryPublishRoot
        if ($RealWindow) {
            $windowTracePath = Join-Path $artifactRoot "published-directory-$mode-window-trace.json"
            Invoke-DotNet @($directoryLauncher, "--mode", $mode, "--trace", $windowTracePath, "--", "--smoke-close") $directoryPublishRoot
        }
    }
}

Invoke-DotNet @(
    "publish", $consumerProject, "-c", $Configuration, "-r", $RuntimeIdentifier,
    "--self-contained", "true", "--no-restore", "-p:SharpTSGuiPublishMode=SingleFile", "-o", $singleFilePublishRoot) $consumerRoot

$singleFile = Join-Path $singleFilePublishRoot $consumerExecutableName
if (-not (Test-Path $singleFile)) {
    throw "Single-file GUI executable was not created: $singleFile"
}
$distributionFiles = @(Get-ChildItem -LiteralPath $singleFilePublishRoot -File)
if ($distributionFiles.Count -ne 1 -or $distributionFiles[0].Name -ne $consumerExecutableName) {
    throw "Single-file publish produced unexpected sidecar files: $($distributionFiles.Name -join ', ')"
}

if ($canExecute) {
    $oldDotnetRoot = $env:DOTNET_ROOT
    $env:DOTNET_ROOT = Join-Path $artifactRoot "intentionally missing dotnet"
    try {
        $singleTrace = Join-Path $artifactRoot "single-file-compiled-trace.json"
        Invoke-Application $singleFile @("--headless", "--trace", $singleTrace, "--", "--smoke-close") $singleFilePublishRoot
        if ($RealWindow) {
            $singleWindowTrace = Join-Path $artifactRoot "single-file-compiled-window-trace.json"
            Invoke-Application $singleFile @("--trace", $singleWindowTrace, "--", "--smoke-close") $singleFilePublishRoot
        }
    }
    finally {
        $env:DOTNET_ROOT = $oldDotnetRoot
    }

    $diagnosticLog = Join-Path $artifactRoot "single-file-error.log"
    $oldErrorLog = $env:SHARPTS_GUI_ERROR_LOG
    $env:SHARPTS_GUI_ERROR_LOG = $diagnosticLog
    try {
        $interpretedOutput = & $singleFile --mode interpreted --headless 2>&1 | Out-String
        $interpretedExitCode = $LASTEXITCODE
    }
    finally {
        $env:SHARPTS_GUI_ERROR_LOG = $oldErrorLog
    }
    $interpretedDiagnostic = $interpretedOutput
    if (Test-Path -LiteralPath $diagnosticLog) {
        $interpretedDiagnostic += Get-Content -LiteralPath $diagnosticLog -Raw
    }
    if ($interpretedExitCode -eq 0 -or $interpretedDiagnostic -notmatch "contains only the compiled guest") {
        throw "Single-file interpreted mode did not produce the expected diagnostic.`n$interpretedDiagnostic"
    }
}

if ($NativeAot) {
    Invoke-DotNet @(
        "publish", $consumerProject, "-c", $Configuration, "-r", $RuntimeIdentifier,
        "--self-contained", "true", "-p:PublishAot=true", "-p:TrimmerSingleWarn=true",
        "-o", $nativeAotPublishRoot) $consumerRoot

    $nativeAotExecutable = Join-Path $nativeAotPublishRoot $consumerExecutableName
    if (-not (Test-Path -LiteralPath $nativeAotExecutable -PathType Leaf)) {
        throw "Native AOT publish did not create '$nativeAotExecutable'."
    }
    $nativeAotFiles = @(Get-ChildItem -LiteralPath $nativeAotPublishRoot -File -Recurse)
    $symbolFiles = @($nativeAotFiles | Where-Object Extension -in @(".pdb", ".dbg"))
    if ($symbolFiles.Count -ne 0) {
        throw "Native AOT shipping output contains symbol sidecars: $($symbolFiles.Name -join ', ')"
    }
    $nativeAotExecutableBytes = (Get-Item -LiteralPath $nativeAotExecutable).Length
    $nativeAotTotalBytes = ($nativeAotFiles | Measure-Object Length -Sum).Sum
    $performanceBudgets = Get-Content -LiteralPath (Join-Path $repositoryRoot "SharpTS.Gui.Benchmarks\PerformanceBudgets.json") -Raw | ConvertFrom-Json
    $nativeAotMaxExecutableBytes = $performanceBudgets.nativeAot.maxExecutableBytes
    $nativeAotMaxTotalBytes = $performanceBudgets.nativeAot.maxShippingBytes
    if ($nativeAotExecutableBytes -gt $nativeAotMaxExecutableBytes) {
        throw "Native AOT executable budget exceeded: $nativeAotExecutableBytes bytes > $nativeAotMaxExecutableBytes bytes."
    }
    if ($nativeAotTotalBytes -gt $nativeAotMaxTotalBytes) {
        throw "Native AOT artifact budget exceeded: $nativeAotTotalBytes bytes > $nativeAotMaxTotalBytes bytes."
    }
    Write-Host "Native AOT budgets: executable $nativeAotExecutableBytes / $nativeAotMaxExecutableBytes bytes; total $nativeAotTotalBytes / $nativeAotMaxTotalBytes bytes."

    if ($canExecute) {
        $nativeAotTrace = Join-Path $artifactRoot "native-aot-trace.json"
        $nativeAotMeasurement = Measure-Application $nativeAotExecutable @(
            "--headless", "--trace", $nativeAotTrace, "--", "--smoke-close") $nativeAotPublishRoot
        Assert-GuestTrace $nativeAotTrace "Native AOT application"
        Write-Host "Native AOT cold startup: $($nativeAotMeasurement.ElapsedMilliseconds) ms; peak working set: $($nativeAotMeasurement.PeakWorkingSetBytes) bytes."
        if ($EnforcePerformanceBudgets -and
            $nativeAotMeasurement.ElapsedMilliseconds -gt $performanceBudgets.nativeAot.maxColdStartupMilliseconds) {
            throw "Native AOT startup budget exceeded: $($nativeAotMeasurement.ElapsedMilliseconds) ms > $($performanceBudgets.nativeAot.maxColdStartupMilliseconds) ms."
        }
        if ($EnforcePerformanceBudgets -and
            $nativeAotMeasurement.PeakWorkingSetBytes -gt $performanceBudgets.nativeAot.maxPeakWorkingSetBytes) {
            throw "Native AOT working-set budget exceeded: $($nativeAotMeasurement.PeakWorkingSetBytes) bytes > $($performanceBudgets.nativeAot.maxPeakWorkingSetBytes) bytes."
        }
    }
}

# Exercise the TypeScript-only CLI against the exact SDK package under test. The generated
# project is an internal implementation detail; users author no .csproj.
$sharpTsCli = Join-Path $repositoryRoot "bin\Release\net10.0\SharpTS.dll"
if (-not (Test-Path -LiteralPath $sharpTsCli)) {
    throw "SharpTS CLI was not built: $sharpTsCli"
}
Invoke-DotNet @(
    $sharpTsCli, "new", "avalonia", "-n", "PackagedCliApp", "-o", $cliRoot,
    "--sdk-version", $version)
[IO.File]::WriteAllText((Join-Path $cliRoot "Directory.Build.props"), "<Project />")
[IO.File]::WriteAllText((Join-Path $cliRoot "NuGet.config"), $nugetConfig)

Invoke-DotNet @(
    $sharpTsCli, "app", "compile", "headless.tests.tsx", "--source", $feed,
    "--configuration", $Configuration) $cliRoot
$generatedProject = Join-Path $cliRoot ".sharpts-gui.generated.csproj"
if (-not (Test-Path -LiteralPath $generatedProject)) {
    throw "TypeScript-only CLI did not materialize its internal SDK project."
}
$generatedProjectText = Get-Content -LiteralPath $generatedProject -Raw
if ($generatedProjectText -notmatch [regex]::Escape("SharpTS.Gui.Sdk/$version")) {
    throw "TypeScript-only CLI generated project did not pin the candidate GUI SDK version."
}
$generatedProjectWrite = (Get-Item -LiteralPath $generatedProject -Force).LastWriteTimeUtc
$cliGuest = Get-ChildItem -LiteralPath (Join-Path $cliRoot ".sharpts\gui\obj") -Force -Recurse -File -Filter "SharpTS.Gui.Guest.dll" |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($null -eq $cliGuest) {
    throw "TypeScript-only CLI compile did not create a guest assembly."
}
$cliGuestWrite = $cliGuest.LastWriteTimeUtc
Invoke-DotNet @(
    $sharpTsCli, "app", "build", "headless.tests.tsx", "--source", $feed,
    "--configuration", $Configuration) $cliRoot
if ((Get-Item -LiteralPath $generatedProject -Force).LastWriteTimeUtc -ne $generatedProjectWrite) {
    throw "TypeScript-only CLI rewrote an unchanged generated project."
}
if ((Get-Item -LiteralPath $cliGuest.FullName).LastWriteTimeUtc -ne $cliGuestWrite) {
    throw "TypeScript-only CLI rebuilt an unchanged guest assembly."
}

foreach ($mode in @("interpreted", "compiled")) {
    $cliRunTrace = Join-Path $artifactRoot "cli-run-$mode-trace.json"
    Invoke-DotNet @(
        $sharpTsCli, "app", "run", "headless.tests.tsx", "--source", $feed,
        "--mode", $mode, "--", "--headless", "--trace", $cliRunTrace) $cliRoot
    Assert-GuestTrace $cliRunTrace "TypeScript-only CLI $mode run"
}

Invoke-DotNet @(
    $sharpTsCli, "app", "publish", "headless.tests.tsx", "--source", $feed,
    "--rid", $RuntimeIdentifier, "--self-contained", "false", "--single-file", "false",
    "--output", $cliDirectoryPublishRoot) $cliRoot
$cliDirectoryLauncher = Join-Path $cliDirectoryPublishRoot "cli_app_with_spaces.dll"
if (-not (Test-Path -LiteralPath $cliDirectoryLauncher)) {
    throw "TypeScript-only CLI directory publish did not create its app host assembly."
}
if ($canExecute) {
    foreach ($mode in @("interpreted", "compiled")) {
        $cliDirectoryTrace = Join-Path $artifactRoot "cli-directory-$mode-trace.json"
        Invoke-DotNet @(
            $cliDirectoryLauncher, "--mode", $mode, "--headless", "--trace", $cliDirectoryTrace) $cliDirectoryPublishRoot
        Assert-GuestTrace $cliDirectoryTrace "TypeScript-only CLI published $mode application"
    }
}

$sdkManifest = Get-Content -LiteralPath (Join-Path $directoryPublishRoot ".sharpts\app.json") -Raw | ConvertFrom-Json
$cliManifest = Get-Content -LiteralPath (Join-Path $cliDirectoryPublishRoot ".sharpts\app.json") -Raw | ConvertFrom-Json
foreach ($property in @("hostedAbiVersion", "guiApiVersion", "descriptorSchemaVersion", "descriptorSchemaHash")) {
    if ($sdkManifest.$property -ne $cliManifest.$property) {
        throw "SDK/CLI application manifest parity failed for '$property'."
    }
}
$sdkManagedClosure = @(Get-ChildItem -LiteralPath $directoryPublishRoot -File -Filter "*.dll" |
    Where-Object Name -ne "SharpTS.Gui.Sdk.Consumer.dll" | ForEach-Object Name | Sort-Object)
$cliManagedClosure = @(Get-ChildItem -LiteralPath $cliDirectoryPublishRoot -File -Filter "*.dll" |
    Where-Object Name -ne "cli_app_with_spaces.dll" | ForEach-Object Name | Sort-Object)
if (Compare-Object $sdkManagedClosure $cliManagedClosure -SyncWindow 0) {
    throw "SDK/CLI managed dependency closure differs."
}
$sdkNativeClosure = @(Get-ChildItem -LiteralPath $directoryPublishRoot -File |
    Where-Object Extension -notin @(".dll", ".json", ".pdb") | ForEach-Object Name | Sort-Object)
$cliNativeClosure = @(Get-ChildItem -LiteralPath $cliDirectoryPublishRoot -File |
    Where-Object Extension -notin @(".dll", ".json", ".pdb") |
    Where-Object Name -notin @($consumerExecutableName, $cliExecutableName) |
    ForEach-Object Name | Sort-Object)
$sdkNativeClosure = @($sdkNativeClosure | Where-Object { $_ -ne $consumerExecutableName })
if (Compare-Object $sdkNativeClosure $cliNativeClosure -SyncWindow 0) {
    throw "SDK/CLI native dependency closure differs."
}

Invoke-DotNet @(
    $sharpTsCli, "app", "publish", "headless.tests.tsx", "--source", $feed,
    "--rid", $RuntimeIdentifier, "--self-contained", "true", "--single-file", "true",
    "--output", $cliSingleFilePublishRoot) $cliRoot
$cliSingleFile = Join-Path $cliSingleFilePublishRoot $cliExecutableName
if (-not (Test-Path -LiteralPath $cliSingleFile)) {
    throw "TypeScript-only CLI self-contained single-file publish did not create an executable."
}
if ($canExecute) {
    $oldDotnetRoot = $env:DOTNET_ROOT
    $env:DOTNET_ROOT = Join-Path $artifactRoot "intentionally missing cli dotnet"
    try {
        $cliSingleFileTrace = Join-Path $artifactRoot "cli-single-file-trace.json"
        Invoke-Application $cliSingleFile @("--headless", "--trace", $cliSingleFileTrace) $cliSingleFilePublishRoot
        Assert-GuestTrace $cliSingleFileTrace "TypeScript-only CLI single-file application"
    }
    finally {
        $env:DOTNET_ROOT = $oldDotnetRoot
    }
}

Push-Location $cliRoot
try {
    $cliMissingOutput = & dotnet $sharpTsCli app build missing.tsx --host avalonia --source $feed 2>&1 | Out-String
}
finally {
    Pop-Location
}
if ($LASTEXITCODE -eq 0 -or $cliMissingOutput -notmatch "Application entry not found") {
    throw "TypeScript-only CLI missing-entry diagnostic was not actionable.`n$cliMissingOutput"
}

$missingRoot = Join-Path $artifactRoot "missing entry point"
New-Item -ItemType Directory -Path $missingRoot | Out-Null
Copy-Item -LiteralPath $consumerProject -Destination $missingRoot
Copy-Item -LiteralPath (Join-Path $consumerRoot "NuGet.config") -Destination $missingRoot
Copy-Item -LiteralPath (Join-Path $consumerRoot "Directory.Build.props") -Destination $missingRoot
$missingOutput = & dotnet build (Join-Path $missingRoot "SharpTS.Gui.Sdk.Consumer.csproj") -c $Configuration 2>&1 | Out-String
if ($LASTEXITCODE -eq 0 -or $missingOutput -notmatch "SharpTSEntryPoint.*does not exist") {
    throw "Missing-entry-point build did not produce the expected SDK diagnostic.`n$missingOutput"
}

Write-Host "SharpTS.Gui.Sdk packaged consumer verification passed for $RuntimeIdentifier."
exit 0
