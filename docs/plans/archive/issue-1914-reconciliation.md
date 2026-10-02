# String.raw declarations and computed tags (#1914)

The current recovered String.raw declaration probes succeed. The computed-tag receiver
failure remains and is transferred to [#1931](https://github.com/nickna/SharpTS/issues/1931).
This changes no runtime behavior and does not claim an exact historical declaration repair.

## Recovery and current status

Frozen #1599 L89 and [PR #1677](https://github.com/nickna/SharpTS/pull/1677) preserve two
distinct observations: the CLI declaration path rejected String.raw (so execution used
`--noLib`), and computed tagged templates lost their receiver. The PR gives no exact
rejected source or diagnostic. Its final committed `TemplateMetadataPrograms` rows 2, 3
and 8 are recoverable; each source in [cases.json](issue-1914/cases.json) was checked
verbatim against head `75bc19d2e657fe67b6746630ade814a8f78f79ac`. They retain the original
parity outputs, including `undefined:c|d:8`, separately from the reference expectations.
The standalone phase retained a 30-second execution deadline. The original declaration
failure bundle was not recovered from linked comments, committed fixtures, or the retained
primary checkout's `artifacts`/`.perf-runs` directories.

| Named observation | Supported disposition |
| --- | --- |
| String.raw declaration availability | The recovered tagged and method-value/constructor-alias sources now pass default CLI interpretation AND default CLI compilation/execution without `--noLib`; ESNext and builtin-typing paths also accept them. Missing original rejected source/diagnostic prevents declaring that exact historical case repaired or identifying a fixing commit. |
| Computed `obj['tag']` receiver | Still fails in compiled API, CLI-compiled standalone and actual hosted initialization; Node and both interpreted paths retain `obj`. Exact recovered source and discrepancy are transferred to #1931. |

For [computed-tag.ts](issue-1914/computed-tag.ts), Node and interpretation print
`P:a|b:7\nP:c|d:8\n`. All three compiled execution paths print
`P:a|b:7\nundefined:c|d:8\n`. The ordinary member tag is the positive receiver control.
[raw.ts](issue-1914/raw.ts) and [raw-value.ts](issue-1914/raw-value.ts) match their Node
outputs across the API, CLI, standalone and hosted paths. TypeScript 7.0.2 compiles all
three with ES2024/DOM libraries; each emitted JavaScript file matches the Node result.
The separate direct-call probe from row 5 was also accepted by the default declaration
path; its interpreted string-valued `raw` indexing output differs. That companion
runtime behavior is outside the two frozen observations and is not certified conformant
by this declaration check.

## Evidence and verification

Product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`, main checked October 1,
2026 (America/Los_Angeles). Earlier commits on this evidence branch affect only docs and
collectors; [results.json](issue-1914/results.json) records the actual collection HEAD,
runtime 10.0.12, SDK 10.0.401, Node 25.5.0, source hashes and all outputs/diagnostics.
[historical.json](issue-1914/historical.json) retains the frozen line and hash.
Original saved output bundles and numerical compile deadlines were not recovered; the
fresh runner records a separate 30-second deadline per operation, not an invented old one.

Six builtin-typing standalone/hosted compilations and three default CLI compilations
pass IL verification. Standalone outputs carry framework references; hosted outputs also
carry `SharpTS.Hosting.Abstractions`. None co-locates or references `SharpTS.dll`.
The computed receiver output remains wrong despite valid IL. Current open/closed issue
searches and source history found no exact receiver fix issue before #1931; #1677
explicitly preserved the discrepancy. No default-library/full String-family claim is made.

```powershell
dotnet build tests/fixtures/SharpTS.HistoricalEvidence/SharpTS.HistoricalEvidence.csproj -c Release
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1914 artifacts/issue-reconciliation/issue-1914
```

Collection exit status means the observations were recorded, not that they all passed.
The final Release collector build and all 244 existing template/wrapper/numeric/Number/
Math/BigInt owner cases pass. No original parity assertion was changed into a conformance
assertion. Raw reports remain under `artifacts/issue-reconciliation`; checked-in JSON
replaces only the local repository prefix with `<repository>`.
