# Historical timing and allocation evidence (#1913)

The named original workloads pass on the current Windows baseline without raising their
limits. The generator handoff correction in #1848 has specific supporting reproduction
evidence; the original string-search and bitwise allocation spikes do not have established
root causes. Passing retries remain passing retries, not retroactive repairs.

## Original failures and precise dispositions

Frozen #1599 L95/L211/L465/L517/L667/L789 and PRs
[#1816](https://github.com/nickna/SharpTS/pull/1816),
[#1817](https://github.com/nickna/SharpTS/pull/1817),
[#1847](https://github.com/nickna/SharpTS/pull/1847) identify these observations:

| Observation and unchanged contract | Supported disposition |
| --- | --- |
| `StablePrimitiveGeneratorIterationTests.StableNumericRange_IsCorrect(Interpreted)`: 100,000 yields, output `4999950000\n`, 30 seconds; recurring subset/Windows CI timeouts | Original failures preserved. #1848's 62-class workload failed three times with default spinning, then passed in 18 seconds with zero-spin handoffs. Current original test passes in 1.0243334 seconds; compiled control passes in 0.7147192 seconds. Specific contention improvement and lifetime repair supported; arbitrary-load wall-clock guarantees are not. |
| `StablePrimitiveStringIntrinsicTests.StablePrimitiveSearch_DoesNotAllocatePerCall`: 10,000 warmups, 100,000 iterations, total 700,000, 1,024-byte ceiling; original 7,648 bytes | Not reproduced: exact committed test passes unchanged. No particular allocation repair or source of the original excess is established. The original single-sample assertion remains in place. |
| `NumericBitwiseLoweringTests.NumericTypedArrayBitwiseLoop_HasNoPerIterationAllocation`: original 6,992 bytes versus 4,096; 1,000 warmups, 1,000,000 measured iterations, results 232/64 | Original method recovered from pre-`c85a5505` history and freshly measured at 96 bytes with the original warmup/one sample. #1679's later five-sample gate also measures 96 bytes in every sample; its persistent-excess controls pass at both 4,096 and 8,192. Measurement stabilization is credited separately from product behavior. Historical transient-overhead attribution remains an inference, not reproduced causality. |
| #1680 aggregate Windows/Linux CI step exhaustion at 15 minutes | Aggregate supervision changed to 20 minutes inside the existing job budget; the original generator deadline/workload was unchanged. Later passing full CI and #1862's supervised local coverage supersede the operational budget concern, not the original generator failure. No new aggregate budget change is made here. |
| #1848 synthetic 28-compiler load exceeding 30 seconds with GC pauses | Preserved as a load-control limitation. Its exact source/GC timeline was not recovered from linked comments, committed tests, or retained primary-checkout artifacts; it is not substituted for the named 100,000-yield original or certified repaired. |
| #1862 long serial standalone tail, GUI deadline and cleanup records | Already investigated in [the September 27 report](../../test-regression-investigation-2026-09-27.md), with progress/dump limitations and separate per-process/aggregate budgets. Its 24,969 passes/3 skips are prior evidence, not a fresh full-suite run here. These independently identified workload/cleanup changes do not prove the generator/string-search root causes. |
| #1863 watcher timeout under shared TEMP traffic | Private-directory tests and diagnostics supersede that fixture problem; overflow is a reproduced mechanism, not an observed cause in the original log. Current 16 watcher cases pass. This is distinct from generator/allocation defects. |

[#1848](https://github.com/nickna/SharpTS/pull/1848) also supplies lifecycle and
per-timer microtask corrections; the corresponding current regressions pass. None of
#1847/#1862/#1863 modifies the original search-allocation limit to erase the observation.
The original failed CI logs and subset output bundles were not recovered in the bounded
search of linked bodies/comments, source history, `D:/nickna/SharpTS/artifacts` and
`.perf-runs`. Frozen outputs/limits and committed workloads remain recoverable. No
individually reproduced current defect needs a new fix issue in this reconciliation.

## Current evidence and reproduction

Product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`; Windows,
SDK 10.0.401/runtime 10.0.12, October 1, 2026 (America/Los_Angeles). The preceding
#1912 commit changes only evidence files, not these classes or runtime behavior.
[results.json](issue-1913/results.json) retains all 72 fresh test names, durations,
outcomes and available output. [numeric-original.json](issue-1913/numeric-original.json)
retains the recovered one-sample workload and actual measurement. These are in-process
interpreter/compiled and emitted-helper checks; no fresh Linux, CLI/standalone or hosted
performance measurement is inferred from them.

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~StablePrimitiveGeneratorIterationTests|FullyQualifiedName~StablePrimitiveStringIntrinsicTests|FullyQualifiedName~NumericBitwiseLoweringTests|FullyQualifiedName~GeneratorLifetimeTests|FullyQualifiedName~TimerMicrotaskCheckpointTests|FullyQualifiedName~FsWatchTests'
dotnet build tests/fixtures/SharpTS.HistoricalEvidence/SharpTS.HistoricalEvidence.csproj -c Release
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll timing artifacts/issue-reconciliation/numeric-original.json
```

All 72 pass with zero skips. The recovered numeric measurement also meets the original
4,096-byte limit. No workloads, limits, filters, or assertion expectations were relaxed.
Current success establishes bounded current status; unavailable original diagnostics
still limit causal conclusions.
