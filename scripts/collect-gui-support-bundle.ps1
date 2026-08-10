[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$DiagnosticsRoot = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'SharpTS.Gui'),
    [string]$ApplicationName,
    [ValidateRange(1, 365)][int]$MaximumAgeDays = 14,
    [ValidateRange(1, 200)][int]$MaximumFiles = 40,
    [switch]$IncludeTraces,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)
if ([IO.Path]::GetExtension($resolvedOutput) -ne '.zip') { throw 'OutputPath must end in .zip.' }
if (Test-Path -LiteralPath $resolvedOutput) {
    if (-not $Force) { throw "Support bundle already exists; pass -Force to replace it: $resolvedOutput" }
    Remove-Item -LiteralPath $resolvedOutput -Force
}
$outputParent = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($outputParent)) { New-Item -ItemType Directory -Path $outputParent -Force | Out-Null }

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "sharpts-gui-support-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $cutoff = [DateTime]::UtcNow.AddDays(-$MaximumAgeDays)
    $patterns = @([pscustomobject]@{ Directory = 'Errors'; Pattern = 'sharpts-gui-error-*.log' })
    if ($IncludeTraces) { $patterns += [pscustomobject]@{ Directory = 'Traces'; Pattern = 'sharpts-gui-host-*.json' } }
    $candidates = foreach ($entry in $patterns) {
        $directory = Join-Path $DiagnosticsRoot $entry.Directory
        if (Test-Path -LiteralPath $directory -PathType Container) {
            Get-ChildItem -LiteralPath $directory -Filter $entry.Pattern -File |
                Where-Object { $_.LastWriteTimeUtc -ge $cutoff -and $_.Length -le 10MB }
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($ApplicationName)) {
        $safeName = $ApplicationName -replace '[^A-Za-z0-9._-]', '-'
        $candidates = @($candidates | Where-Object Name -like "*-$safeName-*")
    }
    $selected = @($candidates | Sort-Object -Property @(
        @{ Expression = 'LastWriteTimeUtc'; Descending = $true },
        @{ Expression = 'Name'; Descending = $false }) | Select-Object -First $MaximumFiles)
    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $temporaryPath = [IO.Path]::GetTempPath().TrimEnd('\')
    $copied = foreach ($file in $selected) {
        $category = $file.Directory.Name
        $destinationDirectory = Join-Path $temporaryRoot $category
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        $destination = Join-Path $destinationDirectory $file.Name
        $content = Get-Content -LiteralPath $file.FullName -Raw
        if (-not [string]::IsNullOrWhiteSpace($userProfile)) { $content = $content.Replace($userProfile, '%USERPROFILE%', [StringComparison]::OrdinalIgnoreCase) }
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath)) { $content = $content.Replace($temporaryPath, '%TEMP%', [StringComparison]::OrdinalIgnoreCase) }
        [IO.File]::WriteAllText($destination, $content, [Text.UTF8Encoding]::new($false))
        Get-Item -LiteralPath $destination
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
        application = $ApplicationName
        osDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        frameworkDescription = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        traceContentIncluded = [bool]$IncludeTraces
        files = @($copied | ForEach-Object {
            [ordered]@{
                path = [IO.Path]::GetRelativePath($temporaryRoot, $_.FullName).Replace('\', '/')
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        })
    }
    [IO.File]::WriteAllText((Join-Path $temporaryRoot 'support.json'), ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    Compress-Archive -Path (Join-Path $temporaryRoot '*') -DestinationPath $resolvedOutput -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
Write-Host "SharpTS GUI support bundle: $resolvedOutput"
