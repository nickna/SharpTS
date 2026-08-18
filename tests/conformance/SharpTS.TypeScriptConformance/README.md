# SharpTS TypeScript Conformance Runner

Runs SharpTS's type checker against the canonical [microsoft/TypeScript](https://github.com/microsoft/TypeScript) conformance corpus and diffs our diagnostics against `tsc`'s `*.errors.txt` baselines. Mirrors the shape of `tests/conformance/SharpTS.Test262/`.

Standing tracking issue: [#1281](https://github.com/nickna/SharpTS/issues/1281).

## Pinned TypeScript version

The corpus is vendored as a git submodule at `external/typescript/`, pinned to **`v6.0.3`**. TypeScript rewords its diagnostic messages between versions, so the pin is load-bearing for baseline stability — bumping it is intentional, not incidental.

## Initial setup

```bash
git submodule update --init external/typescript
```

The TypeScript repo contains paths that exceed Windows' default 260-character `MAX_PATH`. On Windows you'll need long-path support enabled globally before initializing the submodule:

```bash
git config --global core.longpaths true
```

## Running locally

This project is **not** included in `SharpTS.sln`. Solution-level `dotnet build` and `dotnet test` (what CI runs) won't pick it up. Invoke explicitly:

```bash
dotnet test tests/conformance/SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj
```

The configured subset covers type relationships, conditional types, symbols,
modern ECMAScript libraries, and representative TSX inputs. It builds each
multi-file test as a program, diffs SharpTS diagnostics against `tsc`'s
`*.errors.txt` baseline, and compares the bucket distribution against the
committed baseline at `baselines/interpreted.txt`.

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
| `Skipped` | Skipped per directive policy, lib-drift filter, or explicit by-path skip. |
| `HarnessError` | Setup error: couldn't read test, baseline parse failed, etc. |

`Skipped` carries a reason suffix (`Skipped:lib-drift`,
`Skipped:directive:experimentaldecorators`, `Skipped:explicitly-skipped`) so
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

## Library selection

The runner loads the compiler's embedded copy of the pinned TypeScript
distribution's `lib.*.d.ts` graph from `src/SharpTS/Modules/TypeScriptLibResources`,
including triple-slash library references. `@lib`, `@target`, `@noLib`,
declaration-file roots, and visible `@types` packages flow through the same
program resolver as the CLI. The conservative legacy `lib-drift` skip remains
for expected missing-surface diagnostics that produce no SharpTS diagnostic.

## Configuration

| File | Purpose |
|---|---|
| `config/subset.json` | Folders to enumerate, per-test timeout, paths to skip-files. |
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
- [#1281](https://github.com/nickna/SharpTS/issues/1281) — standing TypeScript conformance work,
  corpus growth, and diagnostic-alignment tracking.
