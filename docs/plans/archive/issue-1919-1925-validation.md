# Consolidated reconciliation validation (#1919–#1925)

These seven tasks reconcile historical evidence. They do not implement the outstanding
runtime fixes, reopen completed metadata ownership migrations or alter original assertions.
Each task has its own commit, recovered/fresh source manifest, reference compilation results,
current execution report, frozen excerpts and explicit supported disposition.

| Task | Result |
| --- | --- |
| #1919 | Recovered Boolean/Number aliases still fail; #1944/#1945. BigInt deduplicates to #1942. Reserved primitive import remains a resolver rejection. |
| #1920 | Recovered getter/setter failures; #1946/#1947. New top-level CLI/hosted strict failure; #1948. Original top-level source remains unavailable. |
| #1921 | Both unnamed exploratory failures lack original programs/results; preserved as evidence gaps. Separate species and exec controls pass. |
| #1922 | Original callback source/assembly/full diagnostic unavailable; DelegateCtor at offset 199 and expected historical execution remain preserved. A distinct callback control passes. |
| #1923 | Original exploratory identities unavailable. New custom-brand failure transfers to #1949; recovered committed combined accessor control passes. |
| #1924 | Recovered symbol delete/reinsert sequence still fails when compiled; #1950. |
| #1925 | Original exploratory sources unavailable. New array/Promise predicates and primitive-string descriptor/predicate failures split into #1951–#1954; boxed-string control passes. |

All current product execution uses baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`.
The evidence branch changes no product/test behavior. Each report records its actual
collection HEAD, source hashes and time; differences in evidence-only commits do not change
that product baseline. Collection was October 2, 2026 (America/Los_Angeles), Windows,
.NET SDK 10.0.401/runtime 10.0.12, TypeScript 7.0.2 and Node 25.5.0.

## Executed checks

- Release build of `SharpTS.HistoricalEvidence`: passed. Existing NuGet advisory NU1902
  for Microsoft.Build.Tasks.Git 8.0.0 remains unchanged.
- Nineteen source-bound cases: eighteen successful Node references and one intended
  reserved-import rejection. Sixteen TypeScript reference compilations pass; CommonJS
  aliases run directly, and the SharpTS-specific primitive import is not given a fabricated
  TypeScript/Node success expectation.
- All API, three interpreted CLI typing modes, default/builtin-typed standalone compilation
  and execution, and actual hosted initialization records are retained. Primitive import
  fails before module emission. Primitive-string descriptor execution throws in-process/
  hosted and times out in both isolated standalone paths; it is not a passing execution.
- Fifty-four CLI assemblies pass IL verification. No standalone/hosted assembly references
  or co-locates SharpTS.dll; hosted metadata adds SharpTS.Hosting.Abstractions. Their runtime
  outputs/exceptions/timeouts are assessed separately from verification success.
- All 297 focused Boolean/coercion/object storage/RegExp/Error/prototype/key/predicate owners,
  computed accessor cases and the original interpreter-only symbol-order assertion pass,
  with zero failures/skips. Existing assertions and numerical deadlines remain untouched.
- New evidence verifier checks all source/reference/report hashes, frozen ledger bindings,
  required observation records, successful verification records and deployment boundaries.
- Nine verifier runs pass as intended: original/restored records plus seven mutation rejection
  checks for erased alias failures/timeouts, changed Node references, absent IL proof/hosted
  execution, unexpected SharpTS deployment and relaxed deadlines. Schema checks are tested
  after refreshing the mutated copy's checksum so a checksum rejection cannot mask them.
- Existing #1912–#1918 evidence verifier passes: 19 programs and 54 retained IL checks.
  Frozen reconciliation verifier still covers 459 original source lines, 18 comments,
  63 semantic destinations, 26 supersession candidates and three original #1805 sources.
- Repository code-quality gates and analyzer/duplicate/dead-emitter mutation fixtures pass:
  28 existing approved duplicate groups, zero errors. `git diff --check` passes.

Run from the repository root. Export the issue body, comments and URL before the frozen
coverage check; the current response matches the frozen body and original eighteen comments.
That verifier checks their recorded hashes and intentionally rejects later edits. If the
issue changes, use a retained original response at the same repository-relative path.

```powershell
New-Item -ItemType Directory -Force artifacts/issue-1919-1925 | Out-Null
gh issue view 1599 --repo nickna/SharpTS --json body,comments,url | Set-Content -Encoding utf8 artifacts/issue-1919-1925/issue-1599.json
dotnet build tests/fixtures/SharpTS.HistoricalEvidence/SharpTS.HistoricalEvidence.csproj -c Release
foreach ($issue in 1919..1925) {
    dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases "docs/plans/archive/issue-$issue" "artifacts/issue-1919-1925/run-$issue"
}
pwsh scripts/verify-reconciled-evidence.ps1
pwsh scripts/test-reconciled-evidence.ps1
pwsh scripts/verify-historical-evidence.ps1
pwsh scripts/verify-1599-reconciliation.ps1 -IssueJson artifacts/issue-1919-1925/issue-1599.json
pwsh scripts/test-code-quality.ps1
```

The collector exit code means collection completed; it does not certify conformance.
LF checkout rules preserve the new records' byte hashes. Repository and home-directory path
prefixes are normalized to `<repository>` and `<home>` in committed reports; raw logs/binaries
remain in ignored artifacts. Historical
evidence gaps, verifier failures and deadlines are not replaced with passing fresh controls.
