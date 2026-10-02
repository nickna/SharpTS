# Aliased Map.groupBy evidence reconciliation (#1911)

The retained discrepancy has a confirmed current counterpart: a `Map` constructor stored in an
`any` alias loses `groupBy` in compiled execution. It is transferred to
[#1928](https://github.com/nickna/SharpTS/issues/1928) for implementation.
This reconciliation changes no compiler or interpreter behavior.

## Historical evidence and its limits

The frozen [#1599](https://github.com/nickna/SharpTS/issues/1599) body, updated
2026-09-27T18:53:55Z, has SHA-256
`420cdca4eaf29b19354bcce748ffa034f4cfaa3400e772fb2089b8d97978c15f`.
Its L339 records eighteen TypeScript 5.9.3 / Node 22.23.2 references, fourteen passing guest
executions, two hosted outputs, and **two pre-existing grouping observations that failed unchanged**.
L343 specifically retains an aliased `Map.groupBy` discrepancy and says its sources, expectations,
and deadlines were retained locally. It also records a rejected issue-publication attempt.
That rejection is administrative evidence; it says nothing about runtime correctness.

[PR #1809](https://github.com/nickna/SharpTS/pull/1809) repeats the count of two failing alias
observations, excluded from its successful execution counts. Its checked head was
`3fbc0cee91eec17b4f604b21b8981e085cc8f3cc`; merge commit
`ec4c72c52399d8e3990975017bba5d9db97a8feb` preserved that tree. The PR's body and comment do
not supply either failing program, diagnostic, output, source hash, individual identifier,
numerical deadline, or per-case reference result.

The recovery search covered the linked issue body and comments, PR #1809, its committed tests,
the archived reconciliation ledger, and the retained `D:/nickna/SharpTS/artifacts` and
`.perf-runs` evidence. The original frozen issue snapshot exists under
`artifacts/issue-1926/issue-1599.json` in that primary checkout. The retained feature inventories
and Array-operations mapping do not identify these Map observations. No original Map alias
program or output was recovered from those locations. This is a bounded search, not a claim
that the files never existed or cannot exist elsewhere.

| Historical observation | Disposition |
| --- | --- |
| First of the two unnamed failures in L339 / #1809 | Lacks recoverable individual source, diagnostic, expectation, and deadline in the searched evidence. The original failed status remains preserved. |
| Second of the two unnamed failures in L339 / #1809 | Same evidence gap; it cannot be identified as a distinct method alias, constructor alias, duplicate, or repaired case. |
| Retained aliased `Map.groupBy` discrepancy in L343 | Specific current constructor-alias defect established below and transferred for implementation. Exact identity with either unnamed historical case remains unproven. |

The separate current probes do not replace those missing original programs. In particular, the
passing method-alias probe does **not** establish that either historical failure was repaired.
No original expectation or deadline has been relaxed. The historical record says deadlines were
unchanged, but does not expose their values; current probes use the existing harness's 30-second
default and explicitly record that budget.

## Current execution evidence

Recorded baseline: `1e3c86f87ff8ae9b6dcc4656cb6a1a1af69bca0d`, also the GitHub `main` head
when checked on October 1, 2026 (America/Los_Angeles). Frozen decomposition baseline:
`83a41096108fe6de739fa7cfcb148c4bb1193121`. The intervening #1927 changes historical documentation
and tests, not runtime behavior. Tools: .NET SDK 10.0.401, TypeScript 7.0.2, Node 25.5.0 on Windows.
These versions describe the fresh run; they are not the original reference versions above.

The [direct source](issue-1911/direct.ts) is recovered verbatim from #1809's committed
`RuntimeClassPrograms` entry `map_group_by_direct`, including its final newline. The
[constructor alias](issue-1911/constructor-alias.ts) and
[detached method alias](issue-1911/method-alias.ts) are newly constructed, matched probes.
All three have the reference expectation `2\n`, with empty stderr and zero exit status where
execution is a subprocess. TypeScript separately compiles each source using ES2024 / DOM libraries;
all three emitted JavaScript programs produce that result in Node.

| Probe | Node | Interpreted API | In-process compiled API | Interpreted CLI | CLI-compiled standalone execution | Hosted ABI initialization |
| --- | --- | --- | --- | --- | --- | --- |
| Direct `Map.groupBy(...)` | `2\n` | `2\n` | `2\n` | `2\n` | `2\n` | `2\n` |
| Constructor alias `const M:any=Map; M.groupBy(...)` | `2\n` | `2\n` | TypeError | `2\n` | TypeError | TypeError |
| Detached `const groupBy=Map.groupBy; groupBy(...)` | `2\n` | `2\n` | `2\n` | `2\n` | `2\n` | `2\n` |

The three compiled failures report `$ThrownValueException: TypeError: undefined is not a function`.
Standalone output is empty, stderr contains that exception at `$Runtime.InvokeMethodValue`, and
the Windows exit code is `-532462766`. Hosted initialization actually executes the source through
`SharpTSHostedAssembly.CreateRuntime` and `InitializeAsync`; its task faults with the same guest
TypeError. Hosted success is not inferred from compilation or an empty output assertion.

All six CLI compilations succeed with `--verify --standalone`; **all six pass IL verification**.
The three standalone assemblies have only framework references. The hosted assemblies additionally
reference `SharpTS.Hosting.Abstractions`. None references or co-locates `SharpTS.dll`. Valid IL and
standalone metadata therefore coexist with the alias's incorrect runtime result.

[results.json](issue-1911/results.json) preserves source hashes, all current outputs, exceptions,
assembly references, versions, and the recorded deadline. Only the absolute repository prefix is
replaced with `<repository>` for portability; the raw artifact remains at
`artifacts/issue-1911/probes/results.json`. Its SHA-256 is
`0f03dce6a368a1b8dbb9855e5d1523e197df46bc9fdf7966220029b682494a6a`.
[historical.json](issue-1911/historical.json) preserves the frozen excerpts and their ledger hashes.

## Duplicate and repair check

GitHub searches across open/closed issues for `groupBy`, and PR searches for `Map.groupBy` and
`aliased Map`, found #1911, its parent/umbrella/conformance trackers, ownership PRs #1691/#1809,
and unrelated string-return/scope fixes. No exact constructor-alias fix issue was found before
publication. Local history for the Map emitter, interpreter built-in, and static-member dispatch
through the recorded baseline contains no later alias repair. The reproduced failure is the
decisive evidence that the current path remains broken.

The direct/detached compiler paths resolve `EmittedMapRuntime.GroupBy` through
`MapStaticEmitter`. The constructor alias uses runtime property lookup, whose
`BuiltInStaticDispatchInputs` / lookup table contains no Map component or `groupBy` mapping.
This is a source-based explanation consistent with the observed undefined callable, not a
claim that a proposed implementation has been validated.

## Reproduce and validation

The evidence runner is outside the default solution and collects failures without accepting them
as test expectations. Its zero exit code means collection completed; read the result records to
determine correctness. Build it, then run from the repository root:

```powershell
dotnet build tests/fixtures/SharpTS.MapGroupByEvidence/SharpTS.MapGroupByEvidence.csproj -c Release
dotnet tests/fixtures/SharpTS.MapGroupByEvidence/bin/Release/net10.0/SharpTS.MapGroupByEvidence.dll docs/plans/archive/issue-1911 artifacts/issue-1911/probes
foreach ($case in Get-ChildItem docs/plans/archive/issue-1911 -Filter '*.ts') {
    tsc --target ES2024 --lib 'ES2024,DOM' --outDir artifacts/issue-1911/tsc-reference $case.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Reference compilation failed' }
    node (Join-Path artifacts/issue-1911/tsc-reference ($case.BaseName + '.js'))
    if ($LASTEXITCODE -ne 0) { throw 'Reference execution failed' }
}
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~SharedTests.GroupByTests|FullyQualifiedName~Isolated_RuntimeClass_PreservesCrossFamilyCallsAndDeployment'
```

Release test-project and evidence-runner builds pass. The 16 shared GroupBy cases and 16 existing
RuntimeClass standalone/hosted controls all pass (32 tests, no failures/skips). The execution matrix
above records 15 correct results and three confirmed failures; six additional IL checks pass.
The code-quality/analyzer mutation gates pass with the unchanged 28 approved duplicate groups
and zero errors. The frozen ledger verifier still covers all 459 original lines and 18 comments;
the retained excerpt hashes and all three fixture source hashes also match.
No compiler fix, original-case repair, or surrounding Map-family conformance is claimed.
