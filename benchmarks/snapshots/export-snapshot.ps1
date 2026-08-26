[CmdletBinding(DefaultParameterSetName = 'CrossRuntime')]
param(
    [Parameter(Mandatory)] [string]$OutputFile,
    [Parameter(Mandatory, ParameterSetName = 'CrossRuntime')] [string]$CrossRuntimeSnapshot,
    [Parameter(Mandatory, ParameterSetName = 'CompilerMicro')] [switch]$CompilerMicro,
    [Parameter(Mandatory, ParameterSetName = 'GuiBenchmark')] [switch]$GuiBenchmark,
    [Parameter(Mandatory, ParameterSetName = 'GuiPackaging')] [string]$GuiPackagingEvidence,
    [Parameter(Mandatory, ParameterSetName = 'CompilerMicro')]
    [Parameter(Mandatory, ParameterSetName = 'GuiBenchmark')]
    [string[]]$ReportPath,
    [Parameter(Mandatory, ParameterSetName = 'CompilerMicro')]
    [Parameter(Mandatory, ParameterSetName = 'GuiBenchmark')]
    [string[]]$MetadataPath,
    [Parameter(Mandatory, ParameterSetName = 'GuiBenchmark')] [string]$BudgetPath,
    [Parameter(ParameterSetName = 'CompilerMicro')]
    [Parameter(ParameterSetName = 'GuiBenchmark')]
    [string]$RepositoryRoot,
    [string]$TimestampUtc,
    [string]$GeneratedAtUtc,
    [string]$RunnerIdentity
)

$ErrorActionPreference = 'Stop'
$snapshotDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
Import-Module (Join-Path $snapshotDirectory 'PublicSnapshot.psm1') -Force
if (-not $RepositoryRoot) {
    $RepositoryRoot = (Resolve-Path (Join-Path $snapshotDirectory '../..')).Path
}

switch ($PSCmdlet.ParameterSetName) {
    'CrossRuntime' {
        $run = New-CrossRuntimeRun -SnapshotPath $CrossRuntimeSnapshot
    }
    'CompilerMicro' {
        $arguments = @{
            ReportPath = $ReportPath
            MetadataPath = $MetadataPath
            RepositoryRoot = $RepositoryRoot
        }
        if ($TimestampUtc) { $arguments.TimestampUtc = $TimestampUtc }
        if ($RunnerIdentity) { $arguments.RunnerIdentity = $RunnerIdentity }
        $run = New-CompilerMicroRun @arguments
    }
    'GuiBenchmark' {
        $arguments = @{
            ReportPath = $ReportPath
            MetadataPath = $MetadataPath
            BudgetPath = $BudgetPath
            RepositoryRoot = $RepositoryRoot
        }
        if ($TimestampUtc) { $arguments.TimestampUtc = $TimestampUtc }
        if ($RunnerIdentity) { $arguments.RunnerIdentity = $RunnerIdentity }
        $run = New-GuiBenchmarkRun @arguments
    }
    'GuiPackaging' {
        $run = New-GuiPackagingRun -EvidencePath $GuiPackagingEvidence
    }
}

$arguments = @{ Runs = @($run); OutputFile = $OutputFile }
if ($GeneratedAtUtc) { $arguments.GeneratedAtUtc = $GeneratedAtUtc }
[void](Export-SharpTSPublicPerformanceSnapshot @arguments)
Write-Host "Wrote schema-v2 $($run.suite) performance snapshot to $([IO.Path]::GetFullPath($OutputFile))"
