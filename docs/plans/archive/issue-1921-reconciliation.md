# RegExp species and custom-exec history (#1921)

Neither of the two unnamed exploratory failures can be identified from recoverable
program/diagnostic/output evidence. Both historical failed statuses remain preserved;
neither is claimed repaired or identified with a passing control. This bounded recovery
conclusion completes the reconciliation without inventing an implementation defect.

## Separate historical dispositions

[#1695](https://github.com/nickna/SharpTS/pull/1695) and frozen #1599 L127 record two
pre-existing species/custom-exec differences with original Node expectations retained
separately. The PR body gives no failing program, per-case output, source hash or diagnostic.
The linked comments/reviews, original final-head tests, frozen issue comments and retained
`D:/nickna/SharpTS/artifacts` / `.perf-runs` bundles do not recover those records. The original
saved failing assembly bundles and their reference results/deadlines remain unavailable.
[historical.json](issue-1921/historical.json) preserves the frozen line and its ledger hash.

| Named historical observation | Disposition |
| --- | --- |
| Species-related exploratory difference | Lacks recoverable original source, output, expectation and individual deadline. Preserve the original failure; no exact identity with either committed species fixture is established. |
| Custom-exec-related exploratory difference | Same evidence gap. No original custom-exec program or exception has been identified. |

The phrase “two species/custom-exec differences” does not supply individual identifiers
or prove a one-to-one mapping. These categories preserve the named scope without inventing
the missing programs. No source or expectation is replaced by the probes below.

## Independently verified controls

[cases.json](issue-1921/cases.json) recovers the split and matchAll species programs
verbatim from #1695 final head `ec126f37ae15bf2f7a127dae904f71da9647b386`. Their passing
original parity outputs and 30-second isolated execution deadlines remain unchanged.
The simple exec override is explicitly a new control, not historical failure recovery.

| Control | Reference output | Interpreted API/CLI, compiled API, default/builtin-typed standalone and actual hosted initialization |
| --- | --- | --- |
| Split species constructs a `/b/y` splitter from `/a/` | `a y\na\|c 0\n` | Matches. |
| matchAll species constructs `/b/g` and retains original lastIndex | `a g\nb 1 b 2 true 1\n` | Matches. |
| New custom `exec` override returns null through `test` | `exec x\nfalse\n` | Matches. |

The original source commits and manifests identify the controls precisely. They do not
establish full species/custom-exec conformance. Relevant historical repairs include
`4b995392` and `8eddee78` for matchAll species flag behavior; they precede the ownership
phase and cannot be cited as later repairs of these unidentified failures. Open/closed
species/custom-exec searches find broader #101/#102/#112 and their conformance work,
without an exact mapping to either missing original. No confirmed remaining defect in
these bounded controls requires a new fix issue.

## Verification and reproduction

Product baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows, SDK 10.0.401/runtime
10.0.12, TypeScript 7.0.2, Node 25.5.0; collected October 2, 2026 (America/Los_Angeles).
[results.json](issue-1921/results.json) records actual collection HEAD, source hashes,
all outputs/diagnostics, deployment metadata and the current 30-second operation budget.
Only the absolute repository prefix is normalized to `<repository>`; raw reports remain
under `artifacts/issue-1919-1925/run-1921`.

All nine default/standalone/hosted compilations pass IL verification. Standalone/hosted
output neither references nor co-locates SharpTS.dll; hosted metadata adds the hosting
ABI. Runtime results were actually executed and checked separately from verifier success.
The Release collector build and 297 focused owner/accessor regressions pass without
failures or skips. Missing original expectations/deadlines remain missing, not relaxed.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1921 artifacts/issue-1919-1925/run-1921
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1921
```
