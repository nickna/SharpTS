param([string[]]$ChangedPaths = @(), [switch]$Full)
# Explicit deployment/build and test-harness changes exercise the entire matrix.
# Unknown or unavailable diffs are handled by the caller with -Full.
$deployment = $Full.IsPresent
$gui = $Full.IsPresent
$benchmarks = $Full.IsPresent
$methods = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$types = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $ChangedPaths) {
    $path = $path.Replace('\', '/')
    if ($path -match '^(\.github/|scripts/|global\.json$|Directory\.|SharpTS\.sln$|src/SharpTS\.Sdk|src/SharpTS\.Hosting|src/SharpTS/Compilation/ILCompiler|src/SharpTS/Compilation/Assembly|src/SharpTS/Compilation/Native|src/SharpTS/Cli/|tests/SharpTS.Tests/Infrastructure/|tests/SharpTS.Tests/IntegrationTests/CliTestHelper\.cs$|tests/SharpTS.Tests/CompilerTests/HistoricalRuntimeDeploymentTests\.cs$|tests/fixtures/|tests/Shared/|tests/native-guest/)') {
        $deployment = $true
    }
    if ($path -match '^tests/SharpTS.Tests/.*\.cs$') {
        # Changed standalone tests and directly referenced semantic data providers
        # retain ALL their rows. Adding a regression must not force every unrelated
        # deployment sweep back onto the critical path. Deleted/unreadable sources
        # fall back to the complete suite. This expands coverage; it never excludes it.
        $sourcePath = Join-Path (Split-Path -Parent $PSScriptRoot) $path
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { $deployment = $true; continue }
        $source = Get-Content -LiteralPath $sourcePath -Raw
        if ($path -match '/StandaloneDllTests[^/]*\.cs$') {
            $methodMatches = [regex]::Matches($source, '\bpublic\s+(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(')
            if ($methodMatches.Count -eq 0) { $deployment = $true }
            foreach ($match in $methodMatches) { [void]$methods.Add($match.Groups[1].Value) }
        } else {
            foreach ($match in [regex]::Matches($source, '\bclass\s+(\w+)')) { [void]$types.Add($match.Groups[1].Value) }
        }
    }
    if ($path -match '^(\.github/|scripts/|global\.json$|Directory\.|src/SharpTS\.Gui|samples/SharpPaint/|samples/Calculator/|tests/gui-conformance/|src/SharpTS/Hosting/|src/SharpTS\.Hosting)') {
        $gui = $true
    }
    if ($path -match '^(\.github/|scripts/|benchmarks/|global\.json$|Directory\.)') { $benchmarks = $true }
}
$methodNames = @($methods | Sort-Object) -join '|'
$typeNames = @($types | Sort-Object) -join '|'
# Keep workflow outputs and Windows environment blocks bounded. A large change
# expands to Full rather than truncating hints and accidentally deferring a case.
if ($methodNames.Length + $typeNames.Length -gt 12000) { $deployment = $true }
if ($deployment) { $methodNames = ''; $typeNames = '' }
[pscustomobject]@{
    Deployment = if ($deployment) { 'Full' } else { 'Pr' }
    Gui = if ($gui) { 'Full' } else { 'Pr' }
    Benchmarks = if ($benchmarks) { 'Full' } else { 'Pr' }
    DeploymentMethods = $methodNames
    DeploymentTypes = $typeNames
}
