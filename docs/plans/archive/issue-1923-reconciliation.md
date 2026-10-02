# Custom Symbol branding and combined accessors (#1923)

A bounded current probe confirms that custom Symbol.toStringTag does not affect
Object.prototype.toString in either engine; its concrete implementation destination is
[#1949](https://github.com/nickna/SharpTS/issues/1949). The original exploratory tag source
is unavailable, so exact historical identity is unproven. A recovered committed combined
getter/setter program passes, without proving the unnamed historical observation repaired.

## Recovery and individual dispositions

[#1699](https://github.com/nickna/SharpTS/pull/1699) and frozen #1599 L135 retain a custom
Symbol tag difference with 1,388 identical metadata/IL bodies, excluded from passing output
totals. L143 / [#1703](https://github.com/nickna/SharpTS/pull/1703) retain a combined Symbol
getter/setter observation with separate 1,001-body parity evidence. These counts and failed
statuses are preserved in [historical.json](issue-1923/historical.json).

The linked PR bodies/comments/reviews, committed fixtures, frozen issue comments and
retained `D:/nickna/SharpTS/artifacts` / `.perf-runs` bundles do not identify the exploratory
tag source or the independently saved combined observation. Their individual output bytes,
source hashes, saved assemblies and numerical deadlines remain unavailable. The current
probes do not replace either original program or claim its repair.

| Program | Reference output | Interpreted API/CLI, compiled API, default/builtin-typed standalone and actual hosted initialization | Disposition |
| --- | --- | --- | --- |
| New own `[Symbol.toStringTag]: 'Widget'` | `[object Widget]\nWidget\n` | `[object Object]\nWidget\n` | Confirmed current branding defect in both engines; #1949. Direct symbol lookup is a positive control. Historical source identity is unproven. |
| Recovered committed class-expression combined accessor control | `tag5 5\ntag99 99\n` | Matches | Passing named-behavior control; no historical repair claim. |

[cases.json](issue-1923/cases.json) recovers the combined control verbatim from
`ComputedAccessorNameTests.SymbolAccessor_InClassExpression_GetterSetterWithFieldAndModuleLocalKey`
at #1699's final head; it is also carried by the later `SymbolMetadataPrograms/class_expression`
fixture. That identifies the committed program precisely, but the frozen note supplies no
identifier that binds it to the separate exploratory assembly. The custom tag probe is
explicitly new and retains both Object branding and direct lookup outputs.

## Scope, duplicate checks and verification

Open/closed toStringTag searches find closed #261/#266/#281/#282 for parsing and symbol
accessor registration, and the older Math branding metadata repair. These do not fix this
plain object's custom brand; the passing class accessors preserve their credited work.
Local Symbol/prototype history and current execution establish no exact later repair.
#1949 has one concrete output goal without broadening into general Symbol compatibility.

Product baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows, .NET SDK 10.0.401/runtime
10.0.12, TypeScript 7.0.2, Node 25.5.0; collected October 2, 2026 (America/Los_Angeles).
[results.json](issue-1923/results.json) records collection HEAD, source hashes, all outputs,
diagnostics, deployment metadata and the current 30-second operation budget. Only the
absolute repository prefix is normalized to `<repository>`; raw results remain under
`artifacts/issue-1919-1925/run-1923`. No unknown historical deadline is inferred from it.

All six CLI assemblies pass IL verification. No standalone/hosted assembly references or
co-locates SharpTS.dll; hosted outputs add the hosting ABI. The custom tag output is wrong
despite valid IL and normal exits. The Release collector build and 297 focused owner/accessor
regressions pass with no failures or skips. Original assertions and budgets remain intact.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1923 artifacts/issue-1919-1925/run-1923
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1923
```
