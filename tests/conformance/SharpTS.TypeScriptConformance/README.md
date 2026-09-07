# SharpTS TypeScript Conformance Runner

Runs SharpTS's type checker against the canonical [microsoft/TypeScript](https://github.com/microsoft/TypeScript) conformance corpus and diffs our diagnostics against `tsc`'s `*.errors.txt` baselines. Mirrors the shape of `tests/conformance/SharpTS.Test262/`.

The diagnostic-parity campaign in [#1281](https://github.com/nickna/SharpTS/issues/1281)
is complete. Per-node inferred-type baseline work is tracked separately in
[#88](https://github.com/nickna/SharpTS/issues/88).

## Pinned TypeScript version

The corpus is vendored as a git submodule at `external/typescript/`, pinned to **`v6.0.3`**. TypeScript rewords its diagnostic messages between versions, so the pin is load-bearing for baseline stability — bumping it is intentional, not incidental.

The exact revision is **`050880ce59e30b356b686bd3144efe24f875ebc8`**. The gate
acquires only this submodule, verifies its HEAD against the repository gitlink and
the committed baseline header, rejects modified/missing tracked or untracked input
files, and checks the reference package version is `6.0.3`. Reference diagnostics
are TypeScript's checked-in `tests/baselines/reference/*.errors.txt` at that same
revision. No live `tsc`, npm installation, or Node runtime is needed; CI must not
regenerate those reference outputs. .NET is selected through the root `global.json`.

## Bounded CI gate

From the repository root, in PowerShell 7:

```powershell
./scripts/test-typescript-conformance.ps1
```

This is the same command used by CI. It acquires the pinned corpus, builds the
Release harness, runs its gate-policy tests and the baseline fact, and requires a
fresh successful baseline summary and a non-empty TRX result. Other corpus facts
(including the broad inferred-type parser sweep) are excluded by an explicit
test filter so additions to those suites cannot silently expand the smoke budget.
`-NoBuild` reuses a Release build; `-NoAcquire` validates an existing clean checkout
without fetching it. `-ResultsDirectory <path>` relocates artifacts.

The `typescript-conformance` job runs once on Ubuntu on the existing `full` CI
route, for both pull requests and eligible main pushes. The aggregate `Gate`
requires it to succeed. Documentation-only and proven C# trivia-only changes
retain their existing routes and skip this job; the classifier and push path
filters are unchanged. Harness/config/baseline/script/workflow changes select
the full route. Test262 execution remains owned by #1280.

`config/smoke.json` explicitly selects **32 files** from the existing
`baselines/interpreted.txt`; there is no separate smoke baseline. Selected paths
must exist and appear in the committed baseline. Coverage includes:

| Area | Representative cases |
|---|---|
| Parser and recovery diagnostics | `invalidTaggedTemplateEscapeSequences`, `inferTypesInvalidExtendsDeclaration`, `jsxParsingError1`, `jsxUnclosedParserRecovery` |
| Type relationships and inference | tuple/array and optional call signatures, conditional and mapped types, `keyof`/indexed-access valid and error cases, union/intersection/literal/`this` types |
| Declarations and control flow | classes, functions, interfaces with call/construct signatures, generic base types, aliases, enums, decorators, assignment narrowing and `if` flow |
| Libraries and modern syntax | symbols, Object values/entries, Promise finally, bigint library selection, logical assignment |
| JSX and programs | tuple children, generic tag inference, ambient global modules and multi-file export-as-namespace |

Every case retains the harness's `(line, TSnnnn)` diagnostic matching policy.
The gate fails on regressions, new passes, any other bucket/skip-reason changes,
unbaselined selections, or removed entries in a full run. It also fails for a
missing/empty baseline, missing corpus/files/folders, an empty selection, or a
run without meaningful checker comparisons. Both expected-error and valid-input
cases must execute. Baseline-update mode is explicitly rejected by the gate.

The smoke fact has a **120-second total execution budget**, with the existing
**5-second per-case timeout**. A timeout stops the gate; it does not keep launching
checks alongside an abandoned checker task. VSTest has a **3-minute hang timeout**
without memory dumps, the acquisition/build/test step has an **8-minute ceiling**,
and the job has **10 minutes** including artifact upload. These are ceilings, not
performance assertions; normal runs should finish well below them.

Local verification on Windows with .NET 10.0.400 (2026-09-07) measured the smoke
comparison at 6.7 seconds and the broader 534-case comparison at 73.5 seconds;
both matched the committed baseline. The smoke includes 21 expected-diagnostic
inputs and 11 valid inputs. Hosted-runner timings will differ.

CI always uploads the `typescript-conformance` artifact for seven days. Each run
has its own directory containing:

- `inputs.json`: corpus SHA, reference version, SDK and selected profile.
- `cases.jsonl`: every completed case, old/new bucket, and expected/actual diagnostic tuples; flushed after each case.
- `diagnostic-diff.txt`: changed cases with missing/extra diagnostics and error messages, plus removed entries.
- `summary.json`: completed comparison, counts, elapsed time and all baseline changes.
- `conformance.trx` and `test.log`: test failures and stack traces; `failure.txt` also explains setup or process failures.

An interrupted run may have only partial artifacts. Absence of a fresh completed
summary is itself a gate failure; stale artifacts cannot satisfy a later run.

### Deliberate regression proof

The gate was verified against an actual checker mutation, with the committed
corpus and baseline untouched. In a disposable checkout, temporarily change
`TypeChecker.GetDiagnostics()` in `src/SharpTS/TypeSystem/TypeChecker.cs` to return
`[]`, then run:

```powershell
pwsh -NoProfile -File scripts/test-typescript-conformance.ps1 -NoAcquire `
  -ResultsDirectory artifacts/typescript-regression
$LASTEXITCODE # 1
```

The measured result was **12 Pass, 20 Fail**, with the baseline fact failing and
all 16 gate-policy test cases passing. `summary.json` recorded `passed: false`
and 20 `NewRegressions`. One excerpt from `diagnostic-diff.txt`:

```text
tests/cases/conformance/es2019/globalThisAmbientModules.ts: Pass -> Fail
baseline expected 2, got 0; missing: [TS2339@L8, TS2339@L11]
  expected: [{"Line":8,"TsCode":"TS2339"},{"Line":11,"TsCode":"TS2339"}]
  actual:   []
```

That entry also contains the missing `(line, TSnnnn)` tuples and empty actual
diagnostics. Restore the original checker and rebuild before running again;
`-NoBuild` must not be used after changing or restoring the checker. The normal
32- and 534-case baselines pass without changing any committed expectations.

## Broader manual baseline run

```powershell
./scripts/test-typescript-conformance.ps1 -Profile full
```

This uses `config/subset.json` and compares all **534 committed cases**, including
selection additions/removals, with the same acquisition, reporting and baseline
policy. Its fact budget is ten minutes, per-case timeout five seconds, and VSTest
hang timeout twelve minutes. Run it before broad checker refactors, corpus or
selection updates, and during manual baseline reviews. It stays off the normal
PR route. Run the standalone project without the script to include all harness
unit and inferred-type infrastructure tests as well.

## Initial setup

```bash
git submodule update --init external/typescript
```

The TypeScript repo contains paths that exceed Windows' default 260-character `MAX_PATH`. On Windows you'll need long-path support enabled globally before initializing the submodule:

```bash
git config --global core.longpaths true
```

## Running locally

This project is **not** included in `SharpTS.sln`. Solution-level `dotnet build`
and `dotnet test` won't pick it up; CI invokes the bounded gate separately. To run
all harness tests locally, invoke explicitly:

```bash
dotnet test tests/conformance/SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj
```

The configured subset covers type relationships, conditional, `keyof`, indexed
access, alias, mapped, union, intersection, literal, and `this` types; symbols;
classes; type parameters; functions; interfaces; expressions; control flow;
enums; decorators; modern ECMAScript libraries; and representative TSX inputs.
It builds each multi-file test as a program, diffs SharpTS diagnostics against
`tsc`'s `*.errors.txt` baseline, and compares the bucket distribution against the
committed baseline at `baselines/interpreted.txt`.

### Verified snapshot (2026-08-27)

At TypeScript `050880ce59e30b356b686bd3144efe24f875ebc8`, the selected
subset contains 534 tests, all `Pass`, with zero parser failures,
checker/harness errors, failures, or skips. The pass rate is **100.00%**. Fourteen of the
tests are explicit high-signal files used to expand coverage without pulling an
entire large corpus directory into one untriaged rollout.

Set `SHARPTS_TSCONFORMANCE_DUMP_FAILURES=1` to print every failing test's
missing and extra `(line, TSnnnn)` tuples. Use this before implementing a
diagnostic-parity cluster so the triage reflects the currently pinned corpus.

## Updating the baseline

After an intentional change (new feature, fixed parser bug, refined diagnostic), regenerate the committed baseline:

```bash
SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1 dotnet test tests/conformance/SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj
```

Same shape as `SHARPTS_TEST262_UPDATE_BASELINE=1`. Commit the regenerated `baselines/interpreted.txt` alongside the change so reviewers see what shifted.
Sandboxed runners can additionally set
`SHARPTS_TSCONFORMANCE_BASELINE_OUTPUT` to a writable artifact path, then
copy that generated file into `baselines/interpreted.txt`. Set it to `-` when
the test host has no writable filesystem; entries are emitted to test output
with a `baseline-entry:` prefix and the versioned header with a
`baseline-header:` prefix.

## Bucket model

Each test classifies into one of:

| Bucket | Meaning |
|---|---|
| `Pass` | Diagnostic set matches the baseline (or both empty). |
| `Fail` | Diagnostic set differs from the baseline. |
| `ParseError` | Source failed to lex or parse before the type checker ran. |
| `TypeCheckError` | Checker threw something unrecoverable — distinct from "checker found errors." |
| `Skipped` | Skipped per directive policy or explicit by-path skip. |
| `HarnessError` | Setup error: couldn't read test, baseline parse failed, etc. |

`Skipped` carries a reason suffix (`Skipped:directive:usedefineforclassfields`,
`Skipped:explicitly-skipped`) so
the diff harness can tell different skip causes apart.

## Baseline file contract

The committed baseline is consumed outside this repository, including by the
`sharpts-www` conformance explorer. Its first and only comment line has this
machine-readable shape:

```text
# SharpTS baseline-format=1 suite=TypeScript corpus=<40-character-git-sha> — ...
```

Every remaining non-empty line is `<test-path> <Bucket[:reason]>`. Paths contain
no spaces. The closed bucket vocabulary is `Pass`, `Fail`, `ParseError`,
`TypeCheckError`, `HarnessError`, and `Skipped:<reason>`. Any format or
vocabulary change must bump `baseline-format`; consumers must reject versions
and buckets they do not know.

Official aggregation uses every result except `Skipped:*` in the denominator.
Only `Pass` contributes to the numerator. All other non-skipped buckets count as
not passing, while skipped results are reported separately and never folded into
the percentage.

## Match strategy

Diagnostics match on `(line, tsCode)` tuples. Column is intentionally dropped — TS rewords messages and column drift is endemic. The `tsCode` field on every type-checker diagnostic comes from the work in [#95](https://github.com/nickna/SharpTS/issues/95): each `throw new TypeCheckException(...)` site in `TypeSystem/` is tagged with the closest canonical `TSnnnn` code.

Diagnostics with no `tsCode` (SharpTS-only — e.g. `@DotNetType` integration errors) are excluded from baseline matching for that test rather than forcing a fail.

## Inferred-type baseline infrastructure

Phase 1 of [#88](https://github.com/nickna/SharpTS/issues/88) provides the
non-semantic infrastructure for the future `*.types` track:

- `TypeScriptBaselineResolver` selects plain or target/module-configured
  `.errors.txt` and `.types` files through the same deterministic algorithm.
  A missing compatible file is a typed `NoBaseline` result; unsupported
  harness axes are not guessed, and equally specific matches are reported as
  ambiguous.
- `TypesBaselineParser` reads the pinned writer format into ordered,
  source-backed observations containing virtual filename, one-based source
  line, source-line text, observed node text, occurrence ordinal, expected type
  text, and optional underline metadata.
- The parser receives the metadata parser's virtual source files as the
  coordinate authority. This distinguishes real blank source lines from blank
  separators inserted only for baseline readability.

This infrastructure does not yet produce SharpTS observations or compare
inferred types. Those semantic query, display, walking, and pilot-runner steps
remain later phases of #88. Neither parser nor resolver invokes `tsc` at test
time.

## JavaScript inputs

Selected tests that opt into `@allowJs` / `@checkJs` are measured. Their
virtual `.js` and `.jsx` roots use the same parser, resolver, and checker as the
SharpTS CLI; this diagnostics-only runner does not emit JavaScript. JavaScript-
specific semantic gaps remain visible as `Fail` instead of being skipped.

## Library selection

The runner loads the compiler's embedded copy of the pinned TypeScript
distribution's `lib.*.d.ts` graph from `src/SharpTS/Modules/TypeScriptLibResources`,
including triple-slash library references. `@lib`, `@target`, `@noLib`,
declaration-file roots, and visible `@types` packages flow through the same
program resolver as the CLI. Missing-surface diagnostic differences are
measured as `Fail`; the pinned lib graph makes them actionable checker or
resolver gaps rather than harness skips.

## Configuration

| File | Purpose |
|---|---|
| `config/subset.json` | Folders and explicit files to enumerate, per-test timeout, paths to skip-files. Explicit files support small, reviewable coverage rollouts. |
| `config/skip-directives.txt` | Directive names (lower-cased) whose presence in a test's `// @<key>: <value>` header short-circuits the run as `Skipped:directive:<name>`. |
| `config/skip-tests.txt` | Test paths (relative to the conformance corpus root) to wholesale skip. Escape hatch for tests that crash the runner. |

## Layout

| Path | Purpose |
|---|---|
| `external/typescript/` | Vendored TS repo (submodule, pinned to v6.0.3) |
| `external/typescript/tests/cases/conformance/` | The conformance corpus (~10–15k `.ts` files) |
| `external/typescript/tests/baselines/reference/` | `tsc`'s `*.errors.txt` / `*.js` / `*.types` baselines |
| `external/typescript/src/lib/` | Embedded `lib.es*.d.ts`, `lib.dom.d.ts`, and WebWorker declaration inputs |
| `tests/conformance/SharpTS.TypeScriptConformance/baselines/interpreted.txt` | Our committed baseline |

## See also

- `tests/conformance/SharpTS.Test262/` — equivalent project for the ECMA-262 / JavaScript spec; this one mirrors its harness shape.
- [#1281](https://github.com/nickna/SharpTS/issues/1281) — completed diagnostic-parity campaign.
- [#88](https://github.com/nickna/SharpTS/issues/88) — per-node inferred-type baseline parity.
