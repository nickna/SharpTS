# catch-hanging-test.ps1
# Runs tests in a loop with --blame-hang until a hang is detected.
# The hanging test name and dump files are preserved in TestResults/.

param(
    [int]$TimeoutSeconds = 120,
    [int]$MaxIterations = 0  # 0 = unlimited
)

$iteration = 0
$resultDir = "tests/SharpTS.Tests/TestResults"

# Clean old results so we only see fresh blame output
if (Test-Path $resultDir) {
    Remove-Item $resultDir -Recurse -Force
}

while ($true) {
    $iteration++
    $timestamp = Get-Date -Format "HH:mm:ss"
    Write-Host "[$timestamp] Run #$iteration" -ForegroundColor Cyan

    dotnet test --blame-hang --blame-hang-timeout "${TimeoutSeconds}s" --no-build 2>&1 | Out-Null
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Write-Host ""
        Write-Host "[$timestamp] HANG DETECTED on run #$iteration (exit code $exitCode)" -ForegroundColor Red

        # Find and display the sequence XML that identifies the hanging test
        $seqFiles = Get-ChildItem -Path $resultDir -Filter "Sequence_*.xml" -Recurse -ErrorAction SilentlyContinue
        foreach ($f in $seqFiles) {
            Write-Host ""
            Write-Host "Blame sequence file: $($f.FullName)" -ForegroundColor Yellow
            [xml]$xml = Get-Content $f.FullName
            foreach ($test in $xml.TestSequence.Test) {
                $status = if ($test.Completed -eq "False") { "HUNG" } else { "OK" }
                $color = if ($status -eq "HUNG") { "Red" } else { "Green" }
                Write-Host "  [$status] $($test.DisplayName)" -ForegroundColor $color
            }
        }

        # List dump files
        $dumps = Get-ChildItem -Path $resultDir -Filter "*_hangdump.dmp" -Recurse -ErrorAction SilentlyContinue
        if ($dumps) {
            Write-Host ""
            Write-Host "Hang dumps:" -ForegroundColor Yellow
            foreach ($d in $dumps) {
                Write-Host "  $($d.FullName)"
            }
        }

        Write-Host ""
        Write-Host "Stopping after $iteration run(s)." -ForegroundColor Cyan
        break
    }

    Write-Host "  Passed" -ForegroundColor Green

    # Clean results between passing runs to avoid accumulation
    if (Test-Path $resultDir) {
        Remove-Item $resultDir -Recurse -Force
    }

    if ($MaxIterations -gt 0 -and $iteration -ge $MaxIterations) {
        Write-Host "Reached $MaxIterations iterations with no hang. Stopping." -ForegroundColor Cyan
        break
    }
}
