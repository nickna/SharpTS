param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$RealWindow,
    [switch]$PublishOnly
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$supportedPlatformsPath = Join-Path $repositoryRoot "SharpTS.Gui.Sdk\Sdk\SupportedPlatforms.props"
[xml]$supportedPlatforms = Get-Content -LiteralPath $supportedPlatformsPath -Raw
$supportedRuntimeIdentifiers = @(
    $supportedPlatforms.SelectNodes("//SharpTSGuiSupportedRuntimeIdentifier") |
        ForEach-Object { $_.GetAttribute("Include") }
)
if ($RuntimeIdentifier -notin $supportedRuntimeIdentifiers) {
    throw "SharpTS.Gui.Sdk supports only $($supportedRuntimeIdentifiers -join ', '); got '$RuntimeIdentifier'."
}
$version = "0.1.0-preview.1"
$artifactRoot = Join-Path $repositoryRoot "artifacts\windows-preview\$RuntimeIdentifier"
$feed = Join-Path $artifactRoot "feed"
$packageCache = Join-Path $artifactRoot "packages"
$consumerRoot = Join-Path $artifactRoot "consumer with spaces"
$directoryPublishRoot = Join-Path $artifactRoot "published directory"
$singleFilePublishRoot = Join-Path $artifactRoot "published single file"

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
    Push-Location $WorkingDirectory
    try {
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Executable $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
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

Invoke-DotNet @("build", "SharpTS.Sdk.Tasks\SharpTS.Sdk.Tasks.csproj", "-c", "Release", "--no-restore")
Invoke-DotNet @("publish", "SharpTS.Gui.Host\SharpTS.Gui.Host.csproj", "-c", "Release", "--self-contained", "false", "--no-restore")
Invoke-DotNet @("restore", "SharpTS.Gui.Sdk\SharpTS.Gui.Sdk.csproj")
Invoke-DotNet @("pack", "SharpTS.Gui.Sdk\SharpTS.Gui.Sdk.csproj", "-c", "Release", "--no-restore", "-o", $feed, "-p:MinVerVersionOverride=$version")

$packagePath = Join-Path $feed "SharpTS.Gui.Sdk.$version.nupkg"
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
        "gui/internal-testing.ts",
        "launcher/Launcher.cs",
        "tools/net10.0/any/host/SharpTS.Gui.Host.dll",
        "tools/net10.0/any/host/SharpTS.dll",
        "tools/net10.0/any/host/SharpTS.Gui.dll"
    )) {
        if ($entryNames -notcontains $required) {
            throw "GUI SDK package is missing '$required'."
        }
    }
    if ($entryNames -match "native/runtimes/") {
        throw "GUI SDK package contains a duplicated native-runtime path."
    }
    if ($entryNames -match "/runtimes/(linux|osx|browser|maccatalyst|ios|android)-") {
        throw "Windows GUI SDK package contains a non-Windows native runtime asset."
    }
    foreach ($rid in $supportedRuntimeIdentifiers) {
        if (-not ($entryNames -match "/runtimes/$rid/native/")) {
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
    <add key="windows-preview" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
[IO.File]::WriteAllText((Join-Path $consumerRoot "NuGet.config"), $nugetConfig)

$consumerProject = Join-Path $consumerRoot "SharpTS.Gui.Sdk.Consumer.csproj"
Invoke-DotNet @("restore", $consumerProject, "--configfile", (Join-Path $consumerRoot "NuGet.config")) $consumerRoot
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot
$guestAssembly = Join-Path $consumerRoot "bin\$Configuration\net10.0\SharpTS.Gui.Guest.dll"
$firstGuestWrite = (Get-Item -LiteralPath $guestAssembly).LastWriteTimeUtc
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot
if ((Get-Item -LiteralPath $guestAssembly).LastWriteTimeUtc -ne $firstGuestWrite) {
    throw "Incremental GUI build recreated an unchanged guest assembly."
}
Invoke-DotNet @("clean", $consumerProject, "-c", $Configuration) $consumerRoot
if (Test-Path (Join-Path $consumerRoot "bin\$Configuration\net10.0\SharpTS.Gui.Host.dll")) {
    throw "GUI SDK clean left the packaged host in the output directory."
}
Invoke-DotNet @("build", $consumerProject, "-c", $Configuration, "--no-restore") $consumerRoot

$buildOutput = Join-Path $consumerRoot "bin\$Configuration\net10.0"
$buildLauncher = Join-Path $buildOutput "SharpTS.Gui.Sdk.Consumer.dll"
$dotnetRunTrace = Join-Path $artifactRoot "dotnet-run-interpreted-trace.json"
Invoke-DotNet @("run", "--project", $consumerProject, "-c", $Configuration, "--no-build", "--", "--mode", "interpreted", "--headless", "--auto-close", "--trace", $dotnetRunTrace) $consumerRoot
$buildTraces = @{}
foreach ($mode in @("interpreted", "compiled")) {
    $tracePath = Join-Path $artifactRoot "build-$mode-trace.json"
    Invoke-DotNet @($buildLauncher, "--mode", $mode, "--headless", "--auto-close", "--trace", $tracePath) $buildOutput
    $buildTraces[$mode] = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
}
if (Compare-Object (RendererTrace $buildTraces["interpreted"]) (RendererTrace $buildTraces["compiled"]) -SyncWindow 0) {
    throw "Packaged interpreted and compiled renderer traces differ."
}

Invoke-DotNet @("restore", $consumerProject, "-r", $RuntimeIdentifier, "--configfile", (Join-Path $consumerRoot "NuGet.config")) $consumerRoot
Invoke-DotNet @(
    "publish", $consumerProject, "-c", $Configuration, "-r", $RuntimeIdentifier,
    "--self-contained", "false", "--no-restore", "-p:SharpTSGuiPublishMode=Directory", "-o", $directoryPublishRoot) $consumerRoot

$osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
$canExecute = -not $PublishOnly -and (
    ($RuntimeIdentifier -eq "win-x64" -and $osArchitecture -eq "X64") -or
    ($RuntimeIdentifier -eq "win-arm64" -and $osArchitecture -eq "Arm64"))
$directoryLauncher = Join-Path $directoryPublishRoot "SharpTS.Gui.Sdk.Consumer.dll"
if ($canExecute) {
    foreach ($mode in @("interpreted", "compiled")) {
        $tracePath = Join-Path $artifactRoot "published-directory-$mode-trace.json"
        Invoke-DotNet @($directoryLauncher, "--mode", $mode, "--headless", "--auto-close", "--trace", $tracePath) $directoryPublishRoot
        if ($RealWindow) {
            $windowTracePath = Join-Path $artifactRoot "published-directory-$mode-window-trace.json"
            Invoke-DotNet @($directoryLauncher, "--mode", $mode, "--auto-close", "--trace", $windowTracePath) $directoryPublishRoot
        }
    }
}

Invoke-DotNet @(
    "publish", $consumerProject, "-c", $Configuration, "-r", $RuntimeIdentifier,
    "--self-contained", "true", "--no-restore", "-p:SharpTSGuiPublishMode=SingleFile", "-o", $singleFilePublishRoot) $consumerRoot

$singleFile = Join-Path $singleFilePublishRoot "SharpTS.Gui.Sdk.Consumer.exe"
if (-not (Test-Path $singleFile)) {
    throw "Single-file GUI executable was not created: $singleFile"
}
$distributionFiles = @(Get-ChildItem -LiteralPath $singleFilePublishRoot -File)
if ($distributionFiles.Count -ne 1 -or $distributionFiles[0].Name -ne "SharpTS.Gui.Sdk.Consumer.exe") {
    throw "Single-file publish produced unexpected sidecar files: $($distributionFiles.Name -join ', ')"
}

if ($canExecute) {
    $oldDotnetRoot = $env:DOTNET_ROOT
    $env:DOTNET_ROOT = Join-Path $artifactRoot "intentionally missing dotnet"
    try {
        $singleTrace = Join-Path $artifactRoot "single-file-compiled-trace.json"
        Invoke-Application $singleFile @("--headless", "--auto-close", "--trace", $singleTrace) $singleFilePublishRoot
        if ($RealWindow) {
            $singleWindowTrace = Join-Path $artifactRoot "single-file-compiled-window-trace.json"
            Invoke-Application $singleFile @("--auto-close", "--trace", $singleWindowTrace) $singleFilePublishRoot
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
