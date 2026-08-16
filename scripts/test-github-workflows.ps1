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

    foreach ($upload in [regex]::Matches(
        $content,
        '(?ms)^\s*-?\s*uses:\s*actions/upload-artifact@[0-9a-f]{40}[^\r\n]*\r?\n(?<body>(?:\s{8,}[^\r\n]*\r?\n){1,20})')) {
        if ($upload.Groups['body'].Value -notmatch '(?m)^\s+retention-days:\s*\d+\s*$') {
            $errors.Add("$relativePath upload-artifact step must declare retention-days.")
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

$desktop = Get-Content -LiteralPath (Join-Path $workflowRoot 'desktop-gui.yml') -Raw
foreach ($requiredText in @(
    'name: Desktop GUI',
    'cancel-in-progress: ${{ github.event_name == ''pull_request'' }}',
    'scripts/get-desktop-gui-ci-scope.ps1',
    'runs-on: ubuntu-24.04',
    'retention-days: 3',
    'retention-days: 7',
    'runs-on: windows-11-vs2026-arm',
    'name: Gate'
)) {
    if (-not $desktop.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("desktop-gui.yml is missing routed desktop contract text: $requiredText")
    }
}
foreach ($forbiddenText in @(
    'windows-desktop-preview',
    'macos-desktop-preview',
    'self-hosted',
    'Hosted scheduler and lifecycle conformance`n        run: dotnet test tests/SharpTS.Tests'
)) {
    if ($desktop.Contains($forbiddenText, [StringComparison]::Ordinal)) {
        $errors.Add("desktop-gui.yml retains duplicated or obsolete text: $forbiddenText")
    }
}
$windowsJobStart = $desktop.IndexOf('  windows-x64:', [StringComparison]::Ordinal)
$windowsJobEnd = $desktop.IndexOf('  windows-arm64-cross-publish:', [StringComparison]::Ordinal)
$windowsJob = $desktop.Substring($windowsJobStart, $windowsJobEnd - $windowsJobStart)
if ($windowsJob -match 'FullyQualifiedName~HostedInterpreterRuntimeTests' -or
    $windowsJob -match 'dotnet test tests/gui-conformance/SharpTS\.Gui\.Conformance\.Tests') {
    $errors.Add('desktop-gui.yml duplicates Windows tests already executed by CI.')
}

$scopeScript = Join-Path $repositoryRoot 'scripts\get-desktop-gui-ci-scope.ps1'
$commonScope = & $scopeScript -ChangedPath 'src/SharpTS.Gui/runtime.ts'
$windowsScope = & $scopeScript -ChangedPath 'distribution/windows/AppxManifest.xml'
$macScope = & $scopeScript -ChangedPath 'distribution/macos/Entitlements.plist'
$irrelevantScope = & $scopeScript -ChangedPath 'docs/README.md'
if (-not $commonScope.Windows -or -not $commonScope.MacOS) { $errors.Add('Shared GUI changes must enable both desktop platforms.') }
if (-not $windowsScope.Windows -or $windowsScope.MacOS) { $errors.Add('Windows-only distribution changes must enable only Windows jobs.') }
if ($macScope.Windows -or -not $macScope.MacOS) { $errors.Add('macOS-only distribution changes must enable only macOS jobs.') }
if ($irrelevantScope.Windows -or $irrelevantScope.MacOS) { $errors.Add('Unrelated changes must skip desktop platform jobs.') }

$publish = Get-Content -LiteralPath (Join-Path $workflowRoot 'publish.yml') -Raw
foreach ($requiredText in @('./scripts/sync-gui-version.ps1 -Version','./scripts/sync-gui-version.ps1 -Check -Version','MinVerVersionOverride=$VERSION','environment: nuget-release','id-token: write','NuGet/login@8d196754b4036150537f80ac539e15c2f1028841','user: nbn','steps.nuget_login.outputs.NUGET_API_KEY','-p:MinVerVersionOverride=${{ steps.version.outputs.VERSION }}','-p:SharpTSGuiHostLibrary=true','SharpTS.Gui.Sdk.${{ steps.version.outputs.VERSION }}.nupkg','-PackageVersion "${{ steps.version.outputs.VERSION }}"','SharpTS.Gui.Sdk.${{ needs.build.outputs.version }}.nupkg')) {
    if (-not $publish.Contains($requiredText, [StringComparison]::Ordinal)) { $errors.Add("publish.yml is missing unified GUI release contract text: $requiredText") }
}
foreach ($forbiddenText in @('SharpTSGuiSkipPack','Invoke-WebRequest','gui_package_filename','PACKAGE_FILE_NAME','sync-gui-preview-version','secrets.NUGET_API_KEY')) {
    if ($publish.Contains($forbiddenText, [StringComparison]::Ordinal)) { $errors.Add("publish.yml retains fixed or obsolete GUI publication logic: $forbiddenText") }
}
$wingetRequiredText = @(
    'is_stable: ${{ steps.version.outputs.IS_STABLE }}',
    'IS_STABLE=false',
    'IS_STABLE=$IS_STABLE',
    "^v[0-9]+\.[0-9]+\.[0-9]+$",
    "needs: [build, release]",
    "needs.build.outputs.is_stable == 'true' && vars.WINGET_AUTOMATION_ENABLED == 'true'",
    'environment: winget-release',
    'WINGET_CREATE_GITHUB_TOKEN: ${{ secrets.WINGET_CREATE_GITHUB_TOKEN }}',
    'SharpTS.SharpTS',
    'SharpTS.SharpTS.NativeAOT',
    'https://github.com/microsoft/winget-create/releases/download/v1.12.13.0/wingetcreate.exe',
    '24042bd37915805615e6cf969ac57c6439124c3fe85823327f5f3fb24bd9ffea',
    '--submit',
    '--no-open',
    'win-x64.zip|x64',
    'win-arm64.zip|arm64'
)
foreach ($requiredText in $wingetRequiredText) {
    if (-not $publish.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("publish.yml is missing WinGet release contract text: $requiredText")
    }
}
$wingetJobStart = $publish.IndexOf("`n  winget:", [StringComparison]::Ordinal)
if ($wingetJobStart -lt 0) {
    $errors.Add('publish.yml is missing the downstream WinGet job.')
}
else {
    $wingetJob = $publish.Substring($wingetJobStart)
    if ($wingetJob -match '(?m)^\s*(?:--token|-t)\s') {
        $errors.Add('publish.yml must provide the WinGetCreate token only through WINGET_CREATE_GITHUB_TOKEN.')
    }
}
$releaseCommand = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\nuget-release.ps1') -Raw
if ($releaseCommand -notmatch '\$VerificationAttempts\s*=\s*30' -or $releaseCommand -notmatch '\$VerificationDelaySeconds\s*=\s*20') { $errors.Add('nuget-release.ps1 must poll NuGet 30 times at 20-second intervals by default.') }

if ($errors.Count -gt 0) {
    throw "GitHub workflow policy failed:`n - $($errors -join "`n - ")"
}

Write-Host "Validated $($workflowFiles.Count) workflows: immutable actions, least-privilege declarations, fixed runners, and pinned toolchains."
