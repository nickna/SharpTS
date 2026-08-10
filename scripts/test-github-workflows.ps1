[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workflowRoot = Join-Path $repositoryRoot '.github\workflows'
$errors = [System.Collections.Generic.List[string]]::new()
$workflowFiles = @(Get-ChildItem -LiteralPath $workflowRoot -Filter '*.yml' -File)

foreach ($file in $workflowFiles) {
    $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $file.FullName).Replace('\', '/')
    $content = Get-Content -LiteralPath $file.FullName -Raw

    if ($content -notmatch '(?m)^permissions:\s*$') {
        $errors.Add("$relativePath must declare top-level permissions.")
    }
    if ($content -match '(?m)runs-on:\s*[^\r\n]*-latest(?:\s|\]|$)') {
        $errors.Add("$relativePath uses a floating runner label.")
    }
    if ($content -match '(?m)^\s*dotnet-version:') {
        $errors.Add("$relativePath must select .NET through global.json.")
    }

    foreach ($match in [regex]::Matches($content, '(?m)^\s*-?\s*uses:\s*(?<action>[^\s#]+)')) {
        $action = $match.Groups['action'].Value
        if ($action.StartsWith('./', [StringComparison]::Ordinal)) { continue }
        if ($action -notmatch '@[0-9a-f]{40}$') {
            $errors.Add("$relativePath uses a non-immutable action reference: $action")
        }
    }

    $checkouts = [regex]::Matches($content, '(?ms)^\s*- uses: actions/checkout@[0-9a-f]{40}[^\r\n]*\r?\n(?<body>(?:\s{8,}[^\r\n]*\r?\n){0,5})')
    foreach ($checkout in $checkouts) {
        if ($checkout.Groups['body'].Value -notmatch '(?m)^\s+persist-credentials:\s*false\s*$') {
            $errors.Add("$relativePath checkout must disable persisted credentials.")
        }
    }
}

$benchmark = Get-Content -LiteralPath (Join-Path $workflowRoot 'benchmarks.yml') -Raw
if ($benchmark -notmatch 'node-version-file:\s*\.node-version') {
    $errors.Add('benchmarks.yml must use .node-version by default.')
}
if ($benchmark -notmatch 'bun-version-file:\s*\.bun-version') {
    $errors.Add('benchmarks.yml must use .bun-version.')
}

foreach ($workflowName in @('windows-desktop-preview.yml', 'macos-desktop-preview.yml')) {
    $content = Get-Content -LiteralPath (Join-Path $workflowRoot $workflowName) -Raw
    foreach ($requiredPath in @('Directory.Build.props', 'eng/GuiPreviewVersion.props', 'global.json', 'scripts/get-gui-preview-version.ps1')) {
        if (-not $content.Contains($requiredPath, [StringComparison]::Ordinal)) {
            $errors.Add("$workflowName path filters must include $requiredPath.")
        }
    }
    if ($content -match 'SharpTS\.Gui\.Sdk\.0\.3\.0-preview\.1\.nupkg' -or $content -match '-ShortVersion\s+0\.2\.0') {
        $errors.Add("$workflowName contains a duplicated GUI version literal.")
    }
}

if ($errors.Count -gt 0) {
    throw "GitHub workflow policy failed:`n - $($errors -join "`n - ")"
}

Write-Host "Validated $($workflowFiles.Count) workflows: immutable actions, least-privilege declarations, fixed runners, and pinned toolchains."
