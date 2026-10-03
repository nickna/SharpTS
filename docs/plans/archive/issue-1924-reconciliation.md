# Symbol deletion/reinsertion order (#1924)

The recovered interpreter-only own-symbol-order program still fails in compiled execution.
Its implementation destination is [#1950](https://github.com/nickna/SharpTS/issues/1950).
This reconciliation preserves the original Node/interpreter expectation and makes no
runtime change or adjustment to the interpreter-only test.

## Original case and current disposition

[#1700](https://github.com/nickna/SharpTS/pull/1700) and frozen #1599 L137 explicitly
identify an interpreter-only exploratory symbol deletion/reinsertion test with a separate
compiled discrepancy and 1,183 identical metadata/IL bodies. The existing
`ObjectPrototypeTests.GetOwnPropertySymbols_PreservesCreationOrderAcrossRedefinition`
test is marked `[InterpretedOnlyData]` and contains that independently testable sequence.
[cases.json](issue-1924/cases.json) recovers its source verbatim at the original PR final head,
retaining all four output controls. [historical.json](issue-1924/historical.json) preserves
the original frozen observation and hash.

| Step | Node / interpreted API and CLI | Compiled API, default/builtin-typed standalone and actual hosted initialization |
| --- | --- | --- |
| Redefine first symbol's descriptor | `true true` | `true true` |
| Delete first symbol and add it again | `true true` (second, then first) | `false false` (wrong order) |
| Array symbols retain their creation order | `true true` | `true true` |
| Array first symbol remains non-writable | `false` | `false` |

Full reference stdout is `true true\ntrue true\ntrue true\nfalse\n`; compiled stdout is
`true true\nfalse false\ntrue true\nfalse\n`. Subprocesses exit normally with empty stderr.
The exact retained behavioral discrepancy remains, rather than being counted among passing
compiled assertions. The surrounding descriptor/array controls actually execute and pass.

The original saved exploratory assembly/log bundle and its numerical per-operation deadline
are not recoverable from the linked PR body/comments/reviews and retained
`D:/nickna/SharpTS/artifacts` / `.perf-runs` search. The committed test source and reference
assertion are recoverable; they remain unchanged. Fresh collection uses the existing
30-second harness budget, without inventing an unavailable historical deadline.

## Duplicate checks and verification

Open/closed symbol-order searches and local property-enumeration/deletion history identify
no exact later repair. #1765 enumerates string keys from declared class fields alongside
added properties; this program uses an ordinary object and symbol creation order after
deletion, so it has a distinct acceptance contract. The existing #1752 array Symbol.iterator
typing issue does not prevent this any-typed program from running.

Product baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows, .NET SDK 10.0.401/runtime
10.0.12, TypeScript 7.0.2, Node 25.5.0; collected October 2, 2026 (America/Los_Angeles).
[results.json](issue-1924/results.json) records actual collection HEAD, source hash, all
typing/execution outputs and deployment metadata. Only the absolute repository prefix is
normalized to `<repository>`; raw results remain under `artifacts/issue-1919-1925/run-1924`.

All three CLI outputs pass IL verification. Standalone/hosted outputs neither reference nor
co-locate SharpTS.dll; hosted metadata adds the hosting ABI. Runtime ordering remains wrong
despite valid IL and zero exit status. The collector Release build and 297 focused regression
cases, including the original interpreter-only assertion, pass without failures or skips.
No original expectation/deadline is weakened or broader own-key compatibility claimed.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1924 artifacts/issue-1919-1925/run-1924
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1924
```
