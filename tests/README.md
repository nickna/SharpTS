# Testing SharpTS

Tests should prove an observable contract and fail for unexpected exceptions. A high
coverage percentage cannot establish language correctness or the validity of emitted IL.
Keep the dual-mode semantic tests, optimization rejection cases, injected-failure tests,
standalone artifact tests and independent conformance oracles.

## Standard commands

Run from the repository root with PowerShell 7 and the SDK selected by `global.json`:

```powershell
./scripts/invoke-tests.ps1                         # Fast: Unit layer
./scripts/invoke-tests.ps1 -Suite Full             # All hermetic core tests
./scripts/invoke-tests.ps1 -Suite Full -Layer Semantics,CompilerContracts
./scripts/invoke-tests.ps1 -Suite Full -NoBuild    # After a Release build
./scripts/invoke-tests.ps1 -Suite Coverage         # Unit tests, isolated instrumented build
./scripts/test-test-reporting.ps1                 # Selection/reporting contract checks
```

Every run writes TRX, JSON/Markdown counts and timing summaries, detailed test output,
and a log under a unique `artifacts/tests/` directory. Zero discovered or executed cases
is an error. Test durations are per-case durations, not elapsed wall time; concurrent
durations must not be added to estimate suite latency. The full command covers the core
project; GUI, packaging, native AOT and corpus conformance remain separate CI gates.

`LiveNetwork`, `LoadSensitive` and `npm` remain orthogonal opt-in categories excluded by
these commands. Select them deliberately with `dotnet test --filter Category=...`.

## Layers and placement

`test-layers.json` is the core suite's run-grouping contract. The most specific namespace
default wins, then an exact type override takes precedence. The trailing dot in generated
filters prevents similarly named classes from leaking between selections. Architecture
tests enforce that all discovered test classes are classified and no stale rules remain.
New files inherit their namespace's layer; update the map when their scope differs.

| Layer | Contract | Typical home |
| --- | --- | --- |
| Unit | Components, diagnostics, runtime primitives and tooling services; no child processes or sockets | ParserTests, TypeCheckerTests, RuntimeTests, Modules, LanguageServer |
| Semantics | Source-to-output behavior, usually interpreted and compiled using ModeData | SharedTests and InterpreterTests |
| CompilerContracts | Lowering, emitted IL, optimization acceptance and fallback | CompilerTests |
| Integration | Processes, standalone artifacts, filesystem/host lifecycle, sockets and protocols | IntegrationTests, Hosting and resource-based overrides |
| Architecture | Dependencies, source policies and runtime/emitter synchronization | Architecture and BuildTests |
| Conformance | Pinned external semantic oracles and GUI contracts | Separate projects under conformance and gui-conformance |

Layers describe a class's primary contract; a checker acceptance test can also traverse
the lexer and parser. Small temporary-file fixtures are appropriate for file-oriented
components. These logical groups do not require one project per layer. Move tests
incrementally when touched. Group by feature and contract instead of numbered campaigns.
The Test262 `RuntimeConformanceTests.*` partial files preserve one serialized collection
while separating arrays, objects, strings, async/generators, language and cross-feature cases.
Keep issue references where they explain regression provenance.

Choose the narrowest layer that demonstrates the bug. Add a cross-mode, IL or deployment
test when that boundary caused the bug; do not duplicate a regression in every layer.
Prefer exact expected output or an independent vector over comparing an implementation
with itself. Successful differential-parity snippets must run successfully in both modes;
matching exception types is not successful parity. Expected-error contracts belong in
separate tests.

For checker tests, use `DiagnosticAssertions`: require parse success, expected diagnostic
code, severity and source line, and no unrelated diagnostics. Positive checker tests assert
an empty diagnostic list without executing the program. Broader runtime error tests still
belong in semantic tests. Malformed-input stress probes use a killable frontend fixture;
internal crashes and timeouts fail, and timed-out work cannot remain in the test host.

## Coverage

The routine coverage job measures the **Unit layer only** with four conservative xUnit
workers. It produces line and branch coverage by production assembly and source-line
coverage by subsystem. Its total is not full-suite coverage. `coverage.runsettings` includes
SharpTS production assemblies, excludes test/fixture assemblies and generated `obj` files,
and does not exclude compiler emitters or runtime implementation. Compiler/runtime code
unreached by unit tests remains visible as uncovered.

Coverage always builds under a fresh `--artifacts-path`. Never collect into normal build
output or reuse a canceled collector's binaries: instrumentation can alter subprocess
behavior and can remain behind after cancellation. Run the full hermetic suite separately
without instrumentation. Generated guest IL is not measured by managed-source coverage;
retain structural verification, standalone execution and conformance tests.

There is deliberately no global percentage gate yet. Use the first stable reports to
review uncovered changed branches, high-risk gaps and slow tests; introduce a reviewed
non-regression baseline only after repeatability is established.

## External conformance

```powershell
git -c core.longpaths=true submodule update --init external/test262 external/typescript
dotnet test tests/conformance/SharpTS.Test262 -c Release --filter 'Category!=Corpus&Category!=Diagnostic'
dotnet test tests/conformance/SharpTS.TypeScriptConformance -c Release --filter 'Category!=Corpus&Category!=Diagnostic'
dotnet test tests/conformance/SharpTS.Test262 -c Release --filter 'Category=CiSmoke'
./scripts/test-typescript-conformance.ps1 -NoAcquire
```

Harness-only tests need no external checkout. Corpus tests fail with setup instructions
when their inputs are missing; they never report a missing checkout as a pass. PR CI runs
harness tests on both platforms, the bounded Test262 `config/ci-smoke.txt` selection, and
the TypeScript baseline smoke gate on Linux. Every Test262 path must pass in both modes;
the TypeScript gate compares pinned reference diagnostics and publishes per-case diffs.
Gitlinks pin the corpus revisions; CI does not track upstream HEAD.

The `Conformance baselines` workflow runs the broader committed subsets weekly and on
manual dispatch. Baseline facts contain many corpus cases: their executed/skipped outcome
counts are published from test output separately from xUnit fact counts. Missing baselines,
empty selections and drift fail. Normal validation never regenerates a baseline. Set the
documented `SHARPTS_*_UPDATE_BASELINE=1` switch only for an intentional, reviewed update.
Manual differential/diagnostic investigation facts remain outside the harness-only gate.
