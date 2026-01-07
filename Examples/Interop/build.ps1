# SharpTS Example Interop Build Script
# This script compiles TypeScript to a .NET DLL and then builds the C# consumer project

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sharpTsProjectDir = Split-Path -Parent $scriptDir
$compiledTsDir = Join-Path $scriptDir "CompiledTS"
$typeScriptDir = Join-Path $scriptDir "TypeScript"

Write-Host "=== SharpTS C# Interop Example Build ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Ensure output directory exists
Write-Host "Step 1: Preparing output directory..." -ForegroundColor Yellow
if (!(Test-Path $compiledTsDir)) {
    New-Item -ItemType Directory -Path $compiledTsDir -Force | Out-Null
}
Write-Host "  Output directory: $compiledTsDir" -ForegroundColor Gray

# Step 2: Build SharpTS first (to ensure we have latest version)
Write-Host ""
Write-Host "Step 2: Building SharpTS..." -ForegroundColor Yellow
Push-Location $sharpTsProjectDir
try {
    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build SharpTS"
    }
} finally {
    Pop-Location
}
Write-Host "  SharpTS built successfully" -ForegroundColor Green

# Step 3: Compile TypeScript to .NET DLL
Write-Host ""
Write-Host "Step 3: Compiling TypeScript to .NET DLL..." -ForegroundColor Yellow
$tsInputPath = Join-Path $typeScriptDir "Library.ts"
$dllOutputPath = Join-Path $compiledTsDir "Library.dll"

Write-Host "  Input: $tsInputPath" -ForegroundColor Gray
Write-Host "  Output: $dllOutputPath" -ForegroundColor Gray

Push-Location $sharpTsProjectDir
try {
    dotnet run --configuration Release -- --compile "$tsInputPath" -o "$dllOutputPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile TypeScript"
    }
} finally {
    Pop-Location
}
Write-Host "  TypeScript compiled successfully" -ForegroundColor Green

# Step 4: Copy SharpTS.dll for runtime dependencies
Write-Host ""
Write-Host "Step 4: Copying runtime dependencies..." -ForegroundColor Yellow
$sharpTsDll = Join-Path $sharpTsProjectDir "bin\Release\net10.0\SharpTS.dll"
if (Test-Path $sharpTsDll) {
    Copy-Item $sharpTsDll -Destination $compiledTsDir -Force
    Write-Host "  Copied SharpTS.dll" -ForegroundColor Green
} else {
    Write-Host "  Warning: SharpTS.dll not found at expected location" -ForegroundColor Yellow
    Write-Host "  Checking Debug build..." -ForegroundColor Gray
    $sharpTsDllDebug = Join-Path $sharpTsProjectDir "bin\Debug\net10.0\SharpTS.dll"
    if (Test-Path $sharpTsDllDebug) {
        Copy-Item $sharpTsDllDebug -Destination $compiledTsDir -Force
        Write-Host "  Copied SharpTS.dll from Debug build" -ForegroundColor Green
    } else {
        Write-Host "  Warning: Could not find SharpTS.dll" -ForegroundColor Red
    }
}

# Step 5: Build the C# consumer project
Write-Host ""
Write-Host "Step 5: Building C# consumer project..." -ForegroundColor Yellow
Push-Location $scriptDir
try {
    dotnet build
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build C# project"
    }
} finally {
    Pop-Location
}
Write-Host "  C# project built successfully" -ForegroundColor Green

# Step 6: Copy DLLs to output directory
Write-Host ""
Write-Host "Step 6: Copying DLLs to output directory..." -ForegroundColor Yellow
$outputDir = Join-Path $scriptDir "bin\Debug\net10.0"
Copy-Item (Join-Path $compiledTsDir "Library.dll") -Destination $outputDir -Force
Copy-Item (Join-Path $compiledTsDir "SharpTS.dll") -Destination $outputDir -Force
Write-Host "  Copied Library.dll and SharpTS.dll to output" -ForegroundColor Green

# Step 7: Run the example
Write-Host ""
Write-Host "Step 7: Running the example..." -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Push-Location $scriptDir
try {
    dotnet run
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build and run completed!" -ForegroundColor Green
