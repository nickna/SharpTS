Set-StrictMode -Version Latest

$script:InvariantCulture = [Globalization.CultureInfo]::InvariantCulture
$script:SchemaUri = 'https://raw.githubusercontent.com/nickna/SharpTS/main/benchmarks/snapshots/snapshot-v2.schema.json'
$script:SuiteOrder = @{ 'cross-runtime' = 0; 'compiler-micro' = 1; 'gui' = 2 }
$script:MeasurementOrder = @(
    'mean', 'throughput', 'allocated', 'gen0Collections', 'gen1Collections',
    'gen2Collections', 'coldStartup', 'peakWorkingSet', 'executableSize', 'shippingSize'
)
$script:MeasurementContract = @{
    mean = @('nanoseconds', 'lowerIsBetter')
    throughput = @('operationsPerSecond', 'higherIsBetter')
    allocated = @('bytes', 'lowerIsBetter')
    gen0Collections = @('collectionsPer1000Operations', 'lowerIsBetter')
    gen1Collections = @('collectionsPer1000Operations', 'lowerIsBetter')
    gen2Collections = @('collectionsPer1000Operations', 'lowerIsBetter')
    coldStartup = @('nanoseconds', 'lowerIsBetter')
    peakWorkingSet = @('bytes', 'lowerIsBetter')
    executableSize = @('bytes', 'lowerIsBetter')
    shippingSize = @('bytes', 'lowerIsBetter')
}

function Get-RequiredProperty {
    param([object]$Value, [string]$Name, [string]$Path)

    if ($null -eq $Value -or $null -eq $Value.PSObject.Properties[$Name]) {
        throw "$Path is missing required property '$Name'."
    }
    return $Value.$Name
}

function Test-FiniteNumber {
    param([object]$Value)

    if ($null -eq $Value) { return $false }
    try { $number = [double]$Value } catch { return $false }
    return [double]::IsFinite($number)
}

function ConvertTo-UtcTimestamp {
    param([object]$Value)

    if (-not $Value) {
        return [DateTimeOffset]::UtcNow.ToString('o', $script:InvariantCulture)
    }
    try {
        if ($Value -is [DateTimeOffset]) {
            return $Value.ToUniversalTime().ToString('o', $script:InvariantCulture)
        }
        if ($Value -is [DateTime]) {
            return ([DateTimeOffset]$Value).ToUniversalTime().ToString('o', $script:InvariantCulture)
        }
        return [DateTimeOffset]::Parse($Value, $script:InvariantCulture).ToUniversalTime().ToString('o', $script:InvariantCulture)
    } catch {
        throw "Invalid timestamp '$Value'."
    }
}

function ConvertTo-StableToken {
    param([Parameter(Mandatory)] [string]$Value)

    $token = [Text.RegularExpressions.Regex]::Replace($Value.Trim(), '([A-Z]+)([A-Z][a-z])', '$1-$2')
    $token = [Text.RegularExpressions.Regex]::Replace($token, '([a-z0-9])([A-Z])', '$1-$2')
    $token = [Text.RegularExpressions.Regex]::Replace($token, '[^A-Za-z0-9]+', '-')
    $token = $token.Trim('-').ToLowerInvariant()
    if (-not $token -or $token -notmatch '^[a-z0-9][a-z0-9-]*$') {
        throw "'$Value' cannot be converted to a stable identifier token."
    }
    return $token
}

function ConvertTo-StableParameterText {
    param([object]$Value)

    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) { return $Value.ToString().ToLowerInvariant() }
    if ($Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or
        $Value -is [uint16] -or $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]) {
        return ([IFormattable]$Value).ToString($null, $script:InvariantCulture)
    }
    if ($Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
        return ([IFormattable]$Value).ToString('G17', $script:InvariantCulture)
    }
    return [string]$Value
}

function New-StableBenchmarkId {
    param([string]$Family, [string]$Method, [object[]]$Parameters)

    $id = "$Family/$Method"
    if ($Parameters.Count -gt 0) {
        $query = @($Parameters | ForEach-Object {
            $name = [string](Get-RequiredProperty $_ 'name' 'parameter')
            $text = ConvertTo-StableParameterText (Get-RequiredProperty $_ 'value' "parameter[$name]")
            '{0}={1}' -f $name, [Uri]::EscapeDataString($text)
        }) -join '&'
        $id += "?$query"
    }
    return $id
}

function Get-GitRevision {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $commit = @(& git -C $RepositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $commit.Count -ne 1 -or $commit[0] -notmatch '^[0-9a-f]{40}$') {
        throw "Could not resolve the SharpTS revision from '$RepositoryRoot'."
    }
    $status = @(& git -C $RepositoryRoot status --porcelain --untracked-files=no 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect the SharpTS worktree at '$RepositoryRoot'." }
    return [ordered]@{ commit = [string]$commit[0]; dirty = $status.Count -gt 0 }
}

function Get-RunnerIdentity {
    if ($env:RUNNER_NAME) { return $env:RUNNER_NAME.Trim() }
    return [Environment]::MachineName
}

function Read-JsonFile {
    param([Parameter(Mandatory)] [string]$Path, [string]$Description = 'JSON file')

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "$Description not found: $Path" }
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { throw "$Description is not valid JSON: $Path. $($_.Exception.Message)" }
}

function New-Measurement {
    param(
        [Parameter(Mandatory)] [string]$Id,
        [Parameter(Mandatory)] [string]$Unit,
        [Parameter(Mandatory)] [string]$Direction,
        [object]$Actual,
        [string]$MissingReason,
        [object]$Budget
    )

    $record = [ordered]@{ id = $Id; unit = $Unit; direction = $Direction }
    if ($PSBoundParameters.ContainsKey('Actual') -and $null -ne $Actual) {
        if (-not (Test-FiniteNumber $Actual) -or [double]$Actual -lt 0) {
            throw "Measurement '$Id' must be finite and non-negative."
        }
        $record.status = 'measured'
        $record.actual = [double]$Actual
    } else {
        $record.status = 'missing'
        $record.reason = if ($MissingReason) { $MissingReason } else { 'notAvailable' }
    }
    if ($null -ne $Budget) { $record.budget = $Budget }
    return [pscustomobject]$record
}

function Get-BenchmarkDotNetMetricValue {
    param([object]$Benchmark, [string]$Id)

    foreach ($metric in @(Get-RequiredProperty $Benchmark 'Metrics' 'BenchmarkDotNet benchmark')) {
        $descriptor = Get-RequiredProperty $metric 'Descriptor' 'BenchmarkDotNet metric'
        if ([string](Get-RequiredProperty $descriptor 'Id' 'BenchmarkDotNet metric descriptor') -ceq $Id) {
            return Get-RequiredProperty $metric 'Value' "BenchmarkDotNet metric '$Id'"
        }
    }
    return $null
}

function New-BenchmarkDotNetMeasurements {
    param([object]$Benchmark, [Collections.IDictionary]$Budgets = @{})

    $statistics = $Benchmark.PSObject.Properties['Statistics']?.Value
    $hasStatistics = $null -ne $statistics -and $null -ne $statistics.PSObject.Properties['N'] -and [int]$statistics.N -gt 0
    $mean = if ($hasStatistics) { $statistics.Mean } else { $null }
    $memory = $Benchmark.PSObject.Properties['Memory']?.Value
    $allocated = if ($null -ne $memory -and $null -ne $memory.PSObject.Properties['BytesAllocatedPerOperation']) {
        $memory.BytesAllocatedPerOperation
    } else { $null }

    $measurements = [Collections.Generic.List[object]]::new()
    $measurements.Add((New-Measurement -Id mean -Unit nanoseconds -Direction lowerIsBetter `
        -Actual $mean -MissingReason noMeasurement -Budget $Budgets['mean']))
    $throughput = if ($null -ne $mean -and [double]$mean -gt 0) { 1000000000.0 / [double]$mean } else { $null }
    $measurements.Add((New-Measurement -Id throughput -Unit operationsPerSecond -Direction higherIsBetter `
        -Actual $throughput -MissingReason noMeasurement -Budget $Budgets['throughput']))
    $measurements.Add((New-Measurement -Id allocated -Unit bytes -Direction lowerIsBetter `
        -Actual $allocated -MissingReason notAvailable -Budget $Budgets['allocated']))

    foreach ($generation in @(0, 1, 2)) {
        $metricId = "Gen${generation}Collects"
        $value = Get-BenchmarkDotNetMetricValue $Benchmark $metricId
        if ($null -eq $value -and $null -ne $memory -and
            $null -ne $memory.PSObject.Properties["Gen${generation}Collections"] -and
            $null -ne $memory.PSObject.Properties['TotalOperations'] -and [long]$memory.TotalOperations -gt 0) {
            $value = [double]$memory."Gen${generation}Collections" * 1000.0 / [double]$memory.TotalOperations
        }
        $measurements.Add((New-Measurement -Id "gen${generation}Collections" `
            -Unit collectionsPer1000Operations -Direction lowerIsBetter `
            -Actual $value -MissingReason notAvailable -Budget $Budgets["gen${generation}Collections"]))
    }
    return $measurements.ToArray()
}

function New-BenchmarkDotNetStatistics {
    param([object]$Benchmark)

    $statistics = $Benchmark.PSObject.Properties['Statistics']?.Value
    if ($null -eq $statistics -or $null -eq $statistics.PSObject.Properties['N'] -or [int]$statistics.N -lt 1) {
        return [pscustomobject][ordered]@{ status = 'missing'; reason = 'noMeasurement' }
    }
    $originalValues = @(Get-RequiredProperty $statistics 'OriginalValues' 'BenchmarkDotNet statistics')
    if ($originalValues.Count -eq 0) { throw 'BenchmarkDotNet statistics contain no original values.' }
    return [pscustomobject][ordered]@{
        status = 'measured'
        sampleCount = [int](Get-RequiredProperty $statistics 'N' 'BenchmarkDotNet statistics')
        meanNanoseconds = [double](Get-RequiredProperty $statistics 'Mean' 'BenchmarkDotNet statistics')
        minimumNanoseconds = [double](Get-RequiredProperty $statistics 'Min' 'BenchmarkDotNet statistics')
        maximumNanoseconds = [double](Get-RequiredProperty $statistics 'Max' 'BenchmarkDotNet statistics')
        standardDeviationNanoseconds = [double](Get-RequiredProperty $statistics 'StandardDeviation' 'BenchmarkDotNet statistics')
        originalValuesNanoseconds = @($originalValues | ForEach-Object { [double]$_ })
    }
}

function Read-BenchmarkDotNetSources {
    param([Parameter(Mandatory)] [string[]]$ReportPath, [Parameter(Mandatory)] [string[]]$MetadataPath)

    if ($ReportPath.Count -eq 0) { throw 'At least one BenchmarkDotNet JSON report is required.' }
    if ($MetadataPath.Count -eq 0) { throw 'At least one SharpTS BenchmarkDotNet metadata file is required.' }

    $metadataIndex = @{}
    foreach ($path in $MetadataPath) {
        $metadata = Read-JsonFile $path 'BenchmarkDotNet metadata file'
        if ([string](Get-RequiredProperty $metadata 'sourceFormat' 'BenchmarkDotNet metadata') -cne
            'sharpts-benchmarkdotnet-metadata-v1') {
            throw "Unsupported BenchmarkDotNet metadata source format in '$path'."
        }
        $title = [string](Get-RequiredProperty $metadata 'title' 'BenchmarkDotNet metadata')
        if ($metadataIndex.ContainsKey($title)) { throw "Duplicate BenchmarkDotNet metadata title '$title'." }
        $metadataIndex[$title] = @(Get-RequiredProperty $metadata 'benchmarks' 'BenchmarkDotNet metadata')
    }

    $entries = [Collections.Generic.List[object]]::new()
    $host = $null
    $hostJson = $null
    foreach ($path in $ReportPath) {
        $report = Read-JsonFile $path 'BenchmarkDotNet JSON report'
        $title = [string](Get-RequiredProperty $report 'Title' 'BenchmarkDotNet report')
        if (-not $metadataIndex.ContainsKey($title)) {
            throw "BenchmarkDotNet report '$title' has no matching structured metadata."
        }
        $currentHost = Get-RequiredProperty $report 'HostEnvironmentInfo' 'BenchmarkDotNet report'
        $currentHostJson = $currentHost | ConvertTo-Json -Depth 10 -Compress
        if ($null -eq $host) { $host = $currentHost; $hostJson = $currentHostJson }
        elseif ($currentHostJson -cne $hostJson) {
            throw 'BenchmarkDotNet reports from unlike host/runtime environments cannot be combined into one run.'
        }

        $benchmarks = @(Get-RequiredProperty $report 'Benchmarks' 'BenchmarkDotNet report')
        $metadataBenchmarks = @($metadataIndex[$title])
        if ($benchmarks.Count -ne $metadataBenchmarks.Count) {
            throw "BenchmarkDotNet report '$title' contains $($benchmarks.Count) results but its structured metadata contains $($metadataBenchmarks.Count)."
        }
        for ($index = 0; $index -lt $benchmarks.Count; $index++) {
            $benchmark = $benchmarks[$index]
            $benchmarkMetadata = $metadataBenchmarks[$index]
            [void](Get-RequiredProperty $benchmarkMetadata 'fullName' 'BenchmarkDotNet metadata benchmark')
            $rawType = [string](Get-RequiredProperty $benchmark 'Type' 'BenchmarkDotNet benchmark')
            $rawMethod = [string](Get-RequiredProperty $benchmark 'Method' 'BenchmarkDotNet benchmark')
            $metadataType = [string](Get-RequiredProperty $benchmarkMetadata 'type' 'BenchmarkDotNet metadata benchmark')
            $metadataMethod = [string](Get-RequiredProperty $benchmarkMetadata 'method' 'BenchmarkDotNet metadata benchmark')
            if ($rawType -cne $metadataType -or $rawMethod -cne $metadataMethod) {
                throw "BenchmarkDotNet report '$title' result $index ($rawType.$rawMethod) does not match its structured metadata ($metadataType.$metadataMethod)."
            }
            $entries.Add([pscustomobject]@{ Raw = $benchmark; Metadata = $benchmarkMetadata })
        }
        $metadataIndex.Remove($title)
    }
    if ($metadataIndex.Count -gt 0) {
        $first = [string]($metadataIndex.Keys | Sort-Object | Select-Object -First 1)
        throw "BenchmarkDotNet metadata '$first' has no matching JSON result."
    }
    if ($entries.Count -eq 0) { throw 'BenchmarkDotNet reports contain no benchmark records.' }
    return [pscustomobject]@{ Host = $host; Entries = $entries.ToArray() }
}

function ConvertTo-NormalizedParameters {
    param([object]$Metadata)

    $parameters = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($parameter in @(Get-RequiredProperty $Metadata 'parameters' 'BenchmarkDotNet metadata benchmark')) {
        $name = ConvertTo-StableToken ([string](Get-RequiredProperty $parameter 'name' 'BenchmarkDotNet parameter'))
        if (-not $seen.Add($name)) { throw "Duplicate normalized BenchmarkDotNet parameter '$name'." }
        $parameters.Add([pscustomobject][ordered]@{
            name = $name
            value = Get-RequiredProperty $parameter 'value' "BenchmarkDotNet parameter '$name'"
        })
    }
    return @($parameters.ToArray() | Sort-Object name)
}

function Get-CompilerImplementation {
    param([string]$Type, [string]$Method)

    if ($Type -match 'Interpreter') { return 'sharpTsInterpreter' }
    if ($Method -match '^(Equivalent|BoxedEquivalent)') { return 'equivalentCSharp' }
    if ($Method -match '^(Idiomatic|NativeCSharp|CSharp|Bcl)') { return 'idiomaticCSharp' }
    if ($Method -match '^SharpTS') { return 'sharpTsCompiled' }
    if ($Type -match '^(JsonParserSubphase|ParseIntDecimal)') { return 'componentProbe' }
    return 'sharpTsCompiled'
}

function New-RunMetadataFromBenchmarkDotNet {
    param(
        [object]$Host,
        [string]$RepositoryRoot,
        [string]$TimestampUtc,
        [string]$RunnerIdentity,
        [Collections.IDictionary]$Revision
    )

    if ($null -eq $Revision) { $Revision = Get-GitRevision $RepositoryRoot }
    if (-not $RunnerIdentity) { $RunnerIdentity = Get-RunnerIdentity }
    $frequency = Get-RequiredProperty (Get-RequiredProperty $Host 'ChronometerFrequency' 'BenchmarkDotNet host') 'Hertz' 'BenchmarkDotNet host chronometer'
    $normalizedHost = [pscustomobject][ordered]@{
        benchmarkDotNetCaption = [string](Get-RequiredProperty $Host 'BenchmarkDotNetCaption' 'BenchmarkDotNet host')
        benchmarkDotNetVersion = [string](Get-RequiredProperty $Host 'BenchmarkDotNetVersion' 'BenchmarkDotNet host')
        operatingSystem = [string](Get-RequiredProperty $Host 'OsVersion' 'BenchmarkDotNet host')
        processorName = [string](Get-RequiredProperty $Host 'ProcessorName' 'BenchmarkDotNet host')
        physicalProcessorCount = $Host.PhysicalProcessorCount
        physicalCoreCount = $Host.PhysicalCoreCount
        logicalCoreCount = $Host.LogicalCoreCount
        runtimeVersion = [string](Get-RequiredProperty $Host 'RuntimeVersion' 'BenchmarkDotNet host')
        architecture = [string](Get-RequiredProperty $Host 'Architecture' 'BenchmarkDotNet host')
        hasAttachedDebugger = [bool](Get-RequiredProperty $Host 'HasAttachedDebugger' 'BenchmarkDotNet host')
        hasRyuJit = [bool](Get-RequiredProperty $Host 'HasRyuJit' 'BenchmarkDotNet host')
        configuration = [string](Get-RequiredProperty $Host 'Configuration' 'BenchmarkDotNet host')
        dotNetCliVersion = [string](Get-RequiredProperty $Host 'DotNetCliVersion' 'BenchmarkDotNet host')
        chronometerFrequencyHertz = [double]$frequency
        hardwareTimerKind = [string](Get-RequiredProperty $Host 'HardwareTimerKind' 'BenchmarkDotNet host')
    }
    return [pscustomobject][ordered]@{
        timestampUtc = ConvertTo-UtcTimestamp $TimestampUtc
        revision = [pscustomobject][ordered]@{ commit = [string]$Revision.commit; dirty = [bool]$Revision.dirty }
        environment = [pscustomobject][ordered]@{
            operatingSystem = $normalizedHost.operatingSystem
            architecture = $normalizedHost.architecture.ToLowerInvariant()
            processor = $normalizedHost.processorName
            runner = $RunnerIdentity
            benchmarkDotNetHost = $normalizedHost
        }
        tools = [pscustomobject][ordered]@{
            dotnet = $normalizedHost.dotNetCliVersion
            benchmarkDotNet = $normalizedHost.benchmarkDotNetVersion
            runtime = $normalizedHost.runtimeVersion
        }
    }
}

function New-CompilerMicroRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]]$ReportPath,
        [Parameter(Mandatory)] [string[]]$MetadataPath,
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [string]$TimestampUtc,
        [string]$RunnerIdentity,
        [Collections.IDictionary]$Revision
    )

    $sources = Read-BenchmarkDotNetSources $ReportPath $MetadataPath
    $cases = [Collections.Generic.List[object]]::new()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $sources.Entries) {
        $metadata = $entry.Metadata
        $type = [string](Get-RequiredProperty $metadata 'type' 'BenchmarkDotNet metadata benchmark')
        $methodName = [string](Get-RequiredProperty $metadata 'method' 'BenchmarkDotNet metadata benchmark')
        $familySource = if ($type.EndsWith('Benchmarks', [StringComparison]::Ordinal)) { $type.Substring(0, $type.Length - 10) } else { $type }
        $family = ConvertTo-StableToken $familySource
        $method = ConvertTo-StableToken $methodName
        $parameters = @(ConvertTo-NormalizedParameters $metadata)
        $id = New-StableBenchmarkId $family $method $parameters
        if (-not $seen.Add($id)) { throw "Duplicate compiler benchmark stable identity '$id'." }
        $categories = @(@(Get-RequiredProperty $metadata 'categories' 'BenchmarkDotNet metadata benchmark') |
            ForEach-Object { ConvertTo-StableToken ([string]$_) } | Sort-Object -Unique)
        $operationsPerInvoke = [int](Get-RequiredProperty $metadata 'operationsPerInvoke' 'BenchmarkDotNet metadata benchmark')
        if ($operationsPerInvoke -lt 1) { throw "Benchmark '$id' has invalid operationsPerInvoke '$operationsPerInvoke'." }
        $cases.Add([pscustomobject][ordered]@{
            id = $id
            family = $family
            method = $method
            categories = $categories
            parameters = $parameters
            implementation = Get-CompilerImplementation $type $methodName
            operationsPerInvoke = $operationsPerInvoke
            displayInfo = [string](Get-RequiredProperty $entry.Raw 'DisplayInfo' 'BenchmarkDotNet benchmark')
            statistics = New-BenchmarkDotNetStatistics $entry.Raw
            measurements = New-BenchmarkDotNetMeasurements $entry.Raw
        })
    }
    return [pscustomobject][ordered]@{
        suite = 'compiler-micro'
        source = 'benchmarkDotNet'
        run = New-RunMetadataFromBenchmarkDotNet $sources.Host $RepositoryRoot $TimestampUtc $RunnerIdentity $Revision
        methodology = [pscustomobject][ordered]@{
            id = 'benchmarkdotnet-managed-in-process-v1'
            sourceFormat = 'benchmarkdotnet-json-with-sharpts-metadata-v1'
            timingScope = 'inProcessBenchmarkMethod'
            units = [pscustomobject][ordered]@{
                duration = 'nanoseconds'; allocation = 'bytes'
                throughput = 'operationsPerSecond'; gc = 'collectionsPer1000Operations'
            }
        }
        cases = @($cases.ToArray() | Sort-Object id)
    }
}

function Get-GuiBenchmarkDefinitions {
    return @(
        [pscustomobject]@{ SourceMethod = 'DirectAvaloniaInitialMount'; Family = 'initial-mount'; Method = 'direct-avalonia'; Implementation = 'directAvalonia'; Operations = 1; BudgetId = $null },
        [pscustomobject]@{ SourceMethod = 'CompiledXamlShapeBaseline'; Family = 'initial-mount'; Method = 'compiled-xaml'; Implementation = 'compiledXaml'; Operations = 1; BudgetId = $null },
        [pscustomobject]@{ SourceMethod = 'SharpTsInitialMount'; Family = 'initial-mount'; Method = 'sharp-ts'; Implementation = 'sharpTsGui'; Operations = 1; BudgetId = 'SharpTsInitialMount' },
        [pscustomobject]@{ SourceMethod = 'ScalarUpdate'; Family = 'scalar-update'; Method = 'sharp-ts'; Implementation = 'sharpTsGui'; Operations = 1; BudgetId = 'ScalarUpdate' },
        [pscustomobject]@{ SourceMethod = 'BatchedScalarUpdates'; Family = 'batched-scalar-updates'; Method = 'sharp-ts'; Implementation = 'sharpTsGui'; Operations = 10; BudgetId = 'BatchedScalarUpdates' },
        [pscustomobject]@{ SourceMethod = 'KeyedInsertMoveRemove'; Family = 'keyed-insert-move-remove'; Method = 'sharp-ts'; Implementation = 'sharpTsGui'; Operations = 1; BudgetId = 'KeyedInsertMoveRemove' },
        [pscustomobject]@{ SourceMethod = 'InputToRenderLatency'; Family = 'input-to-render-latency'; Method = 'sharp-ts'; Implementation = 'sharpTsGui'; Operations = 1; BudgetId = 'InputToRenderLatency' }
    )
}

function Read-GuiBudgets {
    param([Parameter(Mandatory)] [string]$BudgetPath)

    $budgets = Read-JsonFile $BudgetPath 'GUI performance budget'
    if ([int](Get-RequiredProperty $budgets 'schemaVersion' 'GUI performance budget') -ne 1) {
        throw "Unsupported GUI performance budget schema version '$($budgets.schemaVersion)'."
    }
    $definitions = @(Get-GuiBenchmarkDefinitions)
    $known = @($definitions | Where-Object BudgetId | ForEach-Object BudgetId)
    $benchmarkBudgets = Get-RequiredProperty $budgets 'benchmarks' 'GUI performance budget'
    foreach ($property in $benchmarkBudgets.PSObject.Properties) {
        if ($property.Name -notin $known) { throw "Unknown GUI benchmark budget '$($property.Name)'." }
    }
    foreach ($budgetId in $known) {
        if ($null -eq $benchmarkBudgets.PSObject.Properties[$budgetId]) {
            throw "GUI performance budget is missing '$budgetId'."
        }
    }
    [void](Get-RequiredProperty $budgets 'nativeAot' 'GUI performance budget')
    return $budgets
}

function New-GuiBenchmarkRun {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string[]]$ReportPath,
        [Parameter(Mandatory)] [string[]]$MetadataPath,
        [Parameter(Mandatory)] [string]$BudgetPath,
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [string]$TimestampUtc,
        [string]$RunnerIdentity,
        [Collections.IDictionary]$Revision
    )

    $sources = Read-BenchmarkDotNetSources $ReportPath $MetadataPath
    $budgets = Read-GuiBudgets $BudgetPath
    $definitions = @(Get-GuiBenchmarkDefinitions)
    $entryByMethod = @{}
    foreach ($entry in $sources.Entries) {
        $type = [string](Get-RequiredProperty $entry.Metadata 'type' 'GUI BenchmarkDotNet metadata')
        $method = [string](Get-RequiredProperty $entry.Metadata 'method' 'GUI BenchmarkDotNet metadata')
        if ($type -cne 'GuiRendererBenchmarks' -or $method -notin @($definitions.SourceMethod)) {
            throw "Unknown GUI benchmark method '$type.$method'."
        }
        if ($entryByMethod.ContainsKey($method)) { throw "Duplicate GUI benchmark method '$method'." }
        $entryByMethod[$method] = $entry
    }

    $cases = [Collections.Generic.List[object]]::new()
    foreach ($definition in $definitions) {
        $budgetMap = @{}
        if ($definition.BudgetId) {
            $budget = $budgets.benchmarks.($definition.BudgetId)
            $budgetMap.mean = [pscustomobject][ordered]@{
                limit = [double](Get-RequiredProperty $budget 'maxMeanNanoseconds' "GUI budget '$($definition.BudgetId)'")
                sourceId = "benchmarks.$($definition.BudgetId).maxMeanNanoseconds"
            }
            $budgetMap.allocated = [pscustomobject][ordered]@{
                limit = [double](Get-RequiredProperty $budget 'maxAllocatedBytes' "GUI budget '$($definition.BudgetId)'")
                sourceId = "benchmarks.$($definition.BudgetId).maxAllocatedBytes"
            }
        }

        $entry = $entryByMethod[$definition.SourceMethod]
        if ($null -eq $entry) {
            $measurements = @(
                New-Measurement -Id mean -Unit nanoseconds -Direction lowerIsBetter -MissingReason noMeasurement -Budget $budgetMap['mean']
                New-Measurement -Id throughput -Unit operationsPerSecond -Direction higherIsBetter -MissingReason noMeasurement
                New-Measurement -Id allocated -Unit bytes -Direction lowerIsBetter -MissingReason noMeasurement -Budget $budgetMap['allocated']
                New-Measurement -Id gen0Collections -Unit collectionsPer1000Operations -Direction lowerIsBetter -MissingReason noMeasurement
                New-Measurement -Id gen1Collections -Unit collectionsPer1000Operations -Direction lowerIsBetter -MissingReason noMeasurement
                New-Measurement -Id gen2Collections -Unit collectionsPer1000Operations -Direction lowerIsBetter -MissingReason noMeasurement
            )
            $statistics = [pscustomobject][ordered]@{ status = 'missing'; reason = 'noMeasurement' }
            $operationsPerInvoke = [int]$definition.Operations
            $displayInfo = "GuiRendererBenchmarks.$($definition.SourceMethod)"
        } else {
            $operationsPerInvoke = [int](Get-RequiredProperty $entry.Metadata 'operationsPerInvoke' 'GUI BenchmarkDotNet metadata')
            if ($operationsPerInvoke -ne [int]$definition.Operations) {
                throw "GUI benchmark '$($definition.SourceMethod)' reports operationsPerInvoke=$operationsPerInvoke; expected $($definition.Operations)."
            }
            if (@(Get-RequiredProperty $entry.Metadata 'parameters' 'GUI BenchmarkDotNet metadata').Count -ne 0) {
                throw "GUI benchmark '$($definition.SourceMethod)' unexpectedly has parameters."
            }
            $measurements = New-BenchmarkDotNetMeasurements $entry.Raw $budgetMap
            $statistics = New-BenchmarkDotNetStatistics $entry.Raw
            $displayInfo = [string](Get-RequiredProperty $entry.Raw 'DisplayInfo' 'GUI BenchmarkDotNet benchmark')
        }

        $cases.Add([pscustomobject][ordered]@{
            id = "$($definition.Family)/$($definition.Method)"
            family = $definition.Family
            method = $definition.Method
            categories = @()
            parameters = @()
            implementation = $definition.Implementation
            operationsPerInvoke = $operationsPerInvoke
            displayInfo = $displayInfo
            statistics = $statistics
            measurements = $measurements
        })
    }
    return [pscustomobject][ordered]@{
        suite = 'gui'
        source = 'benchmarkDotNet'
        run = New-RunMetadataFromBenchmarkDotNet $sources.Host $RepositoryRoot $TimestampUtc $RunnerIdentity $Revision
        methodology = [pscustomobject][ordered]@{
            id = 'benchmarkdotnet-avalonia-headless-v1'
            sourceFormat = 'benchmarkdotnet-json-with-sharpts-metadata-v1'
            timingScope = 'inProcessHeadlessRenderOperation'
            units = [pscustomobject][ordered]@{
                duration = 'nanoseconds'; allocation = 'bytes'
                throughput = 'operationsPerSecond'; gc = 'collectionsPer1000Operations'
            }
            budgetContract = [pscustomobject][ordered]@{
                path = 'benchmarks/micro/SharpTS.Gui.Benchmarks/PerformanceBudgets.json'
                schemaVersion = 1
            }
        }
        cases = @($cases.ToArray() | Sort-Object id)
    }
}

function New-GuiPackagingRun {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$EvidencePath)

    $evidence = Read-JsonFile $EvidencePath 'GUI packaging evidence'
    if ([string](Get-RequiredProperty $evidence 'sourceFormat' 'GUI packaging evidence') -cne 'sharpts-gui-packaging-v1') {
        throw 'Unsupported GUI packaging evidence source format.'
    }
    $definitions = @(
        [pscustomobject]@{ SourceId = 'coldStartup'; Family = 'native-aot'; Method = 'cold-start'; Metric = 'coldStartup'; InputUnit = 'milliseconds'; Unit = 'nanoseconds'; Multiplier = 1000000.0 },
        [pscustomobject]@{ SourceId = 'peakWorkingSet'; Family = 'native-aot'; Method = 'peak-working-set'; Metric = 'peakWorkingSet'; InputUnit = 'bytes'; Unit = 'bytes'; Multiplier = 1.0 },
        [pscustomobject]@{ SourceId = 'executableSize'; Family = 'native-aot'; Method = 'executable-size'; Metric = 'executableSize'; InputUnit = 'bytes'; Unit = 'bytes'; Multiplier = 1.0 },
        [pscustomobject]@{ SourceId = 'shippingSize'; Family = 'native-aot'; Method = 'shipping-size'; Metric = 'shippingSize'; InputUnit = 'bytes'; Unit = 'bytes'; Multiplier = 1.0 }
    )
    $measurements = @(Get-RequiredProperty $evidence 'measurements' 'GUI packaging evidence')
    foreach ($measurement in $measurements) {
        $id = [string](Get-RequiredProperty $measurement 'id' 'GUI packaging measurement')
        if ($id -notin @($definitions.SourceId)) { throw "Unknown GUI packaging measurement '$id'." }
    }
    $cases = [Collections.Generic.List[object]]::new()
    foreach ($definition in $definitions) {
        $matches = @($measurements | Where-Object id -CEQ $definition.SourceId)
        if ($matches.Count -ne 1) { throw "GUI packaging evidence must contain exactly one '$($definition.SourceId)' measurement." }
        $source = $matches[0]
        $unit = [string](Get-RequiredProperty $source 'unit' "GUI packaging measurement '$($definition.SourceId)'")
        if ($unit -cne $definition.InputUnit) {
            throw "GUI packaging measurement '$($definition.SourceId)' uses '$unit'; expected '$($definition.InputUnit)'."
        }
        $budgetSource = Get-RequiredProperty $source 'budget' "GUI packaging measurement '$($definition.SourceId)'"
        $budget = [pscustomobject][ordered]@{
            limit = [double](Get-RequiredProperty $budgetSource 'limit' "GUI packaging measurement '$($definition.SourceId)' budget") * $definition.Multiplier
            sourceId = [string](Get-RequiredProperty $budgetSource 'sourceId' "GUI packaging measurement '$($definition.SourceId)' budget")
        }
        $status = [string](Get-RequiredProperty $source 'status' "GUI packaging measurement '$($definition.SourceId)'")
        if ($status -ceq 'measured') {
            $actual = [double](Get-RequiredProperty $source 'actual' "GUI packaging measurement '$($definition.SourceId)'") * $definition.Multiplier
            $normalized = New-Measurement -Id $definition.Metric -Unit $definition.Unit -Direction lowerIsBetter -Actual $actual -Budget $budget
        } elseif ($status -ceq 'missing') {
            $reason = [string](Get-RequiredProperty $source 'reason' "GUI packaging measurement '$($definition.SourceId)'")
            $normalized = New-Measurement -Id $definition.Metric -Unit $definition.Unit -Direction lowerIsBetter -MissingReason $reason -Budget $budget
        } else {
            throw "GUI packaging measurement '$($definition.SourceId)' has invalid status '$status'."
        }
        $cases.Add([pscustomobject][ordered]@{
            id = "$($definition.Family)/$($definition.Method)"
            family = $definition.Family
            method = $definition.Method
            categories = @('product')
            parameters = @()
            implementation = 'sharpTsGui'
            operationsPerInvoke = 1
            displayInfo = "Native AOT $($definition.Method)"
            measurements = @($normalized)
        })
    }
    return [pscustomobject][ordered]@{
        suite = 'gui'
        source = 'nativeAotPackaging'
        run = Get-RequiredProperty $evidence 'run' 'GUI packaging evidence'
        methodology = [pscustomobject][ordered]@{
            id = 'native-aot-packaging-v1'
            sourceFormat = 'sharpts-gui-packaging-v1'
            timingScope = 'publishedNativeAotProduct'
            units = [pscustomobject][ordered]@{
                duration = 'nanoseconds'; allocation = 'bytes'
                throughput = 'operationsPerSecond'; gc = 'collectionsPer1000Operations'
            }
            budgetContract = [pscustomobject][ordered]@{
                path = 'benchmarks/micro/SharpTS.Gui.Benchmarks/PerformanceBudgets.json'
                schemaVersion = 1
            }
        }
        cases = @($cases.ToArray() | Sort-Object id)
    }
}

function New-CrossRuntimeRun {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$SnapshotPath)

    $snapshot = Read-JsonFile $SnapshotPath 'Cross-runtime snapshot'
    $crossRuntimeModule = Join-Path $PSScriptRoot '../cross-runtime/Snapshot.psm1'
    Import-Module $crossRuntimeModule -Force
    [void](Assert-SharpTSPublicBenchmarkSnapshot $snapshot)
    return [pscustomobject][ordered]@{
        suite = 'cross-runtime'
        source = 'snapshot-v1'
        snapshot = $snapshot
    }
}

function Get-PublicRunSortKey {
    param([object]$Run)

    $suite = [string](Get-RequiredProperty $Run 'suite' 'run')
    $suiteIndex = if ($script:SuiteOrder.ContainsKey($suite)) { $script:SuiteOrder[$suite] } else { 99 }
    $source = [string](Get-RequiredProperty $Run 'source' 'run')
    $timestamp = if ($suite -ceq 'cross-runtime') {
        ConvertTo-UtcTimestamp $Run.snapshot.run.timestampUtc
    } else {
        ConvertTo-UtcTimestamp $Run.run.timestampUtc
    }
    return '{0:D2}|{1}|{2}' -f $suiteIndex, $timestamp, $source
}

function Assert-SharpTSPublicPerformanceSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory, ValueFromPipeline)] [object]$Snapshot)

    process {
        if ([string](Get-RequiredProperty $Snapshot '$schema' 'snapshot') -cne $script:SchemaUri) {
            throw 'Unsupported public performance snapshot schema.'
        }
        if ([int](Get-RequiredProperty $Snapshot 'schemaVersion' 'snapshot') -ne 2) {
            throw 'Unsupported public performance snapshot schema version.'
        }
        [void](ConvertTo-UtcTimestamp (Get-RequiredProperty $Snapshot 'generatedAtUtc' 'snapshot'))
        $runs = @(Get-RequiredProperty $Snapshot 'runs' 'snapshot')
        if ($runs.Count -eq 0) { throw 'snapshot.runs must contain at least one run.' }
        $previousSortKey = $null
        foreach ($run in $runs) {
            $sortKey = Get-PublicRunSortKey $run
            if ($null -ne $previousSortKey -and [StringComparer]::Ordinal.Compare($previousSortKey, $sortKey) -gt 0) {
                throw "snapshot.runs must use deterministic suite/timestamp/source ordering ('$previousSortKey' before '$sortKey')."
            }
            $previousSortKey = $sortKey
            $suite = [string](Get-RequiredProperty $run 'suite' 'snapshot.runs[]')
            if ($suite -ceq 'cross-runtime') {
                $crossRuntimeModule = Join-Path $PSScriptRoot '../cross-runtime/Snapshot.psm1'
                Import-Module $crossRuntimeModule -Force
                [void](Assert-SharpTSPublicBenchmarkSnapshot (Get-RequiredProperty $run 'snapshot' 'cross-runtime run'))
                continue
            }
            if ($suite -notin @('compiler-micro', 'gui')) { throw "Unknown benchmark suite '$suite'." }
            $source = [string](Get-RequiredProperty $run 'source' "run[$suite]")
            if ($suite -ceq 'compiler-micro' -and $source -cne 'benchmarkDotNet') {
                throw 'compiler-micro runs must come from BenchmarkDotNet.'
            }
            $runMetadata = Get-RequiredProperty $run 'run' "run[$suite]"
            [void](ConvertTo-UtcTimestamp (Get-RequiredProperty $runMetadata 'timestampUtc' "run[$suite].run"))
            $revision = Get-RequiredProperty $runMetadata 'revision' "run[$suite].run"
            if ([string]$revision.commit -notmatch '^[0-9a-f]{40}$' -or $revision.dirty -isnot [bool]) {
                throw "run[$suite] has invalid revision provenance."
            }
            $environment = Get-RequiredProperty $runMetadata 'environment' "run[$suite].run"
            foreach ($field in @('operatingSystem', 'architecture', 'processor', 'runner')) {
                if ([string]::IsNullOrWhiteSpace([string](Get-RequiredProperty $environment $field "run[$suite].run.environment"))) {
                    throw "run[$suite].run.environment.$field cannot be empty."
                }
            }
            if ($source -ceq 'benchmarkDotNet' -and $null -eq $environment.PSObject.Properties['benchmarkDotNetHost']) {
                throw "BenchmarkDotNet run[$suite] is missing complete host metadata."
            }
            $caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $previousCaseId = $null
            foreach ($case in @(Get-RequiredProperty $run 'cases' "run[$suite]")) {
                $caseId = [string](Get-RequiredProperty $case 'id' "run[$suite].cases[]")
                if (-not $caseIds.Add($caseId)) { throw "Duplicate benchmark case ID '$caseId'." }
                if ($null -ne $previousCaseId -and [StringComparer]::Ordinal.Compare($previousCaseId, $caseId) -gt 0) {
                    throw "run[$suite].cases must use deterministic ID ordering."
                }
                $previousCaseId = $caseId
                if ([int](Get-RequiredProperty $case 'operationsPerInvoke' "case[$caseId]") -lt 1) {
                    throw "Case '$caseId' has invalid operationsPerInvoke."
                }
                $measurementIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $previousMeasurementIndex = -1
                foreach ($measurement in @(Get-RequiredProperty $case 'measurements' "case[$caseId]")) {
                    $measurementId = [string](Get-RequiredProperty $measurement 'id' "case[$caseId].measurement")
                    if (-not $measurementIds.Add($measurementId)) { throw "Case '$caseId' has duplicate measurement '$measurementId'." }
                    if (-not $script:MeasurementContract.ContainsKey($measurementId)) {
                        throw "Case '$caseId' has unknown measurement '$measurementId'."
                    }
                    $measurementIndex = [Array]::IndexOf($script:MeasurementOrder, $measurementId)
                    if ($measurementIndex -lt $previousMeasurementIndex) {
                        throw "Case '$caseId' measurements must use deterministic ordering."
                    }
                    $previousMeasurementIndex = $measurementIndex
                    $contract = $script:MeasurementContract[$measurementId]
                    $unit = [string](Get-RequiredProperty $measurement 'unit' "case[$caseId].measurement[$measurementId]")
                    $direction = [string](Get-RequiredProperty $measurement 'direction' "case[$caseId].measurement[$measurementId]")
                    if ($unit -cne $contract[0] -or $direction -cne $contract[1]) {
                        throw "Case '$caseId' measurement '$measurementId' must use unit '$($contract[0])' and direction '$($contract[1])'."
                    }
                    $status = [string](Get-RequiredProperty $measurement 'status' "case[$caseId].measurement[$measurementId]")
                    if ($status -ceq 'measured') {
                        $actual = Get-RequiredProperty $measurement 'actual' "case[$caseId].measurement[$measurementId]"
                        if (-not (Test-FiniteNumber $actual) -or [double]$actual -lt 0) {
                            throw "Case '$caseId' measurement '$measurementId' actual must be finite and non-negative."
                        }
                    } elseif ($status -ceq 'missing') {
                        [void](Get-RequiredProperty $measurement 'reason' "case[$caseId].measurement[$measurementId]")
                        if ($null -ne $measurement.PSObject.Properties['actual']) {
                            throw "Case '$caseId' measurement '$measurementId' cannot be missing and have an actual value."
                        }
                    } else { throw "Case '$caseId' measurement '$measurementId' has invalid status '$status'." }
                    if ($null -ne $measurement.PSObject.Properties['budget']) {
                        $limit = Get-RequiredProperty $measurement.budget 'limit' "case[$caseId].measurement[$measurementId].budget"
                        if (-not (Test-FiniteNumber $limit) -or [double]$limit -lt 0) {
                            throw "Case '$caseId' measurement '$measurementId' budget must be finite and non-negative."
                        }
                    }
                }
            }
        }
        return $Snapshot
    }
}

function New-SharpTSPublicPerformanceSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object[]]$Runs, [string]$GeneratedAtUtc)

    if ($Runs.Count -eq 0) { throw 'At least one performance run is required.' }
    $snapshot = [pscustomobject][ordered]@{
        '$schema' = $script:SchemaUri
        schemaVersion = 2
        generatedAtUtc = ConvertTo-UtcTimestamp $GeneratedAtUtc
        runs = @($Runs | Sort-Object { Get-PublicRunSortKey $_ })
    }
    return Assert-SharpTSPublicPerformanceSnapshot $snapshot
}

function Export-SharpTSPublicPerformanceSnapshot {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object[]]$Runs, [Parameter(Mandatory)] [string]$OutputFile, [string]$GeneratedAtUtc)

    $snapshot = New-SharpTSPublicPerformanceSnapshot $Runs $GeneratedAtUtc
    $fullPath = [IO.Path]::GetFullPath($OutputFile)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $fullPath)) | Out-Null
    [IO.File]::WriteAllText($fullPath, ($snapshot | ConvertTo-Json -Depth 20) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    return $snapshot
}

function Test-SharpTSPublicPerformanceSnapshotFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Snapshot file not found: $Path" }
    $raw = Get-Content -LiteralPath $Path -Raw
    $schemaPath = Join-Path $PSScriptRoot 'snapshot-v2.schema.json'
    try {
        if (-not ($raw | Test-Json -SchemaFile $schemaPath -ErrorAction Stop)) {
            throw 'JSON Schema validation returned false.'
        }
        $snapshot = $raw | ConvertFrom-Json
    } catch {
        throw "Snapshot is not valid schema-v2 JSON: $($_.Exception.Message)"
    }
    [void](Assert-SharpTSPublicPerformanceSnapshot $snapshot)
    return $true
}

Export-ModuleMember -Function @(
    'Assert-SharpTSPublicPerformanceSnapshot',
    'Export-SharpTSPublicPerformanceSnapshot',
    'New-CompilerMicroRun',
    'New-CrossRuntimeRun',
    'New-GuiBenchmarkRun',
    'New-GuiPackagingRun',
    'New-SharpTSPublicPerformanceSnapshot',
    'Test-SharpTSPublicPerformanceSnapshotFile'
)
