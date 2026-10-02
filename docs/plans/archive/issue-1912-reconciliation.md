# Console restoration evidence (#1912)

The first-invocation failure remains reproducible and is transferred to
[#1930](https://github.com/nickna/SharpTS/issues/1930). It concerns raw test-proxy
normalization; normal public Console writer restoration succeeds. This is an evidence
reconciliation, not a harness or product repair.

## Recovered observation

Frozen #1599 L155 and L509–511 and [PR #1817](https://github.com/nickna/SharpTS/pull/1817)
retain the original `CompilationServiceTests.Execute_RestoresConsoleAfterRun` failure:
2,950/2,951 broad-subset cases passed, while the full 22,597-case selection passed.
The unchanged committed method captures `Console.Out`/`Console.Error`, compiles
`console.log("x");`, executes it with a StringWriter, and asserts both original identities.
The original failed logs were not recovered from the linked PR comments or the retained
`D:/nickna/SharpTS/artifacts` and `.perf-runs` locations. The frozen diagnostic and
committed original method are recoverable; a successful full-suite run does not erase
the isolated failure. No numerical per-case deadline is recorded for that synchronous method.

## Current status

Baseline `e622d0c88f9133d10ce6809082a3085e759fb42b` (GitHub main on October 1, 2026,
America/Los_Angeles); Windows, SDK 10.0.401, runtime 10.0.12.

The original isolated xUnit test fails at its stdout `Assert.Same`, with expected
`ProxyWriter` and actual `SyncTextWriter`. A fresh-process collector invokes that exact
test twice without changing its assertions. First invocation fails and changes **both**
stdout/stderr identities; second invocation passes and preserves both. Public-installed
StringWriter controls execute twice successfully, each producing `x\n`, and preserve
both writer identities. [results.json](issue-1912/results.json) retains the actual results.

`AsyncLocalConsoleRedirector.Install` bypasses public Console methods by writing raw
proxies to private fields. `CompilationService.Execute` uses public setters for capture
and restoration. Their synchronization wrapper explains the recorded identity change.
The normal public-writer controls support restoration correctness for that product path;
they do not certify arbitrary private-field writer installation or concurrent tenants.
Interpreter, CLI/standalone and hosted ABI paths do not exercise this particular
`CompilationService.Execute` restoration contract and are not substituted for it.

The open/closed issue inventory and local history contain no exact raw-proxy repair
before #1930. #1816/#1817 preserved the failure; #1862 changed deadline supervision,
not these setters or assertions. #1930 owns the remaining test/harness contract work.

## Reproduce

```powershell
dotnet build tests/fixtures/SharpTS.HistoricalEvidence/SharpTS.HistoricalEvidence.csproj -c Release
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll console artifacts/issue-reconciliation/console-results.json
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Execute_RestoresConsoleAfterRun'
```

The last command is expected to report the retained defect, not pass. The collector's
zero exit status means collection completed, not that every recorded result is correct.
Both Release builds passed (existing NU1902 warnings). No original assertion, filter,
workload or deadline was weakened. The JSON replaces only the local repository prefix
with `<repository>`; its raw counterpart is in `artifacts/issue-reconciliation`.
