[CmdletBinding()]
param(
    [string]$PackagePath,
    [string]$PackageVersion = "0.0.0-sdk-smoke"
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "sharpts-sdk-smoke-$([Guid]::NewGuid().ToString('N'))"
$feedPath = Join-Path $tempRoot "feed"
$consumerPath = Join-Path $tempRoot "consumer"
$packagesPath = Join-Path $tempRoot "packages"
$cliHomePath = Join-Path $tempRoot "cli-home"
$publishPath = Join-Path $tempRoot "published"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)] [string]$WorkingDirectory,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [switch]$CaptureOutput
    )

    Push-Location $WorkingDirectory
    try {
        Write-Host "> dotnet $($Arguments -join ' ')"
        if ($CaptureOutput) {
            $lines = & dotnet @Arguments 2>&1
            $exitCode = $LASTEXITCODE
            $text = ($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
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
    Assert-PathExists (Join-Path $publishPath "SharpTS.Sdk.Smoke.runtimeconfig.json")
    Assert-SmokeOutput (Invoke-DotNet -WorkingDirectory $consumerPath -Arguments @($publishedAssembly) -CaptureOutput)

    Write-Host "Packaged SharpTS.Sdk smoke test passed for version $PackageVersion."
}
finally {
    $env:NUGET_PACKAGES = $oldNuGetPackages
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:PATH = $oldPath
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $oldTelemetry
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $oldFirstTimeExperience

    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
