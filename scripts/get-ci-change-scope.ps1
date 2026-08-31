[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$BaseSha,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$HeadSha,

    [string]$DifftasticPath = '',

    [string]$ToolRoot = $(
        if ($env:RUNNER_TEMP) { Join-Path $env:RUNNER_TEMP 'sharpts-ci-tools' }
        else { Join-Path ([IO.Path]::GetTempPath()) 'sharpts-ci-tools' }
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$difftasticVersion = '0.70.0'
$difftasticAssets = @{
    LinuxX64 = @{
        Archive = 'difft-x86_64-unknown-linux-gnu.tar.gz'
        Sha256 = '2997d2bbe620534edbd79b0049f00ce84eef3fedb15c7822456d58e38d8b05c9'
        Executable = 'difft'
    }
    WindowsX64 = @{
        Archive = 'difft-x86_64-pc-windows-msvc.zip'
        Sha256 = 'b563ae76e22ce28c7080a8b628cfabf6fa86f9ee114a0f5697bc2ca26f9ce1d7'
        Executable = 'difft.exe'
    }
}

function New-ScopeResult(
    [ValidateSet('full', 'csharp-trivia-only', 'docs-only')]
    [string]$Mode,
    [string]$Reason,
    [string[]]$ChangedPaths,
    [string[]]$BuildAffectingPaths
) {
    return [pscustomobject]@{
        Mode = $Mode
        Reason = $Reason
        ChangedPaths = @($ChangedPaths)
        BuildAffectingPaths = @($BuildAffectingPaths)
    }
}

function Test-DocumentationPath([string]$Path) {
    $normalized = $Path.Replace('\', '/')
    return $normalized -like 'docs/*' -or
        $normalized -like '*.md' -or
        $normalized -eq 'LICENSE' -or
        $normalized -eq '.gitignore' -or
        $normalized -like '.github/ISSUE_TEMPLATE/*' -or
        $normalized -like '.github/PULL_REQUEST_TEMPLATE*'
}

function Get-ChangedEntries {
    $lines = @(& git -c core.quotepath=false diff --name-status --find-renames=50% $BaseSha $HeadSha --)
    if ($LASTEXITCODE -ne 0) {
        throw "git diff failed for $BaseSha..$HeadSha."
    }

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = @($line -split "`t")
        if ($parts.Count -lt 2) {
            throw "Unexpected git diff entry: $line"
        }

        $status = $parts[0]
        if ($status.StartsWith('R', [StringComparison]::Ordinal) -or
            $status.StartsWith('C', [StringComparison]::Ordinal)) {
            if ($parts.Count -ne 3) { throw "Unexpected rename/copy entry: $line" }
            $entries.Add([pscustomobject]@{
                Status = $status
                OldPath = $parts[1].Replace('\', '/')
                Path = $parts[2].Replace('\', '/')
            })
        }
        else {
            if ($parts.Count -ne 2) { throw "Unexpected changed-file entry: $line" }
            $entries.Add([pscustomobject]@{
                Status = $status
                OldPath = $parts[1].Replace('\', '/')
                Path = $parts[1].Replace('\', '/')
            })
        }
    }
    return @($entries)
}

function Get-Difftastic {
    if ($DifftasticPath) {
        if (Test-Path -LiteralPath $DifftasticPath -PathType Leaf) {
            return (Resolve-Path -LiteralPath $DifftasticPath).Path
        }
        Write-Warning "The requested Difftastic executable does not exist: $DifftasticPath"
        return $null
    }

    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ($IsLinux -and $architecture -eq 'X64') {
        $asset = $difftasticAssets.LinuxX64
    }
    elseif ($IsWindows -and $architecture -eq 'X64') {
        $asset = $difftasticAssets.WindowsX64
    }
    else {
        Write-Warning "No pinned Difftastic asset is configured for this runner."
        return $null
    }

    $installRoot = Join-Path $ToolRoot "difftastic-$difftasticVersion"
    $executable = Join-Path $installRoot $asset.Executable
    if (Test-Path -LiteralPath $executable -PathType Leaf) { return $executable }

    $downloadRoot = Join-Path $ToolRoot ('download-' + [Guid]::NewGuid().ToString('N'))
    $archivePath = Join-Path $downloadRoot $asset.Archive
    try {
        New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
        $uri = "https://github.com/Wilfred/difftastic/releases/download/$difftasticVersion/$($asset.Archive)"
        Write-Host "Downloading Difftastic $difftasticVersion from $uri"
        Invoke-WebRequest -Uri $uri -OutFile $archivePath

        $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $asset.Sha256) {
            throw "Difftastic archive checksum mismatch. Expected $($asset.Sha256), got $actualHash."
        }

        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
        if ($asset.Archive.EndsWith('.zip', [StringComparison]::Ordinal)) {
            Expand-Archive -LiteralPath $archivePath -DestinationPath $installRoot -Force
        }
        else {
            & tar --extract --gzip --file $archivePath --directory $installRoot
            if ($LASTEXITCODE -ne 0) { throw "Unable to extract $($asset.Archive)." }
            & chmod +x $executable
            if ($LASTEXITCODE -ne 0) { throw 'Unable to mark Difftastic executable.' }
        }

        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Difftastic archive did not contain $($asset.Executable)."
        }
        return $executable
    }
    catch {
        if ($_.Exception.Message -like 'Difftastic archive checksum mismatch*') { throw }
        Write-Warning "Difftastic is unavailable; selecting full CI. $($_.Exception.Message)"
        return $null
    }
    finally {
        if (Test-Path -LiteralPath $downloadRoot) {
            $resolvedDownload = (Resolve-Path -LiteralPath $downloadRoot).Path
            $resolvedToolRoot = (Resolve-Path -LiteralPath $ToolRoot).Path
            if (-not $resolvedDownload.StartsWith($resolvedToolRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to clean a Difftastic download outside the tool root: $resolvedDownload"
            }
            Remove-Item -LiteralPath $resolvedDownload -Recurse -Force
        }
    }
}

function Write-GitBlob([string]$Revision, [string]$Path, [string]$Destination) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = (Get-Location).ProviderPath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('cat-file')
    $startInfo.ArgumentList.Add('blob')
    $startInfo.ArgumentList.Add("${Revision}:$Path")

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Unable to start git cat-file for $Path." }
    try {
        $stream = [IO.File]::Create($Destination)
        try { $process.StandardOutput.BaseStream.CopyTo($stream) }
        finally { $stream.Dispose() }
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Unable to read ${Revision}:$Path. $errorText"
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-DifftasticCheck([string]$Tool, [string]$OldPath, [string]$NewPath) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Tool
    $startInfo.WorkingDirectory = (Get-Location).ProviderPath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add('--ignore-comments')
    $startInfo.ArgumentList.Add('--check-only')
    $startInfo.ArgumentList.Add('--exit-code')
    $startInfo.ArgumentList.Add($OldPath)
    $startInfo.ArgumentList.Add($NewPath)

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Unable to start Difftastic for $NewPath." }
    try {
        # Drain both redirected streams concurrently so neither pipe can block the process.
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $standardOutputTask.GetAwaiter().GetResult()
            StandardError = $standardErrorTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Test-NoCSharpSyntaxChange([string]$Tool, [string]$Path) {
    $comparisonRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-ci-diff-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $comparisonRoot | Out-Null
        $oldPath = Join-Path $comparisonRoot 'before.cs'
        $newPath = Join-Path $comparisonRoot 'after.cs'
        Write-GitBlob $BaseSha $Path $oldPath
        Write-GitBlob $HeadSha $Path $newPath

        $result = Invoke-DifftasticCheck $Tool $oldPath $newPath
        foreach ($text in @($result.StandardOutput, $result.StandardError)) {
            if (-not [string]::IsNullOrWhiteSpace($text)) { Write-Host $text.TrimEnd() }
        }
        $exitCode = $result.ExitCode
        if ($exitCode -eq 0) { return $true }
        if ($exitCode -eq 1) { return $false }
        throw "Difftastic failed for '$Path' with exit code $exitCode."
    }
    finally {
        if (Test-Path -LiteralPath $comparisonRoot) {
            $resolvedComparison = (Resolve-Path -LiteralPath $comparisonRoot).Path
            $resolvedTemp = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
            if (-not $resolvedComparison.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to clean a comparison outside the temporary directory: $resolvedComparison"
            }
            Remove-Item -LiteralPath $resolvedComparison -Recurse -Force
        }
    }
}

try {
    $entries = @(Get-ChangedEntries)
}
catch {
    return New-ScopeResult 'full' "Unable to enumerate changes: $($_.Exception.Message)" @() @()
}

if ($entries.Count -eq 0) {
    return New-ScopeResult 'full' 'No changed files were reported; selecting full CI.' @() @()
}

$changedPaths = @($entries | ForEach-Object { $_.Path } | Sort-Object -Unique)
$nonDocumentation = @($entries | Where-Object {
    -not (Test-DocumentationPath $_.Path) -or
    -not (Test-DocumentationPath $_.OldPath)
})
if ($nonDocumentation.Count -eq 0) {
    return New-ScopeResult 'docs-only' 'Every changed path is documentation-only.' $changedPaths @()
}

$buildAffectingPaths = @($nonDocumentation | ForEach-Object { $_.Path } | Sort-Object -Unique)
$ineligible = @($nonDocumentation | Where-Object {
    $_.Status -cne 'M' -or -not $_.Path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase)
})
if ($ineligible.Count -gt 0) {
    $first = $ineligible[0]
    return New-ScopeResult 'full' "Change '$($first.Status) $($first.Path)' is not an eligible modified C# file." $changedPaths $buildAffectingPaths
}

$tool = Get-Difftastic
if (-not $tool) {
    return New-ScopeResult 'full' 'Difftastic was unavailable, so the change could not be proven trivia-only.' $changedPaths $buildAffectingPaths
}

foreach ($entry in $nonDocumentation) {
    try {
        if (-not (Test-NoCSharpSyntaxChange $tool $entry.Path)) {
            return New-ScopeResult 'full' "Difftastic found a C# syntax change in '$($entry.Path)'." $changedPaths $buildAffectingPaths
        }
    }
    catch {
        return New-ScopeResult 'full' "Unable to classify '$($entry.Path)': $($_.Exception.Message)" $changedPaths $buildAffectingPaths
    }
}

return New-ScopeResult 'csharp-trivia-only' 'All non-documentation changes modify only C# comments or formatting.' $changedPaths @()
