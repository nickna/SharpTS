# Retained callback DelegateCtor verifier finding (#1922)

The historical invalid-IL finding remains unresolved evidence: the original callback
program, full verifier diagnostic and saved assembly are not recoverable from the bounded
search. A recovered Error/Promise callback control passes both execution and verification,
but is not identified with that original example. No repair of the retained finding is claimed.

## What is preserved and what is missing

[#1697](https://github.com/nickna/SharpTS/pull/1697) explicitly records an exploratory
callback example with an original `DelegateCtor` verifier finding at offset **199** on
both baseline and actual compilers, identical normalized metadata/IL and unchanged
source/expectations. Frozen #1599 L131 additionally records expected execution on baseline,
preview and actual outputs. The example was excluded from passing IL totals.
[historical.json](issue-1922/historical.json) preserves that frozen record and ledger hash.

The PR body/comments/reviews, final-head committed fixtures, frozen issue comments and
retained `D:/nickna/SharpTS/artifacts` / `.perf-runs` evidence do not supply its program,
full verifier text, method identity, source hash, output bytes or assembly. `DelegateCtor`
and offset 199 are the recoverable diagnostic fragment, not a reconstructed exact diagnostic.
The historical numerical compilation/execution deadlines remain unavailable. Runtime
success in the historical comparison cannot waive invalid IL.

| Observation | Supported disposition |
| --- | --- |
| Original callback DelegateCtor example at offset 199 | Lacks recoverable source/assembly/full diagnostic. Preserve the invalid-IL finding and expected historical execution separately. Current original-case status cannot be established. |
| Recovered `ErrorMetadataPrograms/promise` callback control | Node, interpreted API/CLI, compiled API, default/builtin-typed standalone and actual hosted initialization all print `true true reject\n`; all three CLI outputs pass IL verification. It is a separate passing control. |

[cases.json](issue-1922/cases.json) identifies the control at #1697 final head
`6ab6b254d6726e3274dccf48823998510e114b6b`. It uses a caught RangeError identity, subtype
and message assertion, and retains the original isolated 30-second execution deadline.
The committed fixture is one of the PR's passing cases; it cannot be relabeled the failed
exploratory source just because both contain callbacks.

## Duplicate and repair checks

Open/closed DelegateCtor/callback-verifier searches and local function/invocation history
do not recover an exact successor or repair for this unidentified program. Later ownership
changes to function/invocation ownership preserve existing behavior and
do not by themselves establish a verifier correction. Other verifier issues such as
#1781 and closed #1246 have different programs/diagnostics. No confirmed current original-case
defect is fabricated into a new overlapping fix issue. Recovering the original assembly or
program remains the prerequisite to such a scoped implementation claim.

## Current control evidence

Product baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows, .NET SDK 10.0.401/runtime
10.0.12, TypeScript 7.0.2 and Node 25.5.0; collected October 2, 2026 (America/Los_Angeles).
[results.json](issue-1922/results.json) retains actual collection HEAD, source hash,
outputs, deployment/verification metadata and the current 30-second operation budget.
Only the absolute repository prefix is replaced with `<repository>`; raw results remain
under `artifacts/issue-1919-1925/run-1922`.

All three control assemblies pass IL verification, and all guest paths are actually executed.
Standalone/hosted outputs neither reference nor co-locate SharpTS.dll; hosted output adds
the hosting ABI. The collector Release build and 297 focused owner/accessor regression
cases pass without failures or skips. These facts validate the control, not the missing
original callback example. Original assertions and budgets remain unchanged.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1922 artifacts/issue-1919-1925/run-1922
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1922
```
