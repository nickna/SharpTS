[CmdletBinding()]
param(
    [string]$DifftasticPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scopeScript = Join-Path $PSScriptRoot 'get-ci-change-scope.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-ci-scope-tests-' + [Guid]::NewGuid().ToString('N'))

function Invoke-Git([string[]]$Arguments) {
    & git @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
}

function Write-Fixture([string]$RelativePath, [string]$Content) {
    $path = Join-Path $fixtureRoot $RelativePath
    $parent = Split-Path -Parent $path
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($path, $Content.Replace("`n", [Environment]::NewLine))
}

function Save-Fixture([string]$Message) {
    Invoke-Git @('add', '-A')
    Invoke-Git @('commit', '--quiet', '-m', $Message)
    $sha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read fixture commit.' }
    return $sha
}

function Get-Scope([string]$Base, [string]$Head, [string]$Tool = $DifftasticPath) {
    $arguments = @{
        BaseSha = $Base
        HeadSha = $Head
    }
    if ($Tool) { $arguments.DifftasticPath = $Tool }
    return & $scopeScript @arguments
}

function Assert-Mode([string]$Expected, [object]$Actual, [string]$Case) {
    if ($Actual.Mode -cne $Expected) {
        throw "$Case expected '$Expected', got '$($Actual.Mode)': $($Actual.Reason)"
    }
}

function Assert-NativeExitCode([int]$Expected, [int]$Actual, [string]$Case) {
    if ($Actual -ne $Expected) {
        throw "$Case expected native exit code $Expected, got $Actual."
    }
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Push-Location $fixtureRoot
    try {
        Invoke-Git @('init', '--quiet')
        Invoke-Git @('config', 'user.name', 'SharpTS CI scope tests')
        Invoke-Git @('config', 'user.email', 'ci-scope@example.invalid')

        Write-Fixture 'src/Sample.cs' @'
namespace Fixture;

internal static class Sample
{
    // Original explanation.
    internal static string Value => "// this is a string";
}
'@
        Write-Fixture 'README.md' "# Fixture`n"
        $initial = Save-Fixture 'initial'

        Write-Fixture 'src/Sample.cs' @'
namespace Fixture;

internal static class Sample
{
    /// <summary>A better explanation.</summary>
    internal static string Value => "// this is a string";
}
'@
        $comments = Save-Fixture 'comments only'
        Assert-Mode 'csharp-trivia-only' (Get-Scope $initial $comments) 'comment-only change'

        Write-Fixture 'README.md' "# Fixture`n`nUpdated documentation.`n"
        $commentsAndDocs = Save-Fixture 'comments and docs'
        Assert-Mode 'docs-only' (Get-Scope $comments $commentsAndDocs) 'documentation-only change'
        Assert-Mode 'csharp-trivia-only' (Get-Scope $initial $commentsAndDocs) 'mixed comments and documentation'

        Write-Fixture 'src/Sample.cs' @'
namespace Fixture;

internal static class Sample
{
    /// <summary>A better explanation.</summary>
    internal static string Value => "/* changed string content */";
}
'@
        $stringChange = Save-Fixture 'string token change'
        $stringChangeScope = Get-Scope $commentsAndDocs $stringChange
        Assert-Mode 'full' $stringChangeScope 'comment marker inside string'
        Assert-NativeExitCode 0 $LASTEXITCODE 'syntax-change classification'

        Write-Fixture 'src/Sample.cs' @'
#nullable disable
namespace Fixture;

internal static class Sample
{
    /// <summary>A better explanation.</summary>
    internal static string Value => "/* changed string content */";
}
'@
        $directiveChange = Save-Fixture 'directive change'
        $directiveChangeScope = Get-Scope $stringChange $directiveChange
        Assert-Mode 'full' $directiveChangeScope 'preprocessor directive change'
        Assert-NativeExitCode 0 $LASTEXITCODE 'directive-change classification'

        Write-Fixture 'src/Added.cs' "namespace Fixture;`ninternal sealed class Added;`n"
        $addedFile = Save-Fixture 'add C# file'
        Assert-Mode 'full' (Get-Scope $directiveChange $addedFile) 'added C# file'

        Invoke-Git @('mv', 'src/Added.cs', 'src/Renamed.cs')
        $renamedFile = Save-Fixture 'rename C# file'
        Assert-Mode 'full' (Get-Scope $addedFile $renamedFile) 'renamed C# file'

        New-Item -ItemType Directory -Path 'docs' -Force | Out-Null
        Invoke-Git @('mv', 'src/Renamed.cs', 'docs/Renamed.cs')
        $renamedIntoDocs = Save-Fixture 'rename C# file into docs'
        Assert-Mode 'full' (Get-Scope $renamedFile $renamedIntoDocs) 'source file renamed into documentation'

        $missingTool = Join-Path $fixtureRoot 'missing-difftastic'
        $missingToolScope = Get-Scope $initial $comments $missingTool
        Assert-Mode 'full' $missingToolScope 'missing Difftastic fallback'
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixture = (Resolve-Path -LiteralPath $fixtureRoot).Path
        $resolvedTemp = (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).Path
        if (-not $resolvedFixture.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a fixture outside the temporary directory: $resolvedFixture"
        }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}

Write-Host 'CI change-scope tests passed.'
