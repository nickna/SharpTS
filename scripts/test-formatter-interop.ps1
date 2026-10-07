[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'artifacts/formatter-interop',
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$formatter = Join-Path $repositoryRoot 'tools/formatter-interop/node_modules/prettier/bin/prettier.cjs'
if (-not (Test-Path -LiteralPath $formatter)) {
    throw 'Install the optional pinned formatter first: npm ci --prefix tools/formatter-interop --ignore-scripts'
}
$results = [IO.Path]::GetFullPath($ResultsDirectory, $repositoryRoot)
$runDirectory = Join-Path $results ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        foreach ($project in @('tests/fixtures/FormatterInterop/SharpTS.FormatterInteropEvidence.csproj',
            'src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj')) {
            & dotnet build $project -c $Configuration --nologo --verbosity minimal
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
        }
    }
    & node (Join-Path $PSScriptRoot 'test-formatter-interop.mjs') $Configuration $runDirectory
    if ($LASTEXITCODE -ne 0) { throw "Formatter interoperability failed. Artifacts: $runDirectory" }
    $summary = Get-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Raw | ConvertFrom-Json
    if (-not $summary.completed -or $summary.fixtures.Count -eq 0 -or $summary.protocol.Count -ne 2) {
        throw 'Formatter smoke did not execute the required nonempty fixture and protocol checks.'
    }
}
catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $runDirectory 'failure.txt')
    throw
}
finally {
    Pop-Location
}
