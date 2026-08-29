[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$workflowRoot = Join-Path $repositoryRoot '.github\workflows'
$errors = [System.Collections.Generic.List[string]]::new()
$workflowFiles = @(Get-ChildItem -LiteralPath $workflowRoot -Filter '*.yml' -File)

function Get-WorkflowJob([string]$Content, [string]$Name, [string]$WorkflowName) {
    $escapedName = [regex]::Escape($Name)
    $match = [regex]::Match($Content, "(?ms)^  ${escapedName}:\r?\n.*?(?=^  [A-Za-z0-9_-]+:\r?\n|\z)")
    if (-not $match.Success) {
        $errors.Add("$WorkflowName is missing the '$Name' job.")
        return ''
    }
    return $match.Value
}

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

$ci = Get-Content -LiteralPath (Join-Path $workflowRoot 'ci.yml') -Raw
foreach ($requiredText in @(
    'scripts/get-ci-change-scope.ps1',
    'scripts/test-ci-change-scope.ps1',
    'fetch-depth: 0',
    "mode: `${{ steps.scope.outputs.mode }}",
    "reason: `${{ steps.scope.outputs.reason }}",
    'name: Lightweight C# validation',
    'name: Gate'
)) {
    if (-not $ci.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("ci.yml is missing change-routing contract text: $requiredText")
    }
}
foreach ($jobName in @('build', 'dap-macos-smoke', 'aot-ratchet', 'native-aot-compile-smoke')) {
    $job = Get-WorkflowJob $ci $jobName 'ci.yml'
    if (-not $job.Contains('needs: workflow-policy', [StringComparison]::Ordinal) -or
        -not $job.Contains("if: needs.workflow-policy.outputs.mode == 'full'", [StringComparison]::Ordinal)) {
        $errors.Add("ci.yml '$jobName' must run only after full change classification.")
    }
}
$lightweightJob = Get-WorkflowJob $ci 'lightweight-validation' 'ci.yml'
if (-not $lightweightJob.Contains("if: needs.workflow-policy.outputs.mode == 'csharp-trivia-only'", [StringComparison]::Ordinal)) {
    $errors.Add('ci.yml lightweight-validation must run only for C# trivia changes.')
}
$ciGateJob = Get-WorkflowJob $ci 'gate' 'ci.yml'
foreach ($requiredText in @('CHANGE_MODE:', "'csharp-trivia-only'", "'docs-only'", "'lightweight-validation'")) {
    if (-not $ciGateJob.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("ci.yml Gate is missing routed-result validation text: $requiredText")
    }
}

$ciScopeScript = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\get-ci-change-scope.ps1') -Raw
foreach ($requiredText in @(
    "difftasticVersion = '0.70.0'",
    '2997d2bbe620534edbd79b0049f00ce84eef3fedb15c7822456d58e38d8b05c9',
    'b563ae76e22ce28c7080a8b628cfabf6fa86f9ee114a0f5697bc2ca26f9ce1d7',
    'Get-FileHash -LiteralPath $archivePath -Algorithm SHA256',
    '--ignore-comments --check-only --exit-code',
    "return New-ScopeResult 'full'"
)) {
    if (-not $ciScopeScript.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("get-ci-change-scope.ps1 is missing pinned or fail-safe routing text: $requiredText")
    }
}

$desktop = Get-Content -LiteralPath (Join-Path $workflowRoot 'desktop-gui.yml') -Raw
foreach ($requiredText in @(
    'name: Desktop GUI',
    'cancel-in-progress: ${{ github.event_name == ''pull_request'' }}',
    'scripts/get-ci-change-scope.ps1',
    'scripts/get-desktop-gui-ci-scope.ps1',
    '-ChangedPath $changeScope.BuildAffectingPaths',
    'runs-on: ubuntu-24.04',
    'retention-days: 3',
    'retention-days: 7',
    'runs-on: windows-11-vs2026-arm',
    'RUN_WINDOWS_ARM64_NATIVE:',
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
foreach ($requiredText in @('./scripts/sync-gui-version.ps1 -Version','./scripts/sync-gui-version.ps1 -Check -Version','MinVerVersionOverride=$VERSION','NuGet/login@8d196754b4036150537f80ac539e15c2f1028841','user: nbn','steps.nuget_login.outputs.NUGET_API_KEY','-p:MinVerVersionOverride=${{ steps.version.outputs.VERSION }}','-p:SharpTSGuiHostLibrary=true','SharpTS.Gui.Sdk.${{ steps.version.outputs.VERSION }}.nupkg','-PackageVersion "${{ steps.version.outputs.VERSION }}"','SharpTS.Gui.Sdk.${{ needs.build.outputs.version }}.nupkg')) {
    if (-not $publish.Contains($requiredText, [StringComparison]::Ordinal)) { $errors.Add("publish.yml is missing unified GUI release contract text: $requiredText") }
}
foreach ($forbiddenText in @('SharpTSGuiSkipPack','Invoke-WebRequest','gui_package_filename','PACKAGE_FILE_NAME','sync-gui-preview-version','secrets.NUGET_API_KEY')) {
    if ($publish.Contains($forbiddenText, [StringComparison]::Ordinal)) { $errors.Add("publish.yml retains fixed or obsolete GUI publication logic: $forbiddenText") }
}

function Get-PublishJob([string] $Name) {
    $escapedName = [regex]::Escape($Name)
    $match = [regex]::Match($publish, "(?ms)^  ${escapedName}:\r?\n.*?(?=^  [A-Za-z0-9_-]+:\r?\n|\z)")
    if (-not $match.Success) {
        $errors.Add("publish.yml is missing the '$Name' job.")
        return ''
    }
    return $match.Value
}

$publishNuGetJob = Get-PublishJob 'publish-nuget'
$verifyNuGetJob = Get-PublishJob 'verify-nuget'
$releaseJob = Get-PublishJob 'release'

foreach ($requiredText in @(
    'needs: [build, binaries, native-binaries]',
    "if: startsWith(github.ref, 'refs/tags/v')",
    'contents: read',
    'id-token: write',
    'timeout-minutes: 15',
    'environment: nuget-release',
    'name: nupkg',
    '-Action Publish'
)) {
    if (-not $publishNuGetJob.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("publish-nuget is missing publication boundary text: $requiredText")
    }
}
foreach ($forbiddenText in @("pattern: 'managed-*'", "pattern: 'native-*'", 'contents: write')) {
    if ($publishNuGetJob.Contains($forbiddenText, [StringComparison]::Ordinal)) {
        $errors.Add("publish-nuget must download only packages and retain only publication permissions: $forbiddenText")
    }
}

foreach ($requiredText in @(
    'needs: publish-nuget',
    'contents: read',
    'timeout-minutes: 75',
    'dotnet tool restore',
    'name: nupkg',
    'dotnet wait-for-package --directory ./nupkg --timeout 01:00:00',
    'if ($LASTEXITCODE -ne 0)'
)) {
    if (-not $verifyNuGetJob.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("verify-nuget is missing availability gate text: $requiredText")
    }
}
foreach ($forbiddenText in @('id-token:', 'contents: write', 'environment:', 'secrets.', 'NUGET_API_KEY', 'NuGet/login@')) {
    if ($verifyNuGetJob.Contains($forbiddenText, [StringComparison]::Ordinal)) {
        $errors.Add("verify-nuget must remain read-only and secret-free: $forbiddenText")
    }
}

foreach ($requiredText in @(
    'needs: [build, binaries, native-binaries, verify-nuget]',
    'contents: write',
    'timeout-minutes: 20',
    "pattern: 'managed-*'",
    "pattern: 'native-*'",
    'softprops/action-gh-release@3d0d9888cb7fd7b750713d6e236d1fcb99157228'
)) {
    if (-not $releaseJob.Contains($requiredText, [StringComparison]::Ordinal)) {
        $errors.Add("release is missing the post-verification release contract text: $requiredText")
    }
}
foreach ($forbiddenText in @('id-token:', 'environment:', 'NUGET_API_KEY', 'NuGet/login@')) {
    if ($releaseJob.Contains($forbiddenText, [StringComparison]::Ordinal)) {
        $errors.Add("release must retain only GitHub Release permissions: $forbiddenText")
    }
}

if ([regex]::Matches($publish, '(?m)^\s+id-token:\s*write\s*$').Count -ne 1) {
    $errors.Add('publish.yml must isolate OIDC write permission to publish-nuget.')
}
if ([regex]::Matches($publish, '(?m)^\s+environment:\s*nuget-release\s*$').Count -ne 1) {
    $errors.Add('publish.yml must isolate the nuget-release environment to publish-nuget.')
}

$toolManifestPath = Join-Path $repositoryRoot '.config\dotnet-tools.json'
if (-not (Test-Path -LiteralPath $toolManifestPath -PathType Leaf)) {
    $errors.Add('The root local-tool manifest is missing.')
}
else {
    $toolManifest = Get-Content -LiteralPath $toolManifestPath -Raw | ConvertFrom-Json
    $waiter = $toolManifest.tools.'martincostello.waitfornugetpackage'
    if ($toolManifest.version -ne 1 -or -not $toolManifest.isRoot -or
        $null -eq $waiter -or $waiter.version -cne '1.3.2' -or
        $waiter.rollForward -ne $false -or
        @($waiter.commands).Count -ne 1 -or $waiter.commands[0] -cne 'dotnet-wait-for-package') {
        $errors.Add('The root tool manifest must pin MartinCostello.WaitForNuGetPackage 1.3.2 with roll-forward disabled.')
    }
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
$releaseModule = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\NuGetRelease.psm1') -Raw
foreach ($obsoleteText in @('VerificationAttempts', 'VerificationDelaySeconds')) {
    if ($releaseCommand.Contains($obsoleteText, [StringComparison]::Ordinal) -or
        $releaseModule.Contains($obsoleteText, [StringComparison]::Ordinal)) {
        $errors.Add("NuGet publication retains the old 30x20-second polling contract: $obsoleteText")
    }
}

if ($errors.Count -gt 0) {
    throw "GitHub workflow policy failed:`n - $($errors -join "`n - ")"
}

Write-Host "Validated $($workflowFiles.Count) workflows: immutable actions, least-privilege declarations, fixed runners, and pinned toolchains."
