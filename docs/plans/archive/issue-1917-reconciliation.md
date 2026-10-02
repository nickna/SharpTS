# Math identity, nonfinite sums and signed zero (#1917)

All three historical groups remain reproducible. They have four independent correction
destinations: singleton identity [#1935](https://github.com/nickna/SharpTS/issues/1935),
nonfinite summation [#1936](https://github.com/nickna/SharpTS/issues/1936), sign
[#1937](https://github.com/nickna/SharpTS/issues/1937), and min/max zero ties
[#1938](https://github.com/nickna/SharpTS/issues/1938). No full Math-conformance goal
or runtime fix is introduced by this reconciliation.

## Original evidence and current dispositions

Frozen #1599 L97 and [PR #1681](https://github.com/nickna/SharpTS/pull/1681) explicitly
preserve the original Math identity, aliased nonfinite sum and signed-zero outputs as
refactor parity. All four [source programs](issue-1917/cases.json) were recovered
verbatim from `MathMetadataPrograms` at head
`43baee9c6f99e6b3d55a356c23a68765bb701d07`; their original parity expectations remain
in the provenance field. They used the existing 30-second standalone execution deadline.
The old comparison-binary/output bundles were not recovered from linked comments or
the retained primary checkout's artifact directories. No old compile deadline is invented.

| Original source / independently testable behavior | Reference or specified output | Current compiled API, standalone and hosted output | Disposition |
| --- | --- | --- | --- |
| `identity.ts`: m===globalThis.Math | `true true true true\nfloor 1 0 2 2\n` | `false true true true\nfloor 1 0 2 2\n` | Exact recovered failure; #1935. Floor/random identities and introspection remain positive controls. |
| `nonfinite.ts`: aliased sum over five nonfinite/overflow inputs | `Infinity -Infinity NaN NaN Infinity\n` | `0 -Infinity NaN 0 Infinity\n` | Exact recovered failure; #1936. Negative Infinity, opposing infinities and finite overflow are independent preserved controls. |
| `sign.ts`: Object.is(sign(-0),-0) | `-1 0 1 NaN true\n8 1 NaN\n` | `-1 0 1 NaN false\n8 1 NaN\n` | Exact recovered sign loss; #1937. Nonzero/NaN sign and pow controls pass. |
| `zero-ties.ts`: max(-0,0), min(0,-0) | `-Infinity Infinity 9 -2 NaN\nfalse true\n` | `-Infinity Infinity 9 -2 NaN\ntrue false\n` | Exact recovered zero-tie failures; #1938 owns both order-sensitive min/max rules independently of sign. |

Interpretation matches the specified outputs for all four sources. Node 25.5.0 matches
identity/sign/min/max but **does not implement Math.sumPrecise**: the nonfinite source
fails with `TypeError: sum is not a function`. That actual failure is retained. Its
expected nonfinite result follows the [TC39 Math.sumPrecise algorithm](https://tc39.es/proposal-math-sum/#sec-math.sumprecise):
NaN propagates, one-sign infinity persists, opposite infinities become NaN, and finite
overflow rounds to infinity. This is a specification-derived reference, not a claimed
Node execution result. The interpreter agrees with it. Node's sumPrecise identity
comparison in `identity.ts` compares two absent values and is **not** a positive method
identity control; floor and random are real controls there.

## Execution boundary and verification

Product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`, Windows,
SDK 10.0.401/runtime 10.0.12, Node 25.5.0, October 1, 2026 (America/Los_Angeles).
[results.json](issue-1917/results.json) preserves the actual evidence-branch HEAD,
source hashes, diagnostics, outputs and assembly references. Preceding commits change
only evidence/collectors. [historical.json](issue-1917/historical.json) retains L97's
text and hash.

Default and explicit ESNext CLI declaration paths reject the identity/nonfinite sources
because their Math interface lacks sumPrecise; those rejected paths are not counted as
runtime failures or silently retried under the same label. `--noLib` executes them, as
the historical phase did. Both sign/zero-tie sources compile and execute with default
CLI typing and reproduce the compiled defects. Eight builtin-typed standalone/hosted
assemblies and two default CLI assemblies pass IL verification. None references or
co-locates `SharpTS.dll`; hosted outputs also reference the hosting ABI. Hosted outputs
come from real runtime initialization, not compilation alone. The four defects coexist
with valid IL and standalone deployment.

TypeScript 7.0.2 also rejects those two sources' Math.sumPrecise property with ESNext/DOM
libraries; the other two sources compile. [reference-compilation.json](issue-1917/reference-compilation.json)
preserves that independent reference declaration limitation. Node's runtime absence and
TypeScript's declaration absence do not replace the specified summation expectation.

Open/closed searches and current Math history found no exact fixes before #1935–#1938.
Closed #288 addressed interpreter method identity/enumeration, not this compiled
globalThis singleton comparison. #1509's fixed-arity min/max optimization is not
evidence that these aliased zero ties work; the actual source still fails. The early
sumPrecise descriptor fallback is retained staging behavior, not a new conformance defect
or a correction destination. Completed metadata ownership remains credited.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1917 artifacts/issue-reconciliation/issue-1917
```

Collector exit status means evidence collection completed. All 244 existing owner cases
and repository code-quality gates pass; that coverage validates retained metadata
contracts, not the incorrect outputs above. No original assertion, workload or deadline
was relaxed, and no runtime implementation was changed.
