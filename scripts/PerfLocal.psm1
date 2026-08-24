Set-StrictMode -Version Latest

$script:InvariantCulture = [Globalization.CultureInfo]::InvariantCulture

function Get-SharpTSMedian {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [double[]]$Values)

    if ($Values.Count -eq 0) {
        return [double]::NaN
    }

    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) {
        return [double]$sorted[$middle]
    }

    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function ConvertFrom-SharpTSBenchmarkResults {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Text,
        [Parameter(Mandatory)] [string]$Platform,
        [Parameter(Mandatory)] [ValidateSet('baseline', 'candidate')] [string]$Variant,
        [Parameter(Mandatory)] [ValidateRange(1, 100)] [int]$Launch
    )

    foreach ($line in $Text -split "`r?`n") {
        if (-not $line.Trim()) { continue }
        $runtimeAndPayload = $line -split '\|', 2
        if ($runtimeAndPayload.Count -ne 2) {
            throw "Malformed benchmark result line: $line"
        }

        $fields = $runtimeAndPayload[1] -split ':'
        if ($fields.Count -ne 5) {
            throw "Malformed benchmark payload: $line"
        }

        $runtime = $runtimeAndPayload[0]
        $label = if ($runtime -eq 'node') { 'node' } elseif ($runtime -eq 'compiled') { $Variant } else { $runtime }
        [pscustomobject]@{
            platform = $Platform
            variant = $Variant
            label = $label
            runtime = $runtime
            launch = $Launch
            benchmark = $fields[0]
            parameter = [double]::Parse($fields[1], $script:InvariantCulture)
            meanMilliseconds = [double]::Parse($fields[2], $script:InvariantCulture)
            minimumMilliseconds = [double]::Parse($fields[3], $script:InvariantCulture)
            standardDeviationMilliseconds = [double]::Parse($fields[4], $script:InvariantCulture)
        }
    }
}

function Get-SharpTSPerfComparisons {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]]$Samples,
        [ValidateRange(0, 1000)] [double]$RegressionThresholdPercent = 10,
        [ValidateRange(0, 1000)] [double]$RegressionMinimumMilliseconds = 0.05
    )

    $groups = $Samples | Group-Object platform, benchmark, parameter
    foreach ($group in $groups | Sort-Object Name) {
        $baseline = @($group.Group | Where-Object label -eq 'baseline')
        $candidate = @($group.Group | Where-Object label -eq 'candidate')
        $node = @($group.Group | Where-Object label -eq 'node')
        if ($baseline.Count -eq 0 -or $candidate.Count -eq 0) {
            throw "Missing baseline or candidate samples for $($group.Name)"
        }

        $baselineMedian = Get-SharpTSMedian @($baseline.meanMilliseconds)
        $candidateMedian = Get-SharpTSMedian @($candidate.meanMilliseconds)
        $nodeMedian = if ($node.Count -gt 0) { Get-SharpTSMedian @($node.meanMilliseconds) } else { [double]::NaN }
        $deltaMilliseconds = $candidateMedian - $baselineMedian
        $changePercent = if ($baselineMedian -eq 0) {
            [double]::NaN
        } else {
            (($candidateMedian / $baselineMedian) - 1.0) * 100.0
        }
        $material = [Math]::Abs($deltaMilliseconds) -ge $RegressionMinimumMilliseconds
        $status = if ($material -and $changePercent -gt $RegressionThresholdPercent) {
            'regression'
        } elseif ($material -and $changePercent -lt -$RegressionThresholdPercent) {
            'improvement'
        } else {
            'neutral'
        }

        $first = $group.Group[0]
        [pscustomobject]@{
            platform = $first.platform
            benchmark = $first.benchmark
            parameter = [double]$first.parameter
            baselineMedianMilliseconds = [Math]::Round($baselineMedian, 6)
            candidateMedianMilliseconds = [Math]::Round($candidateMedian, 6)
            deltaMilliseconds = [Math]::Round($deltaMilliseconds, 6)
            changePercent = [Math]::Round($changePercent, 2)
            nodeMedianMilliseconds = if ([double]::IsNaN($nodeMedian)) { $null } else { [Math]::Round($nodeMedian, 6) }
            candidateVsNode = if ([double]::IsNaN($nodeMedian) -or $nodeMedian -eq 0) {
                $null
            } else {
                [Math]::Round($candidateMedian / $nodeMedian, 3)
            }
            baselineSamples = $baseline.Count
            candidateSamples = $candidate.Count
            nodeSamples = $node.Count
            status = $status
        }
    }
}

function ConvertTo-SharpTSPerfMarkdown {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]]$Comparisons,
        [Parameter(Mandatory)] [Collections.IDictionary]$Metadata
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# SharpTS paired performance comparison')
    $lines.Add('')
    $lines.Add("Baseline: ``$($Metadata.baselineCommit)``  ")
    $lines.Add("Candidate: ``$($Metadata.candidateCommit)``  ")
    $lines.Add("Runs per variant/platform: $($Metadata.runs)")
    $lines.Add('')
    $lines.Add('| Platform | Benchmark | Input | Baseline | Candidate | Change | Node | Candidate/Node | Status |')
    $lines.Add('|---|---|---:|---:|---:|---:|---:|---:|---|')

    foreach ($row in $Comparisons | Sort-Object platform, benchmark, parameter) {
        $parameter = $row.parameter.ToString('G17', $script:InvariantCulture)
        $baseline = $row.baselineMedianMilliseconds.ToString('F4', $script:InvariantCulture)
        $candidate = $row.candidateMedianMilliseconds.ToString('F4', $script:InvariantCulture)
        $change = $row.changePercent.ToString('+0.00;-0.00;0.00', $script:InvariantCulture)
        $node = if ($null -eq $row.nodeMedianMilliseconds) {
            '-'
        } else {
            $row.nodeMedianMilliseconds.ToString('F4', $script:InvariantCulture)
        }
        $candidateVsNode = if ($null -eq $row.candidateVsNode) {
            '-'
        } else {
            $row.candidateVsNode.ToString('F3', $script:InvariantCulture) + 'x'
        }
        $lines.Add("| $($row.platform) | $($row.benchmark) | $parameter | $baseline ms | $candidate ms | $change% | $node ms | $candidateVsNode | $($row.status) |")
    }

    return $lines
}

Export-ModuleMember -Function @(
    'Get-SharpTSMedian',
    'ConvertFrom-SharpTSBenchmarkResults',
    'Get-SharpTSPerfComparisons',
    'ConvertTo-SharpTSPerfMarkdown'
)
