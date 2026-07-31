param(
    [string]$LogPath = "aot-publish.log",
    [string]$SummaryPath = "aot-publish-warning-summary.json",
    [string]$BaselinePath = ".github/aot-publish-warning-baseline.json",
    [switch]$EnforceBaseline,
    [switch]$UpdateBaseline
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

function Get-WarningSubject([string]$Message)
{
    $separatorIndex = $Message.IndexOf(": ", [System.StringComparison]::Ordinal)
    if ($separatorIndex -ge 0)
    {
        return $Message.Substring(0, $separatorIndex)
    }

    if ($Message -match "^Assembly '[^']+'")
    {
        return $Matches[0]
    }

    return $Message
}

function New-Inventory($Warnings)
{
    $items = @($Warnings)
    return [ordered]@{
        total = $items.Count
        codes = @(
            $items |
                Group-Object code |
                Sort-Object Name |
                ForEach-Object {
                    [ordered]@{ code = $_.Name; count = $_.Count }
                }
        )
        subjects = @(
            $items |
                Group-Object code, subject |
                ForEach-Object {
                    $first = $_.Group[0]
                    [pscustomobject][ordered]@{
                        code = $first.code
                        subject = $first.subject
                        count = $_.Count
                    }
                } |
                Sort-Object -Property code,subject
        )
    }
}

function ConvertTo-ComparisonRecords($Inventory)
{
    @(
        "total|$($Inventory.total)"
        $Inventory.codes | ForEach-Object {
            "code|$($_.code)|$($_.count)"
        }
        $Inventory.subjects | ForEach-Object {
            "subject|$($_.code)|$($_.subject)|$($_.count)"
        }
    )
}

$logFullPath = Resolve-RepoPath $LogPath
$summaryFullPath = Resolve-RepoPath $SummaryPath
$baselineFullPath = Resolve-RepoPath $BaselinePath

if (-not (Test-Path -LiteralPath $logFullPath))
{
    throw "Native AOT publish log not found at '$logFullPath'."
}

$warningPattern = "warning (?<code>IL\d{4}): (?<message>.+)$"
$projectSuffixPattern = "\s+\[[^\]]+\.csproj\]\s*$"
$escapePattern = [char]27 + "\[[0-9;]*m"
$parsedWarnings = [System.Collections.Generic.List[object]]::new()

foreach ($rawLine in [System.IO.File]::ReadLines($logFullPath))
{
    $line = $rawLine -replace $escapePattern, ""
    if ($line -notmatch $warningPattern)
    {
        continue
    }

    $code = $Matches.code
    $message = ($Matches.message -replace $projectSuffixPattern, "").Trim()
    $subject = Get-WarningSubject $message
    $isProjectOwned =
        $subject.StartsWith("SharpTS.", [System.StringComparison]::Ordinal) -or
        $subject.Equals("Assembly 'SharpTS'", [System.StringComparison]::Ordinal)

    $parsedWarnings.Add([pscustomobject]@{
        code = $code
        subject = $subject
        owner = if ($isProjectOwned) { "project" } else { "external" }
    })
}

$projectWarnings = @($parsedWarnings | Where-Object owner -eq "project")
$externalWarnings = @($parsedWarnings | Where-Object owner -eq "external")
$summary = [ordered]@{
    schemaVersion = 1
    total = $parsedWarnings.Count
    project = New-Inventory $projectWarnings
    external = New-Inventory $externalWarnings
}

$summaryDirectory = Split-Path -Parent $summaryFullPath
if ($summaryDirectory)
{
    New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
}
$summaryJson = $summary | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($summaryFullPath, $summaryJson + [Environment]::NewLine)

Write-Host "Native AOT publish warnings: $($summary.total)"
Write-Host "  SharpTS-owned: $($summary.project.total)"
foreach ($entry in $summary.project.codes | Sort-Object count -Descending)
{
    Write-Host ("    {0,-7} {1,5}" -f $entry.code, $entry.count)
}
Write-Host "  External:      $($summary.external.total)"
foreach ($entry in $summary.external.codes | Sort-Object count -Descending)
{
    Write-Host ("    {0,-7} {1,5}" -f $entry.code, $entry.count)
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
    [System.IO.File]::WriteAllText(
        $baselineFullPath,
        $summaryJson + [Environment]::NewLine)
    Write-Host "Updated baseline: $baselineFullPath"
}

if ($EnforceBaseline)
{
    if (-not (Test-Path -LiteralPath $baselineFullPath))
    {
        throw "Native AOT publish baseline not found at '$baselineFullPath'."
    }

    $baseline = Get-Content -LiteralPath $baselineFullPath -Raw | ConvertFrom-Json
    $expectedProject = ConvertTo-ComparisonRecords $baseline.project
    $actualProject = ConvertTo-ComparisonRecords ([pscustomobject]$summary.project)
    $projectDifferences = @(Compare-Object $expectedProject $actualProject)
    if ($projectDifferences.Count -ne 0)
    {
        Write-Error "SharpTS-owned Native AOT publish warnings differ from the committed baseline."
        foreach ($difference in $projectDifferences)
        {
            $meaning = if ($difference.SideIndicator -eq "=>") { "actual" } else { "baseline" }
            Write-Error "  [$meaning] $($difference.InputObject)"
        }
        throw "Update the baseline only in the PR that explains the project warning changes."
    }

    Write-Host "SharpTS-owned publish inventory matches '$baselineFullPath'."

    $expectedExternal = ConvertTo-ComparisonRecords $baseline.external
    $actualExternal = ConvertTo-ComparisonRecords ([pscustomobject]$summary.external)
    $externalDifferences = @(Compare-Object $expectedExternal $actualExternal)
    if ($externalDifferences.Count -ne 0)
    {
        Write-Warning (
            "External Native AOT diagnostics differ from the observed baseline. " +
            "This is informational because SDK and dependency servicing can change them.")
        foreach ($difference in $externalDifferences)
        {
            $meaning = if ($difference.SideIndicator -eq "=>") { "actual" } else { "baseline" }
            Write-Warning "  [$meaning] $($difference.InputObject)"
        }
    }
}
