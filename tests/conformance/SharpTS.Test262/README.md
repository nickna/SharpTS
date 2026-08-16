# SharpTS Test262 Runner

Runs SharpTS against the canonical [TC39 Test262](https://github.com/tc39/test262) ECMA-262 conformance suite in both interpreter and compiled-IL modes, diffing outcomes against committed baselines. The TS-conformance equivalent lives in `tests/conformance/SharpTS.TypeScriptConformance/`.

## Initial setup

```bash
git submodule update --init external/test262
```

## Running locally

This project is **not** included in `SharpTS.sln` and won't be picked up by solution-level `dotnet test`. Invoke explicitly:

```bash
dotnet test tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj
```

The default subset (config/subset.json) keeps a runtime budget of a few minutes. The wide-sweep config (config/wide-sweep.json) exercises a much larger slice and writes gitignored path-level snapshots plus a differential markdown report — useful for periodic deep checks.

## Updating the baselines

```bash
SHARPTS_TEST262_UPDATE_BASELINE=1 dotnet test tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj
```

Writes `baselines/interpreted.txt` and `baselines/compiled.txt`. Commit the regenerated files alongside the change so reviewers can see what shifted.

```bash
SHARPTS_TEST262_WIDE_SWEEP=1 dotnet test tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj
```

Switches to the wide-sweep config and writes `wide-sweep-baselines/{interpreted|compiled}.txt`. Once both modes finish against the same Test262 revision, it also writes `wide-sweep-report.md`. These artifacts are gitignored and the run is long-running.

## Comparing interpreter and compiler baselines

Generate the issue #1279 differential report directly from the two committed
baselines; this mode does not execute Test262:

```bash
SHARPTS_TEST262_DIFFERENTIAL_REPORT=1 dotnet test tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj \
  --filter FullyQualifiedName=SharpTS.Test262.Test262DifferentialReportModeTests.Generate_from_committed_baselines
```

The command writes `tests/conformance/SharpTS.Test262/differential-report.md` with per-mode pass
rates, the interpreted-to-compiled transition histogram, Track A interpreter
deficits, Track B compiler deficits, other divergent outcomes, and paths found
in only one baseline. It also warns when the baseline headers pin different
Test262 corpus revisions.

## Bucket model

| Bucket | Meaning |
|---|---|
| `Pass` | Test body completed without an assertion throwing. |
| `Fail` | Test body threw a `Test262Error` (assertion failed). |
| `ParseError` | Source (or assembled harness) failed to lex/parse. |
| `TypeCheckError` | Static type checker rejected the source. |
| `RuntimeError` | Test body threw something other than `Test262Error`. |
| `Timeout` | Execution exceeded the per-test deadline. |
| `HarnessError` | Harness code (sta.js / assert.js / includes) threw before the test body ran. |
| `Skipped` | Intentionally not run (negative test, deferred feature, skip-list match). |

Skip reasons are appended to the bucket (`Skipped:async-done-deferred`) so the diff harness can tell different skip causes apart.

## Baseline file contract

The committed baselines are consumed outside this repository, including by the
`sharpts-www` conformance explorer. The first and only comment line has this
machine-readable shape:

```text
# SharpTS baseline-format=1 suite=Test262 corpus=<40-character-git-sha> — ...
```

Every remaining non-empty line is `<test-path> <Bucket[:reason]>`. Paths contain
no spaces. The closed bucket vocabulary is `Pass`, `Fail`, `RuntimeError`,
`ParseError`, `TypeCheckError`, `Timeout`, `HarnessError`, and
`Skipped[:reason]`. Any format or vocabulary change must bump
`baseline-format`; consumers must reject versions and buckets they do not know.

Official aggregation uses every result except `Skipped:*` in the denominator.
Only `Pass` contributes to the numerator. All other non-skipped buckets count as
not passing, while skipped results are reported separately and never folded into
the percentage.

## Layout

| Path | Purpose |
|---|---|
| `external/test262/` | Vendored Test262 repo (submodule, shallow) |
| `config/subset.json` | Default subset: folders to run, per-test timeout, skip-features file |
| `config/wide-sweep.json` | Larger periodic-sweep config; writes a report instead of diffing |
| `config/skip-features.txt` | Feature tags (`generators`, `Atomics`, `decorators`, ...) that cause a test to be skipped |
| `baselines/interpreted.txt` | Committed baseline for interpreter mode |
| `baselines/compiled.txt` | Committed baseline for compiled-IL mode |
| `differential-report.md` | Generated interpreter↔compiler parity report (not committed) |
| `wide-sweep-baselines/` | Generated path-level snapshots for the wide interpreted and compiled sweeps (not committed) |
| `wide-sweep-report.md` | Generated wide-sweep interpreter↔compiler differential report (not committed) |

## See also

- `tests/conformance/SharpTS.TypeScriptConformance/` — equivalent for the TypeScript conformance corpus.
