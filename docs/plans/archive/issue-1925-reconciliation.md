# Length and Promise callback enumerability (#1925)

Current bounded probes split four confirmed behaviors into separate implementation issues:
array length predicate [#1951](https://github.com/nickna/SharpTS/issues/1951), compiled Promise
callback name predicate [#1952](https://github.com/nickna/SharpTS/issues/1952), primitive string
length descriptor [#1953](https://github.com/nickna/SharpTS/issues/1953), and primitive string
length predicate [#1954](https://github.com/nickna/SharpTS/issues/1954). The original exploratory
programs remain unrecovered; these are explicit new probes, not claimed exact historical cases.

## Preserved history and recovery limits

[#1701](https://github.com/nickna/SharpTS/pull/1701) and frozen #1599 L139 record two
exploratory Node expectation differences: array/string length enumerability and Promise
callback name enumerability, retaining 996 and 1,136 identical metadata/IL bodies respectively.
They were excluded from passing expected-output totals. [historical.json](issue-1925/historical.json)
preserves the frozen line and ledger hash.

The original exploratory source/output/reference bytes, exact constructor/boxing forms,
individual deadlines and saved assemblies are not recoverable from the linked body/comments/
reviews, final-head committed fixtures, frozen issue comments or retained
`D:/nickna/SharpTS/artifacts` / `.perf-runs` evidence. The passing committed own-property
fixtures do not identify those separately retained failures. Both original observations
therefore retain an evidence-gap disposition; their failed status is not erased by controls.

## Separate current observations

[cases.json](issue-1925/cases.json) supplies five explicitly new sources. Node output and
TypeScript compilation are retained separately from actual guest results. The descriptor,
predicate and Object.keys checks distinguish independent attributes instead of assigning
one broad enumerability defect to every path.

| Case | Node reference | Interpreted API and CLI | Compiled API, default/builtin-typed standalone and actual hosted result | Disposition |
| --- | --- | --- | --- | --- |
| Array length descriptor and predicate | Descriptor `2 true false false`; predicate/keys `false 0,1` | Correct descriptor; predicate/keys `true 0,1` | Correct descriptor; predicate/keys `true 0,1` | Current predicate-only discrepancy in both engines; #1951. Object.keys correctly omits length. |
| Boxed string `Object('ab')` length | `2 false false false\nfalse 0,1\n` | Matches | Matches in every path | Passing boxing control; no repair of the missing historical string source is claimed. |
| Primitive string length descriptor then predicate | `2 false false false\nfalse 0,1\n` | Matches | API and hosted execution throw `TypeError: Cannot read properties of undefined (reading 'value')`; both standalone executions hit the unchanged 30-second budget | Descriptor lookup does not supply the expected object; #1953. The later predicate cannot be assessed through this failing source. |
| Primitive string predicate without descriptor lookup | `false 0,1\n` | Matches | `true 0,1\n` | Independent compiled predicate failure; #1954. Interpretation and boxed-string control pass. |
| Promise resolve/reject callback name | Empty name, writable=false, enumerable=false, configurable=true; predicate=false and empty keys for each callback | Matches | Correct descriptors and empty Object.keys; predicate=true for both | Compiled predicate-only failure; #1952. |

The primitive descriptor failure was observed in two bounded collections. The first raw
report is retained locally as `artifacts/issue-1919-1925/first-string-observation.json`;
the committed current report preserves repeated API/hosted exceptions and standalone
timeouts. A timeout is not a successful guest execution or proof of a semantic infinite
loop. The independently executed predicate-only program isolates the other string failure
without relaxing the original deadline or replacing the failed descriptor program.

## Duplicate checks and verification

Open/closed length/descriptor/enumerability and Promise-name searches find older wrapper
enumeration work (#475), but no exact current fix issue before #1951–#1954. Local
predicate/descriptor history and the fresh outputs preserve the difference between boxed
and primitive string forms. Each destination has one finite behavior contract. A descriptor
fix need not also correct the separate predicate; neither is masked by a passing IL check.

Product baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows, SDK 10.0.401/runtime
10.0.12, TypeScript 7.0.2, Node 25.5.0; collected October 2, 2026 (America/Los_Angeles).
[results.json](issue-1925/results.json) records actual collection HEAD, hashes, all typing
and execution results, deployment metadata and the 30-second operation budgets. Earlier
branch commits change evidence only. Only the absolute repository prefix is normalized
to `<repository>`; raw results remain under `artifacts/issue-1919-1925/run-1925`.

All fifteen default/standalone/hosted compilations pass IL verification. None references or
co-locates SharpTS.dll; hosted output adds the hosting ABI. API/hosted exceptions, standalone
timeouts and incorrect predicate outputs remain failures despite those valid assemblies.
The Release collector build, 297 focused regressions and repository code-quality gates pass.
Original fixtures/assertions, historical budgets and runtime implementation remain unchanged.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1925 artifacts/issue-1919-1925/run-1925
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1925
```
