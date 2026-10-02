# Explicit hexadecimal radix prefix (#1916)

The original dynamic-radix program still returns `0` for `parseInt('0xff',16)` in
compiled execution, against Node's `255`. Correction is transferred to
[#1934](https://github.com/nickna/SharpTS/issues/1934); the Number ownership migration
and its withdrawn regression finding remain separate from runtime correctness.

## Recovered evidence

Frozen #1599 L95 and [PR #1680](https://github.com/nickna/SharpTS/pull/1680) retain this
discrepancy. The [original review finding](https://github.com/nickna/SharpTS/pull/1680#discussion_r4025270337)
names explicit radix 16; the [baseline verification reply](https://github.com/nickna/SharpTS/pull/1680#discussion_r4025302976)
records four successful re-executions of saved baseline/current assemblies and the
unchanged output `0 21 1295 -9 NaN 16`. The
[reviewer withdrawal](https://github.com/nickna/SharpTS/pull/1680#discussion_r4025311239)
accepts that it was pre-existing and unchanged, not that it was repaired.

[radix.ts](issue-1916/radix.ts) is recovered verbatim from the original final head's
`NumberMetadataPrograms` row 1, including the dynamic helper and all six controls.
[cases.json](issue-1916/cases.json) records head
`f39f2420f799a94f6e5747b43678bb6396d862d0`, the original parity output and the separate
reference expectation `255 21 1295 -9 NaN 16\n`. The standalone phase's execution
deadline was 30 seconds. Old binary/log bundles were not recovered from the linked
comments or retained primary-checkout artifact directories; the exact source and
review output are recoverable. No old compile deadline is inferred.

## Current execution

Product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`, Windows,
SDK 10.0.401/runtime 10.0.12, Node 25.5.0, October 1, 2026 (America/Los_Angeles).
[results.json](issue-1916/results.json) retains the actual evidence-branch HEAD and
all observations; its preceding commits change no product behavior.
[historical.json](issue-1916/historical.json) preserves the frozen line and hash.

| Path | Actual stdout |
| --- | --- |
| Node | `255 21 1295 -9 NaN 16\n` |
| Interpreted API and default/noLib/ESNext interpreted CLI | `255 21 NaN -9 NaN 16\n` |
| Compiled API, default CLI standalone, noLib standalone and actual hosted initialization | `0 21 1295 -9 NaN 16\n` |

The interpreter gets the **named hexadecimal-prefix case** right. Its base-36 companion
differs independently and is recorded in [companion findings](issue-1912-1918-companion-findings.md),
not counted as a repaired whole-program result. The compiled failure matches the recovered
historical result. All three saved default/builtin-typed standalone/hosted compilations
pass IL verification. Their references preserve standalone deployment and the hosted ABI;
none references or co-locates `SharpTS.dll`. Valid IL does not certify parsing output.

The general helper's prefix stripping is guarded by the radix-zero branch, consistent
with this observed dynamic-radix result. The open/closed issue searches and local Number
history found no exact prefix correction before #1934. The decimal fast-path/constant
optimizations #1480/#1474 do not establish this general dynamic-radix repair. Global
predicate function-value coercion remains [#1770](https://github.com/nickna/SharpTS/issues/1770),
with its original source and expected outputs; it is not part of this prefix issue.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1916 artifacts/issue-reconciliation/issue-1916
```

Collection succeeds and records the failure; it does not accept `0` as conformance.
The 244 owner cases and code-quality gates pass. Original parity expectations and the
runtime implementation remain unchanged; no parsing fix is claimed in this reconciliation.
