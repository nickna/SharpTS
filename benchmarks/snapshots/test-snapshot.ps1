[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$snapshotDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repositoryRoot = (Resolve-Path (Join-Path $snapshotDirectory '../..')).Path
Import-Module (Join-Path $snapshotDirectory 'PublicSnapshot.psm1') -Force

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw "Expected an error matching '$Pattern', but no error was thrown." }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "Expected error matching '$Pattern', got: $($caught.Exception.Message)"
    }
}

function Write-FixtureJson([string]$Path, [object]$Value) {
    [IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
}

function New-Host([string]$Processor = 'Fixture CPU') {
    return [ordered]@{
        BenchmarkDotNetCaption = 'BenchmarkDotNet'
        BenchmarkDotNetVersion = '0.14.0'
        OsVersion = 'Fixture OS 1.0'
        ProcessorName = $Processor
        PhysicalProcessorCount = 1
        PhysicalCoreCount = 4
        LogicalCoreCount = 8
        RuntimeVersion = '.NET 10.0.0, X64 RyuJIT'
        Architecture = 'X64'
        HasAttachedDebugger = $false
        HasRyuJit = $true
        Configuration = 'RELEASE'
        DotNetCliVersion = '10.0.100'
        ChronometerFrequency = [ordered]@{ Hertz = 10000000 }
        HardwareTimerKind = 'FixtureTimer'
    }
}

function New-RawBenchmark([string]$Type, [string]$Method, [string]$FullName, [double]$Mean, [long]$Allocated) {
    return [ordered]@{
        DisplayInfo = "$Type.${Method}: FixtureJob"
        Namespace = 'SharpTS.Microbenchmarks.Benchmarks'
        Type = $Type
        Method = $Method
        MethodTitle = $Method
        Parameters = if ($FullName -match '\(N: 10\)$') { 'N=10' } else { '' }
        FullName = $FullName
        HardwareIntrinsics = 'FixtureIntrinsics'
        Statistics = [ordered]@{
            OriginalValues = @(($Mean - 0.0000000000001), ($Mean + 0.0000000000001))
            N = 2
            Min = $Mean - 0.0000000000001
            Mean = $Mean
            Max = $Mean + 0.0000000000001
            StandardDeviation = 0.0000000000001414213562373095
        }
        Memory = [ordered]@{
            Gen0Collections = 2
            Gen1Collections = 1
            Gen2Collections = 0
            TotalOperations = 2000
            BytesAllocatedPerOperation = $Allocated
        }
        Measurements = @()
        Metrics = @(
            [ordered]@{ Value = 1.0; Descriptor = [ordered]@{ Id = 'Gen0Collects' } },
            [ordered]@{ Value = 0.5; Descriptor = [ordered]@{ Id = 'Gen1Collects' } },
            [ordered]@{ Value = 0.0; Descriptor = [ordered]@{ Id = 'Gen2Collects' } },
            [ordered]@{ Value = $Allocated; Descriptor = [ordered]@{ Id = 'Allocated Memory' } }
        )
    }
}

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "sharpts-public-snapshot-tests-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
try {
    $revision = [ordered]@{ commit = '1111111111111111111111111111111111111111'; dirty = $false }
    $fixed = @{
        RepositoryRoot = $repositoryRoot
        TimestampUtc = '2026-08-26T12:00:00Z'
        RunnerIdentity = 'fixture-runner'
        Revision = $revision
    }

    $compilerTitle = 'CompilerFixture-20260826-120000'
    $compilerBenchmarks = @(
        New-RawBenchmark 'FibonacciBenchmarks' 'SharpTS' 'SharpTS.Microbenchmarks.Benchmarks.FibonacciBenchmarks.SharpTS(N: 10)' 12.3456789012345 2048
        New-RawBenchmark 'FibonacciBenchmarks' 'Equivalent' 'SharpTS.Microbenchmarks.Benchmarks.FibonacciBenchmarks.Equivalent(N: 10)' 20.25 4096
        New-RawBenchmark 'FibonacciBenchmarks' 'Idiomatic' 'SharpTS.Microbenchmarks.Benchmarks.FibonacciBenchmarks.Idiomatic(N: 10)' 8.125 64
    )
    $compilerReportPath = Join-Path $temporaryDirectory 'compiler-report.json'
    Write-FixtureJson $compilerReportPath ([ordered]@{
        Title = $compilerTitle; HostEnvironmentInfo = (New-Host); Benchmarks = $compilerBenchmarks
    })
    $compilerMetadataPath = Join-Path $temporaryDirectory 'compiler-metadata.json'
    Write-FixtureJson $compilerMetadataPath ([ordered]@{
        sourceFormat = 'sharpts-benchmarkdotnet-metadata-v1'
        title = $compilerTitle
        benchmarks = @(
            # Presentation names can differ between BenchmarkDotNet exporters; they are not identities.
            [ordered]@{ fullName = 'FibonacciBenchmarks.SharpTS(N: "10")'; type = 'FibonacciBenchmarks'; method = 'SharpTS'; categories = @('Algorithm'); operationsPerInvoke = 1; parameters = @([ordered]@{ name = 'N'; value = 10 }) },
            [ordered]@{ fullName = $compilerBenchmarks[1].FullName; type = 'FibonacciBenchmarks'; method = 'Equivalent'; categories = @('Algorithm'); operationsPerInvoke = 1; parameters = @([ordered]@{ name = 'N'; value = 10 }) },
            [ordered]@{ fullName = $compilerBenchmarks[2].FullName; type = 'FibonacciBenchmarks'; method = 'Idiomatic'; categories = @('Algorithm'); operationsPerInvoke = 1; parameters = @([ordered]@{ name = 'N'; value = 10 }) }
        )
    })
    $compilerRun = New-CompilerMicroRun -ReportPath $compilerReportPath -MetadataPath $compilerMetadataPath @fixed
    Assert-True ($compilerRun.cases.Count -eq 3) 'Compiler exporter did not retain all variants.'
    $compiledCase = $compilerRun.cases | Where-Object id -CEQ 'fibonacci/sharp-ts?n=10'
    Assert-True ($null -ne $compiledCase) 'Compiler case did not receive a stable parameterized ID.'
    Assert-True ($compiledCase.implementation -ceq 'sharpTsCompiled') 'SharpTS compiler variant was not classified.'
    Assert-True ($compiledCase.categories[0] -ceq 'algorithm') 'Benchmark categories were not normalized.'
    Assert-True ($compiledCase.statistics.meanNanoseconds -eq 12.3456789012345) 'Mean precision was not retained.'
    Assert-True (($compiledCase.measurements | Where-Object id -CEQ allocated).actual -eq 2048) 'Allocation was not exported in bytes.'
    Assert-True (($compiledCase.measurements | Where-Object id -CEQ gen0Collections).actual -eq 1.0) 'GC statistics were not exported.'
    Assert-True (($compilerRun.cases | Where-Object method -CEQ equivalent).implementation -ceq 'equivalentCSharp') 'Equivalent C# variant was not classified.'
    Assert-True (($compilerRun.cases | Where-Object method -CEQ idiomatic).implementation -ceq 'idiomaticCSharp') 'Idiomatic C# variant was not classified.'

    $guiTitle = 'GuiFixture-20260826-120000'
    $guiBenchmark = New-RawBenchmark 'GuiRendererBenchmarks' 'BatchedScalarUpdates' 'SharpTS.Gui.Benchmarks.GuiRendererBenchmarks.BatchedScalarUpdates' 90000.125 32000
    $guiReportPath = Join-Path $temporaryDirectory 'gui-report.json'
    Write-FixtureJson $guiReportPath ([ordered]@{
        Title = $guiTitle; HostEnvironmentInfo = (New-Host); Benchmarks = @($guiBenchmark)
    })
    $guiMetadataPath = Join-Path $temporaryDirectory 'gui-metadata.json'
    Write-FixtureJson $guiMetadataPath ([ordered]@{
        sourceFormat = 'sharpts-benchmarkdotnet-metadata-v1'
        title = $guiTitle
        benchmarks = @([ordered]@{
            fullName = $guiBenchmark.FullName; type = 'GuiRendererBenchmarks'; method = 'BatchedScalarUpdates'
            categories = @(); operationsPerInvoke = 10; parameters = @()
        })
    })
    $budgetPath = Join-Path $repositoryRoot 'benchmarks/micro/SharpTS.Gui.Benchmarks/PerformanceBudgets.json'
    $guiRun = New-GuiBenchmarkRun -ReportPath $guiReportPath -MetadataPath $guiMetadataPath -BudgetPath $budgetPath @fixed
    $batched = $guiRun.cases | Where-Object id -CEQ 'batched-scalar-updates/sharp-ts'
    Assert-True ($batched.operationsPerInvoke -eq 10) 'GUI operations-per-invoke was not retained.'
    Assert-True (($batched.measurements | Where-Object id -CEQ mean).budget.limit -eq 100000) 'GUI timing result was not joined to its budget.'
    Assert-True (($batched.measurements | Where-Object id -CEQ allocated).budget.limit -eq 40960) 'GUI allocation result was not joined to its budget.'
    $missingMount = $guiRun.cases | Where-Object id -CEQ 'initial-mount/sharp-ts'
    Assert-True (($missingMount.measurements | Where-Object id -CEQ mean).status -ceq 'missing') 'Missing GUI result was not explicit.'
    Assert-True ($null -eq ($missingMount.measurements | Where-Object id -CEQ mean).PSObject.Properties['actual']) 'Missing GUI result substituted an actual value.'

    $badGuiReportPath = Join-Path $temporaryDirectory 'bad-gui-report.json'
    $badGuiMetadataPath = Join-Path $temporaryDirectory 'bad-gui-metadata.json'
    $unknown = New-RawBenchmark 'GuiRendererBenchmarks' 'MysteryRender' 'SharpTS.Gui.Benchmarks.GuiRendererBenchmarks.MysteryRender' 1 1
    Write-FixtureJson $badGuiReportPath ([ordered]@{ Title = $guiTitle; HostEnvironmentInfo = (New-Host); Benchmarks = @($unknown) })
    Write-FixtureJson $badGuiMetadataPath ([ordered]@{
        sourceFormat = 'sharpts-benchmarkdotnet-metadata-v1'; title = $guiTitle
        benchmarks = @([ordered]@{ fullName = $unknown.FullName; type = 'GuiRendererBenchmarks'; method = 'MysteryRender'; categories = @(); operationsPerInvoke = 1; parameters = @() })
    })
    Assert-Throws {
        New-GuiBenchmarkRun -ReportPath $badGuiReportPath -MetadataPath $badGuiMetadataPath -BudgetPath $budgetPath @fixed
    } 'Unknown GUI benchmark method'

    $unknownBudgetPath = Join-Path $temporaryDirectory 'unknown-budget.json'
    $unknownBudget = Get-Content -LiteralPath $budgetPath -Raw | ConvertFrom-Json
    $unknownBudget.benchmarks | Add-Member -NotePropertyName MysteryRender -NotePropertyValue ([pscustomobject]@{ maxMeanNanoseconds = 1; maxAllocatedBytes = 1 })
    Write-FixtureJson $unknownBudgetPath $unknownBudget
    Assert-Throws {
        New-GuiBenchmarkRun -ReportPath $guiReportPath -MetadataPath $guiMetadataPath -BudgetPath $unknownBudgetPath @fixed
    } 'Unknown GUI benchmark budget'

    $packagingEvidencePath = Join-Path $temporaryDirectory 'packaging-evidence.json'
    Write-FixtureJson $packagingEvidencePath ([ordered]@{
        sourceFormat = 'sharpts-gui-packaging-v1'
        run = [ordered]@{
            timestampUtc = '2026-08-26T12:00:00Z'
            revision = $revision
            environment = [ordered]@{ operatingSystem = 'Fixture OS'; architecture = 'x64'; processor = 'Fixture CPU'; runner = 'fixture-runner' }
            tools = [ordered]@{ dotnet = '10.0.100'; runtimeIdentifier = 'win-x64' }
        }
        measurements = @(
            [ordered]@{ id = 'coldStartup'; unit = 'milliseconds'; status = 'measured'; actual = 12.5; budget = [ordered]@{ limit = 1500; sourceId = 'nativeAot.maxColdStartupMilliseconds' } },
            [ordered]@{ id = 'peakWorkingSet'; unit = 'bytes'; status = 'missing'; reason = 'notExecutable'; budget = [ordered]@{ limit = 268435456; sourceId = 'nativeAot.maxPeakWorkingSetBytes' } },
            [ordered]@{ id = 'executableSize'; unit = 'bytes'; status = 'measured'; actual = 1234; budget = [ordered]@{ limit = 52428800; sourceId = 'nativeAot.maxExecutableBytes' } },
            [ordered]@{ id = 'shippingSize'; unit = 'bytes'; status = 'measured'; actual = 4321; budget = [ordered]@{ limit = 68157440; sourceId = 'nativeAot.maxShippingBytes' } }
        )
    })
    $packagingRun = New-GuiPackagingRun $packagingEvidencePath
    $startup = ($packagingRun.cases | Where-Object method -CEQ cold-start).measurements[0]
    Assert-True ($startup.actual -eq 12500000) 'Milliseconds were not converted to canonical nanoseconds.'
    Assert-True ($startup.budget.limit -eq 1500000000) 'Duration budget was not converted with the actual.'
    $workingSet = ($packagingRun.cases | Where-Object method -CEQ peak-working-set).measurements[0]
    Assert-True ($workingSet.status -ceq 'missing' -and $workingSet.reason -ceq 'notExecutable') 'Packaging missing result was not retained.'

    $crossRun = New-CrossRuntimeRun (Join-Path $repositoryRoot 'benchmarks/cross-runtime/snapshots/latest.json')
    $snapshotPath = Join-Path $temporaryDirectory 'snapshot.json'
    [void](Export-SharpTSPublicPerformanceSnapshot `
        -Runs @($crossRun, $compilerRun, $guiRun, $packagingRun) `
        -OutputFile $snapshotPath `
        -GeneratedAtUtc '2026-08-26T12:05:00Z')
    Assert-True (Test-SharpTSPublicPerformanceSnapshotFile $snapshotPath) 'Aggregate snapshot did not pass schema validation.'
    $snapshot = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json
    Assert-True (($snapshot.runs | ForEach-Object suite | Sort-Object -Unique).Count -eq 3) 'Aggregate snapshot does not contain all three suites.'
    Assert-True (($snapshot.runs | Where-Object suite -CEQ gui).Count -eq 2) 'Independent GUI run boundaries were not retained.'

    $invalid = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json
    ($invalid.runs | Where-Object suite -CEQ compiler-micro).cases[0].measurements[0].unit = 'bytes'
    $invalidPath = Join-Path $temporaryDirectory 'invalid.json'
    Write-FixtureJson $invalidPath $invalid
    Assert-Throws { Test-SharpTSPublicPerformanceSnapshotFile $invalidPath } 'must use unit'

    Write-Host 'Public performance snapshot contract tests passed.'
} finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
