<#
.SYNOPSIS
    Tests SharpTS examples in interpreted, compiled DLL, and compiled EXE modes.

.DESCRIPTION
    This script tests each example in the Examples directory using three execution modes:
    1. Interpreted: dotnet run -- <file>.ts <args>
    2. Compiled DLL: dotnet run -- --compile <file>.ts -o <out>.dll then dotnet <out>.dll <args>
    3. Compiled EXE: dotnet run -- --compile <file>.ts -t exe -o <out>.exe then .\<out>.exe <args>

.PARAMETER Filter
    Filter examples by name pattern (e.g., "file-*" or "system-info")

.PARAMETER Mode
    Execution mode(s) to test: all, interpreted, dll, exe (default: all)

.PARAMETER OutputFormat
    Output format: json, table, verbose (default: table)

.PARAMETER SkipCleanup
    Skip cleanup of temporary files and directories

.EXAMPLE
    .\test-examples.ps1
    Run all tests with default settings

.EXAMPLE
    .\test-examples.ps1 -Filter "system-info" -OutputFormat verbose
    Test only system-info example with verbose output

.EXAMPLE
    .\test-examples.ps1 -Mode interpreted -OutputFormat json
    Test only interpreted mode and output JSON
#>

param(
    [string]$Filter = "*",
    [ValidateSet("all", "interpreted", "dll", "exe")]
    [string]$Mode = "all",
    [ValidateSet("json", "table", "verbose")]
    [string]$OutputFormat = "table",
    [switch]$SkipCleanup
)

$ErrorActionPreference = "Stop"

# ========== Configuration ==========

$Script:ProjectRoot = Split-Path -Parent $PSScriptRoot
$Script:ExamplesDir = $PSScriptRoot
$Script:TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "SharpTS-Tests-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$Script:ProcessTimeout = 30000  # 30 seconds
$Script:BuildTimeout = 120000   # 2 minutes for compilation

# ========== Test Case Definitions ==========

$Script:TestCases = @{
    "file-hasher" = @{
        File = "file-hasher.ts"
        Tests = @(
            @{
                Name = "HashTextFile"
                RequiresArgs = $true
                Setup = {
                    $testFile = Join-Path $Script:TempRoot "test-hash.txt"
                    Set-Content -Path $testFile -Value "Hello, World!" -NoNewline
                    return @{ TestFile = $testFile }
                }
                Args = { param($ctx) @($ctx.TestFile) }
                Assertions = @(
                    @{ Type = "Contains"; Value = "File Hasher Results" }
                    @{ Type = "Contains"; Value = "MD5" }
                    @{ Type = "Contains"; Value = "SHA1" }
                    @{ Type = "Contains"; Value = "SHA256" }
                    @{ Type = "Contains"; Value = "SHA512" }
                )
            },
            @{
                Name = "FileNotFound"
                RequiresArgs = $true
                Args = { @("C:\nonexistent\path\file.txt") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Error: File not found" }
                )
            },
            @{
                Name = "NoArguments"
                RequiresArgs = $false
                Args = { @() }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Usage:" }
                )
            }
        )
    }

    "file-organizer" = @{
        File = "file-organizer.ts"
        Tests = @(
            @{
                Name = "DryRunMode"
                RequiresArgs = $true
                Setup = {
                    $testDir = Join-Path $Script:TempRoot "organize-test"
                    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
                    Set-Content -Path (Join-Path $testDir "document.txt") -Value "text content"
                    Set-Content -Path (Join-Path $testDir "photo.jpg") -Value "fake image"
                    Set-Content -Path (Join-Path $testDir "script.ts") -Value "console.log('test')"
                    return @{ TestDir = $testDir }
                }
                Args = { param($ctx) @($ctx.TestDir, "--dry-run") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "DRY RUN" }
                    @{ Type = "Contains"; Value = "document.txt" }
                    @{ Type = "Contains"; Value = "photo.jpg" }
                )
            },
            @{
                Name = "DirectoryNotFound"
                RequiresArgs = $true
                Args = { @("C:\nonexistent\directory\path") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Error: Directory not found" }
                )
            },
            @{
                Name = "NoArguments"
                RequiresArgs = $false
                Args = { @() }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Usage:" }
                )
            }
        )
    }

    "password-generator" = @{
        File = "password-generator.ts"
        Tests = @(
            @{
                Name = "InvalidLengthTooSmall"
                RequiresArgs = $true
                Args = { @("2") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Error: Length must be between 4 and 128" }
                )
            },
            @{
                Name = "InvalidLengthTooLarge"
                RequiresArgs = $true
                Args = { @("500") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Error: Length must be between 4 and 128" }
                )
            }
        )
    }

    "system-info" = @{
        File = "system-info.ts"
        Tests = @(
            @{
                Name = "DisplaySystemInfo"
                RequiresArgs = $false
                Args = { @() }
                Assertions = @(
                    @{ Type = "Contains"; Value = "System Information Report" }
                    @{ Type = "Contains"; Value = "Platform:" }
                    @{ Type = "Contains"; Value = "Memory" }
                    @{ Type = "Contains"; Value = "CPU" }
                    @{ Type = "Contains"; Value = "Cores:" }
                    @{ Type = "Contains"; Value = "PID:" }
                )
            }
        )
    }

    "url-toolkit" = @{
        File = "url-toolkit.ts"
        Tests = @(
            @{
                Name = "ParseSimpleURL"
                RequiresArgs = $true
                Args = { @("https://example.com/path?foo=bar") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "protocol:" }
                    @{ Type = "Contains"; Value = "https:" }
                    @{ Type = "Contains"; Value = "hostname:" }
                    @{ Type = "Contains"; Value = "example.com" }
                    @{ Type = "Contains"; Value = "pathname:" }
                    @{ Type = "Contains"; Value = "/path" }
                    @{ Type = "Contains"; Value = "foo" }
                )
            },
            @{
                Name = "ParseURLWithPort"
                RequiresArgs = $true
                Args = { @("http://localhost:8080/api") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "port:" }
                    @{ Type = "Contains"; Value = "8080" }
                    @{ Type = "Contains"; Value = "localhost" }
                )
            }
        )
    }

    "source-analyzer" = @{
        File = "SourceAnalyzer/source-analyzer.ts"
        Tests = @(
            @{
                Name = "AnalyzeTestDirectory"
                RequiresArgs = $true
                Setup = {
                    $testDir = Join-Path $Script:TempRoot "analyzer-test"
                    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
                    $tsContent = @"
function hello(): void {
    console.log('Hello');
}

function goodbye(): void {
    console.log('Goodbye');
}

const arrow = () => 42;
"@
                    Set-Content -Path (Join-Path $testDir "sample.ts") -Value $tsContent
                    return @{ TestDir = $testDir }
                }
                Args = { param($ctx) @($ctx.TestDir) }
                Assertions = @(
                    @{ Type = "Contains"; Value = "TOTAL" }
                    @{ Type = "Contains"; Value = "sample.ts" }
                )
            },
            @{
                Name = "HelpFlag"
                RequiresArgs = $true
                Args = { @("--help") }
                Assertions = @(
                    @{ Type = "Contains"; Value = "Usage:" }
                    @{ Type = "Contains"; Value = "Supported file extensions" }
                )
            }
        )
    }
}

# ========== Fixture Functions ==========

function Initialize-TempDirectory {
    if (-not (Test-Path $Script:TempRoot)) {
        New-Item -ItemType Directory -Path $Script:TempRoot -Force | Out-Null
    }
}

function Remove-TempDirectory {
    if ((Test-Path $Script:TempRoot) -and (-not $SkipCleanup)) {
        Remove-Item -Path $Script:TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ========== Execution Functions ==========

function Invoke-ProcessWithTimeout {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$Timeout = $Script:ProcessTimeout,
        [string]$WorkingDirectory = $Script:ProjectRoot
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '\s') { "`"$_`"" } else { $_ }
    }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        [void]$process.Start()

        # Read output asynchronously using tasks
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        $completed = $process.WaitForExit($Timeout)
        $sw.Stop()

        if (-not $completed) {
            $process.Kill()
            return @{
                Success = $false
                Output = ""
                Error = "Process timed out after $($Timeout)ms"
                ExitCode = -1
                Duration = $sw.ElapsedMilliseconds
            }
        }

        # Wait for async reads to complete
        [void]$stdoutTask.Wait(5000)
        [void]$stderrTask.Wait(5000)

        $stdout = if ($stdoutTask.IsCompleted) { $stdoutTask.Result } else { "" }
        $stderr = if ($stderrTask.IsCompleted) { $stderrTask.Result } else { "" }

        return @{
            Success = $process.ExitCode -eq 0
            Output = $stdout
            Error = $stderr
            ExitCode = $process.ExitCode
            Duration = $sw.ElapsedMilliseconds
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-Interpreted {
    param(
        [string]$TsFile,
        [string[]]$Arguments
    )

    $allArgs = @("run", "--", $TsFile) + $Arguments
    return Invoke-ProcessWithTimeout -FilePath "dotnet" -Arguments $allArgs
}

function Invoke-CompiledDll {
    param(
        [string]$TsFile,
        [string[]]$Arguments,
        [string]$TestName
    )

    $outputDir = Join-Path $Script:TempRoot "dll-$TestName"
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($TsFile)
    $dllPath = Join-Path $outputDir "$baseName.dll"

    # Compile to DLL
    $compileArgs = @("run", "--", "--compile", $TsFile, "-o", $dllPath)
    $compileResult = Invoke-ProcessWithTimeout -FilePath "dotnet" -Arguments $compileArgs -Timeout $Script:BuildTimeout

    if (-not $compileResult.Success) {
        return @{
            Success = $false
            Output = $compileResult.Output
            Error = "Compilation failed: $($compileResult.Error)"
            ExitCode = $compileResult.ExitCode
            Duration = $compileResult.Duration
            CompileOutput = $compileResult.Output
        }
    }

    # Copy runtime dependency
    $runtimeDll = Join-Path $Script:ProjectRoot "bin\Debug\net10.0\SharpTS.dll"
    if (Test-Path $runtimeDll) {
        Copy-Item -Path $runtimeDll -Destination $outputDir -Force
    }

    # Also copy runtimeconfig.json if it exists
    $runtimeConfig = Join-Path $Script:ProjectRoot "bin\Debug\net10.0\SharpTS.runtimeconfig.json"
    if (Test-Path $runtimeConfig) {
        $newConfigPath = Join-Path $outputDir "$baseName.runtimeconfig.json"
        Copy-Item -Path $runtimeConfig -Destination $newConfigPath -Force
    }

    # Run the DLL
    $runArgs = @($dllPath) + $Arguments
    $runResult = Invoke-ProcessWithTimeout -FilePath "dotnet" -Arguments $runArgs -WorkingDirectory $outputDir
    $runResult.Duration += $compileResult.Duration
    $runResult.CompileOutput = $compileResult.Output

    return $runResult
}

function Invoke-CompiledExe {
    param(
        [string]$TsFile,
        [string[]]$Arguments,
        [string]$TestName
    )

    $outputDir = Join-Path $Script:TempRoot "exe-$TestName"
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($TsFile)
    $exePath = Join-Path $outputDir "$baseName.exe"

    # Compile to EXE
    $compileArgs = @("run", "--", "--compile", $TsFile, "-t", "exe", "-o", $exePath)
    $compileResult = Invoke-ProcessWithTimeout -FilePath "dotnet" -Arguments $compileArgs -Timeout $Script:BuildTimeout

    if (-not $compileResult.Success) {
        return @{
            Success = $false
            Output = $compileResult.Output
            Error = "Compilation failed: $($compileResult.Error)"
            ExitCode = $compileResult.ExitCode
            Duration = $compileResult.Duration
            CompileOutput = $compileResult.Output
        }
    }

    # Copy runtime dependency
    $runtimeDll = Join-Path $Script:ProjectRoot "bin\Debug\net10.0\SharpTS.dll"
    if (Test-Path $runtimeDll) {
        Copy-Item -Path $runtimeDll -Destination $outputDir -Force
    }

    # Run the EXE
    $runResult = Invoke-ProcessWithTimeout -FilePath $exePath -Arguments $Arguments -WorkingDirectory $outputDir
    $runResult.Duration += $compileResult.Duration
    $runResult.CompileOutput = $compileResult.Output

    return $runResult
}

# ========== Assertion Functions ==========

function Test-Assertion {
    param(
        [string]$Output,
        [hashtable]$Assertion
    )

    switch ($Assertion.Type) {
        "Contains" {
            return $Output -match [regex]::Escape($Assertion.Value)
        }
        "Regex" {
            return $Output -match $Assertion.Value
        }
        "NotContains" {
            return $Output -notmatch [regex]::Escape($Assertion.Value)
        }
        default {
            return $false
        }
    }
}

function Test-AllAssertions {
    param(
        [string]$Output,
        [array]$Assertions
    )

    $results = @()
    foreach ($assertion in $Assertions) {
        $passed = Test-Assertion -Output $Output -Assertion $assertion
        $results += @{
            Assertion = $assertion
            Passed = $passed
        }
    }
    return $results
}

# ========== Test Runner ==========

function Invoke-TestCase {
    param(
        [string]$ExampleName,
        [hashtable]$Example,
        [hashtable]$TestCase,
        [string]$ExecutionMode
    )

    $tsFile = Join-Path $Script:ExamplesDir $Example.File
    $testContext = @{}

    # Run setup if defined
    if ($TestCase.Setup) {
        $testContext = & $TestCase.Setup
    }

    # Get arguments
    $args = @()
    if ($TestCase.Args) {
        $args = & $TestCase.Args $testContext
    }

    # Execute based on mode
    $result = switch ($ExecutionMode) {
        "interpreted" { Invoke-Interpreted -TsFile $tsFile -Arguments $args }
        "dll" { Invoke-CompiledDll -TsFile $tsFile -Arguments $args -TestName "$ExampleName-$($TestCase.Name)" }
        "exe" { Invoke-CompiledExe -TsFile $tsFile -Arguments $args -TestName "$ExampleName-$($TestCase.Name)" }
    }

    # Combine stdout and stderr for assertion testing
    $combinedOutput = "$($result.Output)`n$($result.Error)"

    # Run assertions
    $assertionResults = Test-AllAssertions -Output $combinedOutput -Assertions $TestCase.Assertions
    $allPassed = ($assertionResults | Where-Object { -not $_.Passed }).Count -eq 0

    return @{
        TestName = $TestCase.Name
        Mode = $ExecutionMode
        Passed = $allPassed
        Duration = $result.Duration
        Output = $result.Output
        Error = $result.Error
        ExitCode = $result.ExitCode
        AssertionResults = $assertionResults
    }
}

function Invoke-AllTests {
    $startTime = Get-Date
    $results = @{
        StartTime = $startTime.ToString("o")
        Examples = @()
    }

    $totalTests = 0
    $passedTests = 0
    $failedTests = 0
    $skippedTests = 0

    $modesToTest = switch ($Mode) {
        "all" { @("interpreted", "dll", "exe") }
        default { @($Mode) }
    }

    foreach ($exampleName in $Script:TestCases.Keys | Sort-Object) {
        # Apply filter
        if ($exampleName -notlike $Filter) {
            continue
        }

        $example = $Script:TestCases[$exampleName]
        $exampleResults = @{
            Name = $exampleName
            File = $example.File
            TestCases = @()
        }

        foreach ($testCase in $example.Tests) {
            $testCaseResult = @{
                Name = $testCase.Name
                Modes = @()
            }

            foreach ($mode in $modesToTest) {
                $totalTests++

                if ($OutputFormat -eq "verbose") {
                    Write-Host "Testing $exampleName/$($testCase.Name) [$mode]... " -NoNewline
                }

                try {
                    $result = Invoke-TestCase -ExampleName $exampleName -Example $example -TestCase $testCase -ExecutionMode $mode

                    $testCaseResult.Modes += @{
                        Mode = $mode
                        Passed = $result.Passed
                        Skipped = $false
                        Duration = $result.Duration
                        Output = $result.Output
                        Error = $result.Error
                        AssertionResults = $result.AssertionResults
                    }

                    if ($result.Passed) {
                        $passedTests++
                        if ($OutputFormat -eq "verbose") {
                            Write-Host "PASSED" -ForegroundColor Green
                        }
                    } else {
                        $failedTests++
                        if ($OutputFormat -eq "verbose") {
                            Write-Host "FAILED" -ForegroundColor Red
                            $failedAssertions = $result.AssertionResults | Where-Object { -not $_.Passed }
                            foreach ($failed in $failedAssertions) {
                                Write-Host "  - Failed: $($failed.Assertion.Type) '$($failed.Assertion.Value)'" -ForegroundColor Yellow
                            }
                            if ($result.Error) {
                                Write-Host "  - Error: $($result.Error)" -ForegroundColor Yellow
                            }
                        }
                    }
                }
                catch {
                    $failedTests++
                    $testCaseResult.Modes += @{
                        Mode = $mode
                        Passed = $false
                        Skipped = $false
                        Duration = 0
                        Error = $_.Exception.Message
                    }
                    if ($OutputFormat -eq "verbose") {
                        Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
                    }
                }
            }

            $exampleResults.TestCases += $testCaseResult
        }

        $results.Examples += $exampleResults
    }

    $endTime = Get-Date
    $results.Duration = ($endTime - $startTime).TotalSeconds
    $results.TotalTests = $totalTests
    $results.PassedTests = $passedTests
    $results.FailedTests = $failedTests
    $results.SkippedTests = $skippedTests

    return $results
}

# ========== Output Formatters ==========

function Format-TableOutput {
    param($Results)

    Write-Host ""
    Write-Host "SharpTS Examples Test Results" -ForegroundColor Cyan
    Write-Host "==============================" -ForegroundColor Cyan
    Write-Host ""

    $tableData = @()

    foreach ($example in $Results.Examples) {
        foreach ($testCase in $example.TestCases) {
            $row = [PSCustomObject]@{
                Example = $example.Name
                Test = $testCase.Name
            }

            foreach ($modeResult in $testCase.Modes) {
                $status = if ($modeResult.Skipped) { "SKIP" }
                          elseif ($modeResult.Passed) { "PASS" }
                          else { "FAIL" }
                $row | Add-Member -NotePropertyName $modeResult.Mode -NotePropertyValue $status
            }

            $tableData += $row
        }
    }

    $tableData | Format-Table -AutoSize

    Write-Host ""
    Write-Host "Summary" -ForegroundColor Cyan
    Write-Host "-------" -ForegroundColor Cyan
    Write-Host "Total:   $($Results.TotalTests)"
    Write-Host "Passed:  $($Results.PassedTests)" -ForegroundColor Green
    Write-Host "Failed:  $($Results.FailedTests)" -ForegroundColor $(if ($Results.FailedTests -gt 0) { "Red" } else { "Green" })
    Write-Host "Skipped: $($Results.SkippedTests)" -ForegroundColor Yellow
    Write-Host "Duration: $([math]::Round($Results.Duration, 2))s"
    Write-Host ""
}

function Format-JsonOutput {
    param($Results)

    # Convert to cleaner JSON structure
    $jsonResults = @{
        StartTime = $Results.StartTime
        Duration = $Results.Duration
        TotalTests = $Results.TotalTests
        PassedTests = $Results.PassedTests
        FailedTests = $Results.FailedTests
        SkippedTests = $Results.SkippedTests
        Examples = @()
    }

    foreach ($example in $Results.Examples) {
        $exJson = @{
            Name = $example.Name
            TestCases = @()
        }

        foreach ($tc in $example.TestCases) {
            $tcJson = @{
                Name = $tc.Name
                Modes = @()
            }

            foreach ($m in $tc.Modes) {
                $modeJson = @{
                    Mode = $m.Mode
                    Duration = $m.Duration
                }

                if ($m.Skipped) {
                    $modeJson.Skipped = $true
                    $modeJson.SkipReason = $m.SkipReason
                } else {
                    $modeJson.Passed = $m.Passed
                }

                $tcJson.Modes += $modeJson
            }

            $exJson.TestCases += $tcJson
        }

        $jsonResults.Examples += $exJson
    }

    $jsonResults | ConvertTo-Json -Depth 10
}

# ========== Main Entry Point ==========

try {
    # Ensure project is built
    Write-Host "Building SharpTS..." -ForegroundColor Cyan
    $buildResult = Invoke-ProcessWithTimeout -FilePath "dotnet" -Arguments @("build", "-c", "Debug") -Timeout $Script:BuildTimeout
    if (-not $buildResult.Success) {
        Write-Host "Build failed:" -ForegroundColor Red
        Write-Host $buildResult.Error
        exit 1
    }
    Write-Host "Build completed." -ForegroundColor Green
    Write-Host ""

    # Initialize temp directory
    Initialize-TempDirectory

    # Run tests
    $results = Invoke-AllTests

    # Output results
    switch ($OutputFormat) {
        "json" { Format-JsonOutput -Results $results }
        "table" { Format-TableOutput -Results $results }
        "verbose" {
            Write-Host ""
            Format-TableOutput -Results $results
        }
    }

    # Exit with appropriate code
    if ($results.FailedTests -gt 0) {
        exit 1
    }
    exit 0
}
finally {
    # Cleanup
    Remove-TempDirectory
}
