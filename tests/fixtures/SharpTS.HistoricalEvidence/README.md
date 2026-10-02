# Historical evidence collector

This diagnostic executable is outside the default solution. It records failures as
observations; its zero exit code means collection completed, not conformance.
Reference expectations in each manifest remain separate from historical parity outputs.

```powershell
dotnet build tests/fixtures/SharpTS.HistoricalEvidence/SharpTS.HistoricalEvidence.csproj -c Release
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll console artifacts/issue-reconciliation/console-results.json
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll timing artifacts/issue-reconciliation/numeric-original.json
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1914 artifacts/issue-reconciliation/issue-1914
```

`console` must run in a fresh process to preserve the test module initializer's first
raw-writer invocation. `timing` recovers the pre-c85a5505 bitwise single-sample workload.
`cases` accepts a directory with `cases.json` (`File`, `Provenance`, `ExpectedStdout`) and
the referenced TypeScript/CommonJS files. It compares Node, builtin-typed APIs, three CLI
typing paths, default CLI standalone compilation/execution, IL-verified builtin-typed
standalone output, and actual hosted initialization. Each operation has a 30-second
collection deadline; API work uses the existing bounded test harness. Hosted execution
uses the existing deterministic host dispatcher, lifetime and error sink.

Case reports retain source hashes, actual current Git HEAD and runtime versions. Interpret
them alongside each reconciliation's historical-evidence limits. In particular Node
25.5.0 lacks Math.sumPrecise: its failure is retained, and that expected result comes from
the cited TC39 algorithm, not an invented Node success. All subprocess streams are read
concurrently. Output directories are caller-supplied; use git-ignored artifacts paths.

`pwsh scripts/verify-historical-evidence.ps1` checks the committed source hashes,
provenance, reference expectations and retained observation/deployment records without
rerunning guest programs or treating incorrect outputs as conformance assertions.
`pwsh scripts/test-historical-evidence.ps1` checks that altered copies of the records
fail verification, including an erased original runtime failure or unexpected default
compilation rejection. Timing mode creates its requested parent directory, so it can run
independently of console mode on a fresh output path.
