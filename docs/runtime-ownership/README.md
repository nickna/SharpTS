# Runtime ownership verification for #1864

This ledger records the fifteen fixed outcomes of [epic #1864](https://github.com/nickna/SharpTS/issues/1864).
The inventory is frozen at `83a41096108fe6de739fa7cfcb148c4bb1193121`; this verification
uses source at `78931b4a` (including the subsequent historical reconciliations).
Each cohort has its own report and commit. The reports distinguish metadata ownership
from guest semantics: executing saved output supports the specific tested contracts,
not full JavaScript or Node compatibility.

## Shared lifetime and evidence conventions

[`RuntimeEmitter.EmitAll`](../../src/SharpTS/Compilation/RuntimeEmitter.cs) creates a fresh
[`EmittedRuntime`](../../src/SharpTS/Compilation/EmittedRuntime.cs) for each module.
Its components own that output's declarations; consumers read them through the runtime
or explicit component inputs. The reusable emitter retains its type provider, hosted
selection and current feature selection. These configuration references are intentional;
generated builders must belong to the current output. Sequential reuse is the tested
contract; concurrent use of one emitter is not established.

Declaration makes forward handles readable before bodies or generated types are complete.
Checked owners reject missing reads, null assignments, premature completion
where they track staged work, and writes after completion. Single-assignment owners reject duplicate
assignments; reports identify intentional replacement during declaration. Completion seals metadata,
not guest objects: output-local caches, descriptors, prototype tables, thread-static
contexts and guest collections remain mutable under their individual contracts.
Optional owners and optional implementations are distinct; a required facade can finish
without its feature-specific implementation. Each report spells out its selection.

Construction records and method-local builders are retained when their lifetime is one
emission. They borrow canonical component handles instead of becoming competing owners.
BCL method/type references belong to the framework/type provider; generated tokens
belong to the output module. Shared framework resolution remains the independent
infrastructure scope of [#1866](https://github.com/nickna/SharpTS/issues/1866).

The linked test source is part of the evidence: lifecycle tests exercise failures and
repairable completion, reuse tests compare assemblies and earlier caches, and saved
output tests verify IL **and execute** the generated helpers. Hosted tests allow
`SharpTS.Hosting.Abstractions`; ordinary output must have no `SharpTS` reference.
Where a cohort has a different completion/identity policy, its report overrides the
general convention above. A test count alone does not establish an owner contract.

## Reproduction

Build once with `dotnet build tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release`.
Each report lists the exact test classes used. Join its class names with
`FullyQualifiedName~ClassName|FullyQualifiedName~OtherClass` and run:

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-build --no-restore `
  --filter $filter --logger 'trx;LogFileName=R01.trx' `
  --results-directory artifacts/epic-1864 --blame-hang-timeout 2m
```

Recorded runs use Windows ARM64, .NET SDK 10.0.401/runtime 10.0.12, Release, on
2026-10-02. Logs and TRX files are local artifacts under `artifacts/epic-1864/`.
The build passed with the existing `Microsoft.Build.Tasks.Git` NU1902 warning.
These reports introduce no production changes. Historical unrepaired semantic reports
retain the destinations in the
[frozen reconciliation](../plans/archive/1599-historical-reconciliation.md).

## Cohorts

| Goal | Report | Roots |
| --- | --- | ---: |
| [#1868](https://github.com/nickna/SharpTS/issues/1868) | [R01: Calls and function values](R01.md) | 11 |
| [#1869](https://github.com/nickna/SharpTS/issues/1869) | [R02: Iterator and generator protocols](R02.md) | 8 |
| [#1870](https://github.com/nickna/SharpTS/issues/1870) | [R03: Objects, descriptors and property access](R03.md) | 17 |
| [#1871](https://github.com/nickna/SharpTS/issues/1871) | [R04: JSON and specialized representations](R04.md) | 4 |
| [#1872](https://github.com/nickna/SharpTS/issues/1872) | [R05: Runtime roots, modules and deployment](R05.md) | 13 |
| [#1873](https://github.com/nickna/SharpTS/issues/1873) | [R06: Primitive values, conversions and errors](R06.md) | 17 |
| [#1874](https://github.com/nickna/SharpTS/issues/1874) | [R07: Arrays and collections](R07.md) | 9 |
| [#1875](https://github.com/nickna/SharpTS/issues/1875) | [R08: Scheduling, cancellation and async context](R08.md) | 8 |
| [#1876](https://github.com/nickna/SharpTS/issues/1876) | [R09: Process, host APIs and text utilities](R09.md) | 12 |
| [#1877](https://github.com/nickna/SharpTS/issues/1877) | [R10: Filesystem](R10.md) | 4 |
| [#1878](https://github.com/nickna/SharpTS/issues/1878) | [R11: Networking and fetch](R11.md) | 6 |
| [#1879](https://github.com/nickna/SharpTS/issues/1879) | [R12: Binary storage and atomics](R12.md) | 6 |
| [#1880](https://github.com/nickna/SharpTS/issues/1880) | [R13: Cryptography](R13.md) | 2 |
| [#1881](https://github.com/nickna/SharpTS/issues/1881) | [R14: Streams, compression and events](R14.md) | 4 |
| [#1882](https://github.com/nickna/SharpTS/issues/1882) | [R15: Workers and cross-context values](R15.md) | 5 |

All **126 distinct roots** have supported metadata ownership dispositions. Required
guest mutation, construction-local metadata, intentional aliases and feature-specific
absence are retained with the evidence recorded in each report. No new unresolved
ownership defect is transferred by this pass. Historical semantic failures retain
the concrete linked destinations in the frozen reconciliation; none is silently
reclassified as repaired by ownership tests.

The fifteen focused selections passed **5,085 test executions** with no skips or
failures in their final runs. Selections overlap, so this is not a count of distinct
tests. Supplementary HTTP and dual-mode Node stream execution passed **192/192**.
R08 adds a missing saved-output execution proof for pending timer/FIFO job/Promise
state surviving reuse. R11 records the initial two Windows certificate-import failures
and the unchanged selection's passing run with certificate-store access. No assertion,
deadline or implementation was relaxed to make that run pass.

The broader hermetic core run completed with **23,419 passed, 1 failed and 3
skipped** out of 23,423 cases. The unchanged compiled
`DynamicIteratorResultTests.EscapedNumericClosureKeepsLiveUpdates` throws in
`$Program.create(Double)`; isolated execution crashes the test host. The same
crash was independently reproduced after restoring/building an archive of unchanged
main commit `78931b4a`. The original program, expected `13 14` / `true Infinity`
output, interpreted passing control and reproduction are preserved in separate
compiler defect [#1956](https://github.com/nickna/SharpTS/issues/1956). This report
claims no repair of that defect and no passing full core suite. The three skips
are the existing Unix shebang case and two Windows wildcard-HttpListener cases
requiring a provisioned URL ACL. Original core and baseline logs/TRX results are
retained under `artifacts/epic-1864/`.

`scripts/test-code-quality.ps1` passed its analyzer, mutation, duplicate and
dead-code checks (zero errors). The complete diff passes `git diff --check`, and
the ledger's 126 frozen roots and local source/test links were checked for coverage
and resolution.
