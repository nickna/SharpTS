# Boxed/explicit Number and async lookup (#1915)

Two exact recovered hook failures remain and transfer independently to
[#1932](https://github.com/nickna/SharpTS/issues/1932) and
[#1933](https://github.com/nickna/SharpTS/issues/1933). A matched async Number lookup
probe passes; the missing original failing source prevents an exact repair claim.

## Recovery and dispositions

Frozen #1599 L93/L103 and PRs [#1679](https://github.com/nickna/SharpTS/pull/1679) /
[#1685](https://github.com/nickna/SharpTS/pull/1685) preserve boxed Number string-hook,
explicit Number exotic-hook and async Number lookup findings. Four sources are recovered
verbatim from the original heads recorded in [cases.json](issue-1915/cases.json), with
the original parity outputs retained separately. The original saved failure bundles were
not recovered from linked comments or the primary checkout's `artifacts`/`.perf-runs`;
the async failing source/diagnostic is not present in the committed successful outer-wrapper
control. The original isolated executions used 30 seconds; the fresh collector explicitly
uses that budget per operation. No reference expectation was replaced by parity output.

| Source / named observation | Reference and current interpreted behavior | Current compiled API, standalone and hosted behavior | Disposition |
| --- | --- | --- | --- |
| `boxed-ordinary.ts`, ordinary hook control | `11 true own\nvalue!\n` | Same | Passes; it was already a passing phase control, not evidence that the exotic failure was repaired. |
| `boxed-exotic.ts`, boxed `Symbol.toPrimitive` string hint | `5 true 4 default;default;string;\n` | `5 true 1 default;default;\n` | Exact current failure: String(box) skips the string hook. #1932 owns correction. |
| `explicit-exotic.ts`, explicit `Number(value)` | Node: `9 9 numbernumber\n`; interpreted: `9 NaN number\n` | `NaN 9 number\n` | Number(value) skips the hook in compiled execution. #1933 owns this specific failure; the interpreted unary-plus companion differs independently. |
| `async-outer-control.ts`, wrapper created before async entry | `6\n` | `6\n` | Exact recovered passing suspension control. It did not exercise the original lookup failure. |
| `async-number.ts`, new matched post-await construction probe | `6\n` | `6\n` | Current lookup passes; original failing program is unrecovered, so status of that exact original remains evidence-limited. No fixing commit is attributed. |

The fresh async probe constructs `new Number(4)` **inside** the async function after
await, then checks arithmetic. Its rejection handler would expose a lookup error.
It is explicitly labeled newly constructed, and never substituted for missing original
evidence. The unary-plus companion is recorded separately in the consolidated
reconciliation's [companion findings](issue-1912-1918-companion-findings.md); it is
excluded from claims about the original explicit-Number failure's interpreted status.

## Verification and scope

Product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`, Windows,
SDK 10.0.401/runtime 10.0.12, Node 25.5.0, October 1, 2026 (America/Los_Angeles).
[results.json](issue-1915/results.json) records the actual evidence-branch HEAD, source
hashes and full outputs. Its preceding evidence-only commits change no product behavior.
[historical.json](issue-1915/historical.json) preserves the frozen excerpts and hashes.
Default, `--noLib`, and ESNext CLI interpretation all reproduce the interpreted outputs;
default CLI standalone execution also reproduces the compiled outputs. All fifteen
default/builtin-typed standalone and hosted compilations pass IL verification. No tested
output references or co-locates `SharpTS.dll`; hosted output adds the hosting ABI reference.
Hosted results are real initialization executions, not inferred from compilation.

Open/closed issue searches and source history found no exact hook correction before
#1932/#1933. [#1770](https://github.com/nickna/SharpTS/issues/1770) owns global isNaN
function-value coercion, a different original source and predicate contract. #1795
concerns primitive Number.prototype hooks in string-symbol dispatch, not boxed own hooks
or explicit Number conversion. Neither is duplicated or claimed fixed here.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1915 artifacts/issue-reconciliation/issue-1915
```

The collector's successful exit means collection completed; use the reference outputs
to judge correctness. Release builds, the 244 owner cases and repository code-quality
gates pass. No runtime implementation, original parity assertions, deadline or workload
was changed by this evidence transfer.
