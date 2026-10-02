# Inherited accessors and top-level strict writes (#1920)

The recovered inherited getter and setter still fail and transfer independently to
[#1946](https://github.com/nickna/SharpTS/issues/1946) and
[#1947](https://github.com/nickna/SharpTS/issues/1947). A new bounded top-level strict
probe identifies a CLI/hosted compilation failure owned by
[#1948](https://github.com/nickna/SharpTS/issues/1948). This reconciliation makes no
runtime repair and preserves the original compatibility assertions.

## Recovered evidence and dispositions

Frozen #1599 L105 and [#1686](https://github.com/nickna/SharpTS/pull/1686) retain inherited
accessor and top-level strict limitations. [cases.json](issue-1920/cases.json) recovers
the getter, setter and function-scoped strict fixture verbatim from final PR head
`892f8829a1fb6eb2375749c799c07062f1fce3c1`. Incorrect original parity outputs remain
separate from the reference outputs. [historical.json](issue-1920/historical.json)
preserves the exact frozen line and its ledger hash.

| Case | Reference | Interpreted API / interpreted CLI | Compiled API | Default/builtin-typed standalone and actual hosted execution | Disposition |
| --- | --- | --- | --- | --- | --- |
| Inherited getter reads `this.own + this.base` | `7 true false true\n` | Matches | `NaN true false true\n` | Same failure | Exact retained receiver failure; #1946. |
| Inherited setter writes `this.own` | `9 true false\n` | `undefined false false\n` | Same failure | Same failure | Exact retained dispatch failure in both engines; #1947. |
| Function-scoped strict write control | `TypeError\n1\n` | Matches | Matches | Matches | Recovered positive control. |
| New top-level strict counterpart | `TypeError\n1\n` | Matches | Matches | `1\n` | Confirmed CLI/module-compilation failure; #1948. Original top-level program identity remains unproven. |

The original top-level strict source, individual old stdout and numerical deadline are
not recoverable from the linked PR body/reviews, committed fixtures, frozen issue comments
or searched `D:/nickna/SharpTS/artifacts` and `.perf-runs` bundles. That historical observation
therefore has an evidence-gap disposition. The new source explicitly places the same
non-writable write at top level; passing API execution does not establish CLI correctness.
No original source or deadline was substituted or relaxed. The recovered isolated accessor
and function fixtures retain their original 30-second execution deadline.

## Scope and verification

Open/closed issue searches and relevant local property-write/strict-directive history found
no exact later repair. #1769 installs a setter on globalThis and writes through a global
alias; these programs use ordinary prototype inheritance and do not duplicate it.
B01 / #1900 concerns interpreter non-writable static descriptors, without this ordinary
object receiver or CLI top-level directive. Related closed inherited-class and descriptor
reading issues do not cover these recovered programs. Separate getter/setter contracts
avoid combining independently testable behaviors on the strength of a guessed shared cause.

Product baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows, SDK 10.0.401/runtime
10.0.12, TypeScript 7.0.2 and Node 25.5.0; collected October 2, 2026 (America/Los_Angeles).
[results.json](issue-1920/results.json) records actual collection HEAD, all typing paths,
outputs, source hashes and 30-second operation budgets. Only the absolute repository prefix
is normalized to `<repository>`; raw results remain under `artifacts/issue-1919-1925/run-1920`.

All twelve default/standalone/hosted compilations pass IL verification. No standalone or
hosted assembly references or co-locates SharpTS.dll; hosted metadata adds the hosting ABI.
The three behavioral failures persist despite valid IL and zero exit status. The collector
Release build and 297 focused owner/accessor regressions pass without failures or skips.
Evidence files use LF through `.gitattributes` so recorded byte hashes survive checkout.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1920 artifacts/issue-1919-1925/run-1920
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1920
```

Successful controls do not repair the original accessor failures or identify the missing
historical strict program. No surrounding object/strict-mode compatibility is claimed.
