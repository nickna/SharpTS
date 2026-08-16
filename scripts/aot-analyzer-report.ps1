param(
    [string]$ProjectPath = "src/SharpTS/SharpTS.csproj",
    [string]$LogPath = "aot-analyzer.log",
    [string]$SummaryPath = "aot-analyzer-summary.json",
    [string]$BaselinePath = ".github/aot-warning-baseline.json",
    [switch]$EnforceBaseline,
    [switch]$UpdateBaseline,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($EnforceBaseline -and $UpdateBaseline)
{
    throw "-EnforceBaseline and -UpdateBaseline are mutually exclusive."
}

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath([string]$Path)
{
    if ([System.IO.Path]::IsPathRooted($Path))
    {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

$projectFullPath = Resolve-RepoPath $ProjectPath
$logFullPath = Resolve-RepoPath $LogPath
$summaryFullPath = Resolve-RepoPath $SummaryPath
$baselineFullPath = Resolve-RepoPath $BaselinePath

if (-not $SkipBuild)
{
    # The ILLink analyzer references are restore-conditioned. A --no-restore build
    # after an ordinary restore can misleadingly report zero IL#### warnings.
    $analyzerProperties = @(
        "-p:IsAotCompatible=true",
        "-p:EnableAotAnalyzer=true",
        "-p:EnableTrimAnalyzer=true",
        "-p:EnableSingleFileAnalyzer=true"
    )

    Push-Location $repoRoot
    try
    {
        & dotnet restore $projectFullPath --force-evaluate @analyzerProperties
        if ($LASTEXITCODE -ne 0)
        {
            throw "Analyzer-aware restore failed with exit code $LASTEXITCODE."
        }

        $logDirectory = Split-Path -Parent $logFullPath
        if ($logDirectory)
        {
            New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
        }

        & dotnet build $projectFullPath `
            --configuration Release `
            --no-restore `
            -t:Rebuild `
            @analyzerProperties 2>&1 |
            Tee-Object -FilePath $logFullPath |
            ForEach-Object {
                $line = $_.ToString()
                if ($line -match "Warning\(s\)|Error\(s\)|error ")
                {
                    Write-Host $line
                }
            }
        $buildExitCode = $LASTEXITCODE
        if ($buildExitCode -ne 0)
        {
            throw "Analyzer build failed with exit code $buildExitCode. See '$logFullPath'."
        }
    }
    finally
    {
        Pop-Location
    }
}
elseif (-not (Test-Path -LiteralPath $logFullPath))
{
    throw "-SkipBuild requires an existing log at '$logFullPath'."
}

$warningPattern =
    "^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\): warning " +
    "(?<code>IL\d{4}): (?<message>.+?) \[(?<project>.+?)\]\s*$"
$escapePattern = [char]27 + "\[[0-9;]*m"
$warningsByFingerprint = [System.Collections.Generic.Dictionary[string, object]]::new(
    [System.StringComparer]::Ordinal)

foreach ($rawLine in [System.IO.File]::ReadLines($logFullPath))
{
    $line = $rawLine -replace $escapePattern, ""
    if ($line -notmatch $warningPattern)
    {
        continue
    }

    $sourcePath = [System.IO.Path]::GetFullPath($Matches.file)
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $sourcePath).Replace("\", "/")
    $warning = [pscustomobject]@{
        file = $relativePath
        line = [int]$Matches.line
        column = [int]$Matches.column
        code = $Matches.code
        message = $Matches.message
        area = ($relativePath -split "/", 2)[0]
    }
    $fingerprint =
        "$($warning.file)|$($warning.line)|$($warning.column)|" +
        "$($warning.code)|$($warning.message)"
    $warningsByFingerprint[$fingerprint] = $warning
}

$warnings = @($warningsByFingerprint.Values)
$summary = [ordered]@{
    schemaVersion = 1
    total = $warnings.Count
    codes = @(
        $warnings |
            Group-Object code |
            Sort-Object Name |
            ForEach-Object {
                [ordered]@{ code = $_.Name; count = $_.Count }
            }
    )
    areas = @(
        $warnings |
            Group-Object area |
            Sort-Object Name |
            ForEach-Object {
                [ordered]@{ area = $_.Name; count = $_.Count }
            }
    )
    fileCodes = @(
        $warnings |
            Group-Object file, code |
            ForEach-Object {
                $first = $_.Group[0]
                [pscustomobject][ordered]@{
                    file = $first.file
                    code = $first.code
                    count = $_.Count
                }
            } |
            Sort-Object -Property file,code
    )
}

$summaryDirectory = Split-Path -Parent $summaryFullPath
if ($summaryDirectory)
{
    New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
}
$summaryJson = $summary | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($summaryFullPath, $summaryJson + [Environment]::NewLine)

Write-Host "AOT/trim/single-file analyzer warnings: $($summary.total)"
foreach ($entry in $summary.codes | Sort-Object count -Descending)
{
    Write-Host ("  {0,-7} {1,5}" -f $entry.code, $entry.count)
}
Write-Host "Summary: $summaryFullPath"
Write-Host "Log:     $logFullPath"

if ($UpdateBaseline)
{
    $baselineDirectory = Split-Path -Parent $baselineFullPath
    if ($baselineDirectory)
    {
        New-Item -ItemType Directory -Force -Path $baselineDirectory | Out-Null
    }
    [System.IO.File]::WriteAllText($baselineFullPath, $summaryJson + [Environment]::NewLine)
    Write-Host "Updated baseline: $baselineFullPath"
}

if ($EnforceBaseline)
{
    if (-not (Test-Path -LiteralPath $baselineFullPath))
    {
        throw "Analyzer baseline not found at '$baselineFullPath'."
    }

    $baseline = Get-Content -LiteralPath $baselineFullPath -Raw | ConvertFrom-Json

    function ConvertTo-ComparisonRecords($Value)
    {
        @(
            "total|$($Value.total)"
            $Value.codes | ForEach-Object { "code|$($_.code)|$($_.count)" }
            $Value.areas | ForEach-Object { "area|$($_.area)|$($_.count)" }
            $Value.fileCodes | ForEach-Object {
                "file-code|$($_.file)|$($_.code)|$($_.count)"
            }
        )
    }

    $expectedRecords = ConvertTo-ComparisonRecords $baseline
    $actualRecords = ConvertTo-ComparisonRecords ([pscustomobject]$summary)
    $differences = @(Compare-Object $expectedRecords $actualRecords)
    if ($differences.Count -ne 0)
    {
        # Write-Host, not Write-Error: under ErrorActionPreference=Stop the first
        # Write-Error would terminate before the per-record diff prints, leaving
        # only the headline in the job log.
        Write-Host "AOT analyzer inventory differs from the committed baseline:"
        foreach ($difference in $differences)
        {
            $meaning = if ($difference.SideIndicator -eq "=>") { "actual" } else { "baseline" }
            Write-Host "  [$meaning] $($difference.InputObject)"
        }
        throw "AOT analyzer inventory differs from the committed baseline. Run scripts/aot-analyzer-report.ps1 -UpdateBaseline only in the PR that explains the warning changes."
    }

    Write-Host "Analyzer inventory matches '$baselineFullPath'."
}
