[CmdletBinding()]
param([string]$WorkflowPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($WorkflowPath)) {
    $WorkflowPath = Join-Path $repositoryRoot '.github/workflows/publish.yml'
}
$workflow = Get-Content -LiteralPath $WorkflowPath -Raw
$versionStep = [regex]::Match($workflow, '(?ms)^      - name: Determine version\r?\n.*?        run: \|\r?\n(?<script>(?:          [^\r\n]*(?:\r?\n|\z))+)')
if (-not $versionStep.Success) { throw 'Cannot locate the actual release version script.' }
$versionScript = ($versionStep.Groups['script'].Value -replace '(?m)^          ', '').Replace("`r`n", "`n")

function Get-ReleaseCondition([string]$BlockPattern) {
    $block = [regex]::Match($workflow, $BlockPattern)
    if (-not $block.Success) { throw "Cannot locate release condition block: $BlockPattern" }
    $condition = [regex]::Match($block.Value, '(?m)^\s+if: (?<condition>[^\r\n]+)')
    if (-not $condition.Success) { throw "Missing release condition: $BlockPattern" }
    return $condition.Groups['condition'].Value
}

# Interpret only the finite expressions used by these guards. An unfamiliar
# expression fails instead of guessing GitHub Actions expression semantics.
function Test-ReleaseCondition([string]$Condition, [string]$EventName, [string]$Ref, [bool]$Stable) {
    if ($Condition -ceq "needs.build.outputs.is_stable == 'true' && vars.WINGET_AUTOMATION_ENABLED == 'true'") {
        return $Stable
    }
    $pattern = "^(?:(?<push>github\.event_name == 'push') && )?startsWith\(github\.ref, 'refs/tags/v'\)(?<winget> && needs\.build\.outputs\.is_stable == 'true' && vars\.WINGET_AUTOMATION_ENABLED == 'true')?$"
    $guard = [regex]::Match($Condition, $pattern)
    if (-not $guard.Success) { throw "Unsupported release guard expression: $Condition" }
    return $Ref.StartsWith('refs/tags/v', [StringComparison]::Ordinal) -and
        (-not $guard.Groups['push'].Success -or $EventName -ceq 'push') -and
        (-not $guard.Groups['winget'].Success -or $Stable)
}

$conditions = [ordered]@{
    Preflight = Get-ReleaseCondition '(?ms)^      - name: Preflight NuGet package IDs and artifacts\r?\n.*?(?=^      - |\z)'
    NuGet = Get-ReleaseCondition '(?ms)^  publish-nuget:\r?\n.*?(?=^  [A-Za-z0-9_-]+:\r?\n|\z)'
    Release = Get-ReleaseCondition '(?ms)^  release:\r?\n.*?(?=^  [A-Za-z0-9_-]+:\r?\n|\z)'
    WinGet = Get-ReleaseCondition '(?ms)^  winget:\r?\n.*?(?=^  [A-Za-z0-9_-]+:\r?\n|\z)'
}

if ($IsWindows) {
    $gitRoot = Split-Path -Parent (Split-Path -Parent (Get-Command git -ErrorAction Stop).Source)
    $bash = Join-Path $gitRoot 'bin/bash.exe'
    if (-not (Test-Path -LiteralPath $bash -PathType Leaf)) { throw 'Git for Windows bash is required to test the release version script.' }
}
else { $bash = (Get-Command bash -ErrorAction Stop).Source }

$cases = @(
    @{ Name = 'branch dispatch'; Event = 'workflow_dispatch'; Ref = 'refs/heads/main'; Version = '0.0.0-dryrun.42'; Stable = $false; Publish = $false; Run = 42 },
    @{ Name = 'stable-tag dispatch'; Event = 'workflow_dispatch'; Ref = 'refs/tags/v2.0.0'; Version = '0.0.0-dryrun.42'; Stable = $false; Publish = $false; Run = 42 },
    @{ Name = 'prerelease-tag dispatch'; Event = 'workflow_dispatch'; Ref = 'refs/tags/v2.0.0-rc.1'; Version = '0.0.0-dryrun.42'; Stable = $false; Publish = $false; Run = 42 },
    @{ Name = 'nonversion-tag dispatch'; Event = 'workflow_dispatch'; Ref = 'refs/tags/vinvalid'; Version = '0.0.0-dryrun.42'; Stable = $false; Publish = $false; Run = 42 },
    @{ Name = 'stable-tag push'; Event = 'push'; Ref = 'refs/tags/v2.0.0'; Version = '2.0.0'; Stable = $true; Publish = $true; Run = 42 },
    @{ Name = 'prerelease-tag push'; Event = 'push'; Ref = 'refs/tags/v2.0.0-rc.1'; Version = '2.0.0-rc.1'; Stable = $false; Publish = $true; Run = 42 },
    @{ Name = 'stable-tag push rerun'; Event = 'push'; Ref = 'refs/tags/v2.0.0'; Version = '2.0.0'; Stable = $true; Publish = $true; Run = 43 },
    @{ Name = 'branch push'; Event = 'push'; Ref = 'refs/heads/main'; Version = '0.0.0-dryrun.42'; Stable = $false; Publish = $false; Run = 42 },
    @{ Name = 'other-tag push'; Event = 'push'; Ref = 'refs/tags/release-2.0.0'; Version = '0.0.0-dryrun.42'; Stable = $false; Publish = $false; Run = 42 }
)
$scratch = Join-Path ([IO.Path]::GetTempPath()) "sharpts-release-dispatch-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $scratch | Out-Null
$cleanupSafe = $true
try {
    foreach ($case in $cases) {
        $outputFile = Join-Path $scratch 'output'
        $environmentFile = Join-Path $scratch 'environment'
        [IO.File]::WriteAllText($outputFile, '')
        [IO.File]::WriteAllText($environmentFile, '')
        $process = [Diagnostics.Process]::new()
        try {
            $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($bash)
            $process.StartInfo.UseShellExecute = $false
            $process.StartInfo.CreateNoWindow = $true
            $process.StartInfo.RedirectStandardOutput = $true
            $process.StartInfo.RedirectStandardError = $true
            $process.StartInfo.ArgumentList.Add('-e')
            $process.StartInfo.ArgumentList.Add('-c')
            $process.StartInfo.ArgumentList.Add($versionScript)
            $process.StartInfo.Environment['GITHUB_EVENT_NAME'] = $case.Event
            $process.StartInfo.Environment['GITHUB_REF'] = $case.Ref
            $process.StartInfo.Environment['GITHUB_REF_NAME'] = ($case.Ref -replace '^refs/(heads|tags)/', '')
            $process.StartInfo.Environment['GITHUB_RUN_NUMBER'] = [string]$case.Run
            $process.StartInfo.Environment['GITHUB_OUTPUT'] = $outputFile.Replace('\', '/')
            $process.StartInfo.Environment['GITHUB_ENV'] = $environmentFile.Replace('\', '/')
            $process.Start() | Out-Null
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
            $timedOut = -not $process.WaitForExit(10000)
            if ($timedOut) {
                $process.Kill($true)
                if (-not $process.WaitForExit(5000)) {
                    $cleanupSafe = $false
                    throw "Release version process did not exit after termination: $($case.Name)"
                }
            }
            if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000)) {
                throw "Release version output pipes exceeded their test deadline: $($case.Name)"
            }
            if ($timedOut) { throw "Release version script exceeded its test deadline: $($case.Name)" }
            if ($process.ExitCode -ne 0) { throw "Release version script failed: $($case.Name): $($stderr.GetAwaiter().GetResult())" }
            $null = $stdout.GetAwaiter().GetResult()
        }
        finally { $process.Dispose() }
        $outputs = @{}
        foreach ($line in Get-Content -LiteralPath $outputFile) {
            $key, $value = $line.Split('=', 2)
            $outputs[$key] = $value
        }
        if ($outputs['VERSION'] -cne $case.Version -or $outputs['IS_STABLE'] -cne $case.Stable.ToString().ToLowerInvariant() -or
            (Get-Content -LiteralPath $environmentFile -Raw).Trim() -cne "MinVerVersionOverride=$($case.Version)") {
            throw "Incorrect release version/stability outputs: $($case.Name)"
        }
        foreach ($site in $conditions.Keys) {
            $expected = $case.Publish -and ($site -ne 'WinGet' -or $case.Stable)
            if ((Test-ReleaseCondition $conditions[$site] $case.Event $case.Ref $case.Stable) -ne $expected) {
                throw "Incorrect $site eligibility: $($case.Name)"
            }
        }
        Write-Host "PASS: release event/ref contract: $($case.Name)"
    }
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolvedScratch) -cne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) -or
        [IO.Path]::GetFileName($resolvedScratch) -notmatch '^sharpts-release-dispatch-[a-f0-9]{32}$') {
        throw "Refusing to remove unexpected release test directory: $resolvedScratch"
    }
    if ($cleanupSafe) { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force }
    else { Write-Warning "Retained release test directory because its owned process did not exit: $resolvedScratch" }
}
Write-Host "Release dispatch truth table: $($cases.Count) cases passed; only the version script executed, no workflow or publication was invoked."
