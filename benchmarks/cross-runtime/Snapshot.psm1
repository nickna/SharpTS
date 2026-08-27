Set-StrictMode -Version Latest

$script:InvariantCulture = [Globalization.CultureInfo]::InvariantCulture
$script:RuntimeOrder = @('interpreter', 'compiled', 'node', 'bun')
$script:SupportedUnits = @('milliseconds', 'bytes', 'operationsPerSecond')
$script:SupportedDirections = @('lowerIsBetter', 'higherIsBetter')

function Test-FiniteNumber {
    param([object]$Value)

    if ($null -eq $Value) { return $false }
    try { $number = [double]$Value } catch { return $false }
    return [double]::IsFinite($number)
}

function Get-RequiredProperty {
    param([object]$Value, [string]$Name, [string]$Path)

    if ($null -eq $Value -or $null -eq $Value.PSObject.Properties[$Name]) {
        throw "$Path is missing required property '$Name'."
    }
    return $Value.$Name
}

function ConvertTo-ParameterText {
    param([double]$Value)

    return $Value.ToString('G17', $script:InvariantCulture)
}

function Get-CommandVersion {
    param([string]$Command, [string[]]$Arguments = @())

    $resolved = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolved) { return $null }
    try {
        $output = @(& $resolved.Source @Arguments 2>$null)
        if ($LASTEXITCODE -ne 0 -or $output.Count -eq 0) { return $null }
        return ([string]$output[0]).Trim()
    } catch {
        return $null
    }
}

function Get-CpuIdentity {
    if ($env:PROCESSOR_IDENTIFIER) { return $env:PROCESSOR_IDENTIFIER.Trim() }
    if (Test-Path -LiteralPath '/proc/cpuinfo') {
        $match = Select-String -LiteralPath '/proc/cpuinfo' -Pattern '^model name\s*:\s*(.+)$' |
            Select-Object -First 1
        if ($match) { return $match.Matches[0].Groups[1].Value.Trim() }
    }
    if ($IsMacOS) {
        try {
            $cpu = (& sysctl -n machdep.cpu.brand_string 2>$null | Select-Object -First 1)
            if ($cpu) { return ([string]$cpu).Trim() }
        } catch { }
    }
    return 'unknown'
}

function Get-GitMetadata {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $commit = @(& git -C $RepositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $commit.Count -ne 1 -or $commit[0] -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve the SharpTS revision from '$RepositoryRoot'."
    }
    $status = @(& git -C $RepositoryRoot status --porcelain --untracked-files=no 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect the SharpTS worktree at '$RepositoryRoot'." }
    return [ordered]@{
        commit = [string]$commit[0]
        dirty = $status.Count -gt 0
    }
}

function ConvertFrom-SharpTSRawBenchmarkResults {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$ResultsFile)

    if (-not (Test-Path -LiteralPath $ResultsFile -PathType Leaf)) {
        throw "Results file not found: $ResultsFile"
    }

    $observations = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $ResultsFile) {
        $lineNumber++
        if (-not $line.Trim()) { continue }

        $runtimeAndPayload = $line -split '\|', 2
        if ($runtimeAndPayload.Count -ne 2) {
            throw "Malformed benchmark result at line ${lineNumber}: expected '<runtime>|<payload>'."
        }
        $runtime = $runtimeAndPayload[0]
        if ($runtime -notin $script:RuntimeOrder) {
            throw "Malformed benchmark result at line ${lineNumber}: unsupported runtime '$runtime'."
        }

        $fields = $runtimeAndPayload[1] -split ':'
        if ($fields.Count -ne 10) {
            throw "Malformed benchmark result at line ${lineNumber}: expected 10 payload fields, found $($fields.Count)."
        }
        $name = $fields[0]
        $family = $fields[8]
        if ($name -notmatch '^[a-z0-9][a-z0-9-]*$' -or $family -notmatch '^[a-z0-9][a-z0-9-]*$') {
            throw "Malformed benchmark result at line ${lineNumber}: case name and family must be stable kebab-case identifiers."
        }

        try {
            $parameter = [double]::Parse($fields[1], $script:InvariantCulture)
            $mean = [double]::Parse($fields[2], $script:InvariantCulture)
            $minimum = [double]::Parse($fields[3], $script:InvariantCulture)
            $standardDeviation = [double]::Parse($fields[4], $script:InvariantCulture)
            $sampleCount = [int]::Parse($fields[5], $script:InvariantCulture)
            $innerIterations = [int]::Parse($fields[6], $script:InvariantCulture)
            $sampledDuration = [double]::Parse($fields[7], $script:InvariantCulture)
            $launch = [int]::Parse($fields[9], $script:InvariantCulture)
        } catch {
            throw "Malformed benchmark result at line ${lineNumber}: numeric field could not be parsed. $($_.Exception.Message)"
        }

        foreach ($number in @($parameter, $mean, $minimum, $standardDeviation, $sampledDuration)) {
            if (-not (Test-FiniteNumber $number)) {
                throw "Malformed benchmark result at line ${lineNumber}: numeric values must be finite."
            }
        }
        if ($parameter -lt 0 -or $mean -lt 0 -or $minimum -lt 0 -or
            $standardDeviation -lt 0 -or $sampledDuration -lt 0 -or
            $sampleCount -lt 1 -or $innerIterations -lt 1 -or $launch -lt 1) {
            throw "Malformed benchmark result at line ${lineNumber}: measurements and counters must be non-negative, with positive counts."
        }
        if ($minimum -gt $mean) {
            throw "Malformed benchmark result at line ${lineNumber}: minimum exceeds mean."
        }

        $parameterText = ConvertTo-ParameterText $parameter
        $caseId = '{0}/{1}?n={2}' -f $family, $name, $parameterText
        $observationId = "$caseId|$runtime|$launch"
        if (-not $seen.Add($observationId)) {
            throw "Duplicate benchmark measurement '$observationId' at line $lineNumber."
        }

        $observations.Add([pscustomobject]@{
            caseId = $caseId
            family = $family
            name = $name
            parameter = $parameter
            runtime = $runtime
            launch = $launch
            mean = $mean
            minimum = $minimum
            standardDeviation = $standardDeviation
            sampleCount = $sampleCount
            innerIterations = $innerIterations
            sampledDuration = $sampledDuration
        })
    }

    if ($observations.Count -eq 0) { throw 'Benchmark results contain no measurements.' }
    return $observations.ToArray()
}

function Assert-SharpTSPublicBenchmarkSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)] [object]$Snapshot)

    process {
        $schema = Get-RequiredProperty $Snapshot '$schema' 'snapshot'
        if ($schema -cne 'https://raw.githubusercontent.com/nickna/SharpTS/main/benchmarks/cross-runtime/snapshot-v1.schema.json') {
            throw "Unsupported benchmark snapshot schema '$schema'."
        }
        $schemaVersion = Get-RequiredProperty $Snapshot 'schemaVersion' 'snapshot'
        if ($schemaVersion -ne 1) { throw "Unsupported benchmark snapshot schema version '$schemaVersion'." }

        $run = Get-RequiredProperty $Snapshot 'run' 'snapshot'
        $revision = Get-RequiredProperty $run 'revision' 'snapshot.run'
        $commit = Get-RequiredProperty $revision 'commit' 'snapshot.run.revision'
        if ($commit -notmatch '^[0-9a-f]{40}$') { throw 'snapshot.run.revision.commit must be a 40-character lowercase Git SHA.' }
        [void](Get-RequiredProperty $revision 'dirty' 'snapshot.run.revision')
        $timestamp = Get-RequiredProperty $run 'timestampUtc' 'snapshot.run'
        $parsedTimestamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$timestamp, $script:InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsedTimestamp)) {
            throw 'snapshot.run.timestampUtc must be an ISO-8601 timestamp.'
        }
        $environment = Get-RequiredProperty $run 'environment' 'snapshot.run'
        foreach ($field in @('operatingSystem', 'architecture', 'cpu', 'runner')) {
            $value = Get-RequiredProperty $environment $field 'snapshot.run.environment'
            if ([string]::IsNullOrWhiteSpace([string]$value)) { throw "snapshot.run.environment.$field cannot be empty." }
        }
        $tools = Get-RequiredProperty $run 'tools' 'snapshot.run'
        if ([string]::IsNullOrWhiteSpace([string](Get-RequiredProperty $tools 'dotnet' 'snapshot.run.tools'))) {
            throw 'snapshot.run.tools.dotnet cannot be empty.'
        }
        $runtimeTools = @(Get-RequiredProperty $tools 'runtimes' 'snapshot.run.tools')
        if ($runtimeTools.Count -ne $script:RuntimeOrder.Count) {
            throw 'snapshot.run.tools.runtimes must contain one record for every runtime.'
        }
        for ($runtimeIndex = 0; $runtimeIndex -lt $script:RuntimeOrder.Count; $runtimeIndex++) {
            $tool = $runtimeTools[$runtimeIndex]
            $runtimeId = [string](Get-RequiredProperty $tool 'id' 'snapshot.run.tools.runtimes[]')
            if ($runtimeId -cne $script:RuntimeOrder[$runtimeIndex]) {
                throw 'snapshot.run.tools.runtimes must use deterministic interpreter/compiled/node/bun ordering.'
            }
            $selectedValue = Get-RequiredProperty $tool 'selected' "snapshot.run.tools.runtimes[$runtimeId]"
            $availableValue = Get-RequiredProperty $tool 'available' "snapshot.run.tools.runtimes[$runtimeId]"
            if ($selectedValue -isnot [bool] -or $availableValue -isnot [bool]) {
                throw "snapshot.run.tools.runtimes[$runtimeId] selected and available must be booleans."
            }
            $version = Get-RequiredProperty $tool 'version' "snapshot.run.tools.runtimes[$runtimeId]"
            if ($availableValue -and [string]::IsNullOrWhiteSpace([string]$version)) {
                throw "snapshot.run.tools.runtimes[$runtimeId] is available but has no version."
            }
            if (-not $availableValue -and $null -ne $version) {
                throw "snapshot.run.tools.runtimes[$runtimeId] is unavailable but has a version."
            }
        }
        [void](Get-RequiredProperty $Snapshot 'methodology' 'snapshot')

        $cases = @(Get-RequiredProperty $Snapshot 'cases' 'snapshot')
        if ($cases.Count -eq 0) { throw 'snapshot.cases must contain at least one case.' }
        $caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $previousCaseId = $null
        foreach ($case in $cases) {
            $id = [string](Get-RequiredProperty $case 'id' 'snapshot.cases[]')
            if (-not $caseIds.Add($id)) { throw "Duplicate benchmark case ID '$id'." }
            if ($null -ne $previousCaseId -and [StringComparer]::Ordinal.Compare($previousCaseId, $id) -gt 0) {
                throw 'snapshot.cases must use deterministic ordinal ordering by ID.'
            }
            $previousCaseId = $id

            $family = [string](Get-RequiredProperty $case 'family' "snapshot.cases[$id]")
            $name = [string](Get-RequiredProperty $case 'name' "snapshot.cases[$id]")
            $parameters = Get-RequiredProperty $case 'parameters' "snapshot.cases[$id]"
            $parameter = Get-RequiredProperty $parameters 'n' "snapshot.cases[$id].parameters"
            if (-not (Test-FiniteNumber $parameter) -or [double]$parameter -lt 0) {
                throw "Case '$id' has an invalid parameter."
            }
            $expectedId = '{0}/{1}?n={2}' -f $family, $name, (ConvertTo-ParameterText ([double]$parameter))
            if ($id -cne $expectedId) { throw "Case ID '$id' does not match '$expectedId'." }

            $unit = [string](Get-RequiredProperty $case 'unit' "snapshot.cases[$id]")
            if ($unit -notin $script:SupportedUnits) { throw "Case '$id' has unsupported unit '$unit'." }
            $direction = [string](Get-RequiredProperty $case 'direction' "snapshot.cases[$id]")
            if ($direction -notin $script:SupportedDirections) { throw "Case '$id' has unsupported direction '$direction'." }

            $runtimeRecords = @(Get-RequiredProperty $case 'runtimes' "snapshot.cases[$id]")
            if ($runtimeRecords.Count -ne $script:RuntimeOrder.Count) {
                throw "Case '$id' must contain one explicit record for every runtime."
            }
            for ($runtimeIndex = 0; $runtimeIndex -lt $script:RuntimeOrder.Count; $runtimeIndex++) {
                $record = $runtimeRecords[$runtimeIndex]
                $runtimeId = [string](Get-RequiredProperty $record 'id' "snapshot.cases[$id].runtimes[]")
                if ($runtimeId -cne $script:RuntimeOrder[$runtimeIndex]) {
                    throw "Case '$id' runtime records must use deterministic interpreter/compiled/node/bun ordering."
                }
                $status = [string](Get-RequiredProperty $record 'status' "snapshot.cases[$id].runtimes[$runtimeId]")
                if ($status -eq 'missing') {
                    $reason = [string](Get-RequiredProperty $record 'reason' "snapshot.cases[$id].runtimes[$runtimeId]")
                    if ($reason -notin @('notSelected', 'unavailable', 'noMeasurement')) {
                        throw "Case '$id' runtime '$runtimeId' has invalid missing reason '$reason'."
                    }
                    if ($null -ne $record.PSObject.Properties['measurements']) {
                        throw "Case '$id' runtime '$runtimeId' cannot be missing and contain measurements."
                    }
                    continue
                }
                if ($status -ne 'measured') { throw "Case '$id' runtime '$runtimeId' has invalid status '$status'." }
                $measurements = @(Get-RequiredProperty $record 'measurements' "snapshot.cases[$id].runtimes[$runtimeId]")
                if ($measurements.Count -eq 0) { throw "Case '$id' runtime '$runtimeId' has no measurements." }
                $previousLaunch = 0
                foreach ($measurement in $measurements) {
                    $launch = [int](Get-RequiredProperty $measurement 'launch' "snapshot.cases[$id].runtimes[$runtimeId].measurements[]")
                    if ($launch -le $previousLaunch) { throw "Case '$id' runtime '$runtimeId' has duplicate or unordered launches." }
                    $previousLaunch = $launch
                    foreach ($field in @('mean', 'minimum', 'standardDeviation', 'sampledDuration')) {
                        $number = Get-RequiredProperty $measurement $field "snapshot.cases[$id].runtimes[$runtimeId].measurements[$launch]"
                        if (-not (Test-FiniteNumber $number) -or [double]$number -lt 0) {
                            throw "Case '$id' runtime '$runtimeId' measurement '$field' must be finite and non-negative."
                        }
                    }
                    if ([double]$measurement.minimum -gt [double]$measurement.mean) {
                        throw "Case '$id' runtime '$runtimeId' minimum exceeds mean."
                    }
                    foreach ($field in @('sampleCount', 'innerIterations')) {
                        $count = Get-RequiredProperty $measurement $field "snapshot.cases[$id].runtimes[$runtimeId].measurements[$launch]"
                        if ($count -isnot [byte] -and $count -isnot [int16] -and $count -isnot [int32] -and $count -isnot [int64]) {
                            throw "Case '$id' runtime '$runtimeId' measurement '$field' must be an integer."
                        }
                        if ([long]$count -lt 1) { throw "Case '$id' runtime '$runtimeId' measurement '$field' must be positive." }
                    }
                }
            }
        }
        return $Snapshot
    }
}

function New-SharpTSPublicBenchmarkSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$ResultsFile,
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [string[]]$SelectedRuntimes = @('interpreter', 'compiled', 'node', 'bun'),
        [string]$TimestampUtc,
        [string]$DotNetVersion,
        [string]$NodeVersion,
        [string]$BunVersion,
        [string]$RunnerIdentity,
        [string]$OperatingSystem,
        [string]$Architecture,
        [string]$Cpu,
        [Collections.IDictionary]$Revision
    )

    $selected = @($SelectedRuntimes | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique)
    $unknown = @($selected | Where-Object { $_ -notin $script:RuntimeOrder })
    if ($unknown.Count -gt 0) { throw "Unsupported selected runtime(s): $($unknown -join ', ')." }
    if (-not $TimestampUtc) { $TimestampUtc = [DateTimeOffset]::UtcNow.ToString('o', $script:InvariantCulture) }
    $timestampValue = [DateTimeOffset]::Parse($TimestampUtc, $script:InvariantCulture).ToUniversalTime().ToString('o', $script:InvariantCulture)
    if (-not $PSBoundParameters.ContainsKey('DotNetVersion')) { $DotNetVersion = Get-CommandVersion 'dotnet' @('--version') }
    if (-not $PSBoundParameters.ContainsKey('NodeVersion')) { $NodeVersion = Get-CommandVersion 'node' @('-v') }
    if (-not $PSBoundParameters.ContainsKey('BunVersion')) { $BunVersion = Get-CommandVersion 'bun' @('--version') }
    if (-not $RunnerIdentity) { $RunnerIdentity = if ($env:RUNNER_NAME) { $env:RUNNER_NAME } else { [Environment]::MachineName } }
    if (-not $OperatingSystem) { $OperatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription.Trim() }
    if (-not $Architecture) { $Architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant() }
    if (-not $Cpu) { $Cpu = Get-CpuIdentity }
    if ($null -eq $Revision) { $Revision = Get-GitMetadata $RepositoryRoot }

    $observations = @(ConvertFrom-SharpTSRawBenchmarkResults $ResultsFile)
    $cases = [Collections.Generic.List[object]]::new()
    foreach ($caseGroup in $observations | Group-Object caseId | Sort-Object Name) {
        $first = $caseGroup.Group[0]
        $runtimeRecords = [Collections.Generic.List[object]]::new()
        foreach ($runtime in $script:RuntimeOrder) {
            $runtimeMeasurements = @($caseGroup.Group | Where-Object runtime -ceq $runtime | Sort-Object launch)
            if ($runtimeMeasurements.Count -eq 0) {
                $reason = if ($runtime -notin $selected) {
                    'notSelected'
                } elseif (($runtime -eq 'node' -and -not $NodeVersion) -or ($runtime -eq 'bun' -and -not $BunVersion)) {
                    'unavailable'
                } else {
                    'noMeasurement'
                }
                $runtimeRecords.Add([pscustomobject][ordered]@{
                    id = $runtime
                    status = 'missing'
                    reason = $reason
                })
                continue
            }

            $measurements = @($runtimeMeasurements | ForEach-Object {
                [pscustomobject][ordered]@{
                    launch = [int]$_.launch
                    mean = [double]$_.mean
                    minimum = [double]$_.minimum
                    standardDeviation = [double]$_.standardDeviation
                    sampleCount = [int]$_.sampleCount
                    innerIterations = [int]$_.innerIterations
                    sampledDuration = [double]$_.sampledDuration
                }
            })
            $runtimeRecords.Add([pscustomobject][ordered]@{
                id = $runtime
                status = 'measured'
                measurements = $measurements
            })
        }

        $cases.Add([pscustomobject][ordered]@{
            id = $first.caseId
            family = $first.family
            name = $first.name
            parameters = [pscustomobject][ordered]@{ n = [double]$first.parameter }
            unit = 'milliseconds'
            direction = 'lowerIsBetter'
            runtimes = $runtimeRecords.ToArray()
        })
    }

    $runtimeTools = [Collections.Generic.List[object]]::new()
    foreach ($runtime in $script:RuntimeOrder) {
        $version = $null
        switch ($runtime) {
            'interpreter' { $version = [string]$Revision.commit }
            'compiled' { $version = [string]$Revision.commit }
            'node' { $version = $NodeVersion }
            'bun' { $version = $BunVersion }
        }
        $available = $runtime -in @('interpreter', 'compiled') -or -not [string]::IsNullOrWhiteSpace([string]$version)
        $runtimeTools.Add([pscustomobject][ordered]@{
            id = $runtime
            selected = $runtime -in $selected
            available = $available
            version = if ($available) { [string]$version } else { $null }
        })
    }

    $snapshot = [pscustomobject][ordered]@{
        '$schema' = 'https://raw.githubusercontent.com/nickna/SharpTS/main/benchmarks/cross-runtime/snapshot-v1.schema.json'
        schemaVersion = 1
        run = [pscustomobject][ordered]@{
            timestampUtc = $timestampValue
            revision = [pscustomobject][ordered]@{
                commit = [string]$Revision.commit
                dirty = [bool]$Revision.dirty
            }
            environment = [pscustomobject][ordered]@{
                operatingSystem = $OperatingSystem
                architecture = $Architecture
                cpu = $Cpu
                runner = $RunnerIdentity
            }
            tools = [pscustomobject][ordered]@{
                dotnet = $DotNetVersion
                runtimes = $runtimeTools.ToArray()
            }
        }
        methodology = [pscustomobject][ordered]@{
            harnessVersion = 2
            id = 'performance-now-confirmed-probe-auto-batched-v2'
            timingScope = 'inProcessWorkload'
            clock = 'performance.now'
            includes = @('one workload function invocation')
            excludes = @('process startup', 'script loading', 'SharpTS compilation', 'warmup', 'batch calibration')
            sampling = [pscustomobject][ordered]@{
                warmupCapMilliseconds = 100
                minimumSampleDurationMilliseconds = 1
                targetDurationMilliseconds = 300
                minimumSamples = 8
                hardCapMilliseconds = 2000
                maximumSamples = 100000
            }
        }
        cases = $cases.ToArray()
    }

    return Assert-SharpTSPublicBenchmarkSnapshot $snapshot
}

function Export-SharpTSPublicBenchmarkSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$ResultsFile,
        [Parameter(Mandatory)] [string]$OutputFile,
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [string[]]$SelectedRuntimes = @('interpreter', 'compiled', 'node', 'bun'),
        [string]$TimestampUtc,
        [string]$DotNetVersion,
        [string]$NodeVersion,
        [string]$BunVersion,
        [string]$RunnerIdentity,
        [string]$OperatingSystem,
        [string]$Architecture,
        [string]$Cpu,
        [Collections.IDictionary]$Revision
    )

    $arguments = @{
        ResultsFile = $ResultsFile
        RepositoryRoot = $RepositoryRoot
        SelectedRuntimes = $SelectedRuntimes
    }
    foreach ($name in @('TimestampUtc', 'DotNetVersion', 'NodeVersion', 'BunVersion', 'RunnerIdentity', 'OperatingSystem', 'Architecture', 'Cpu', 'Revision')) {
        if ($PSBoundParameters.ContainsKey($name)) { $arguments[$name] = $PSBoundParameters[$name] }
    }
    $snapshot = New-SharpTSPublicBenchmarkSnapshot @arguments
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputFile))
    if ($parent) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    $json = $snapshot | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputFile), $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    return $snapshot
}

function Test-SharpTSPublicBenchmarkSnapshotFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Snapshot file not found: $Path" }
    $raw = Get-Content -LiteralPath $Path -Raw
    $schemaPath = Join-Path $PSScriptRoot 'snapshot-v1.schema.json'
    try {
        if (-not ($raw | Test-Json -SchemaFile $schemaPath -ErrorAction Stop)) {
            throw 'JSON Schema validation returned false.'
        }
        $snapshot = $raw | ConvertFrom-Json
    } catch {
        throw "Snapshot is not valid schema-v1 JSON: $($_.Exception.Message)"
    }
    [void](Assert-SharpTSPublicBenchmarkSnapshot $snapshot)
    return $true
}

Export-ModuleMember -Function @(
    'Assert-SharpTSPublicBenchmarkSnapshot',
    'ConvertFrom-SharpTSRawBenchmarkResults',
    'Export-SharpTSPublicBenchmarkSnapshot',
    'New-SharpTSPublicBenchmarkSnapshot',
    'Test-SharpTSPublicBenchmarkSnapshotFile'
)
