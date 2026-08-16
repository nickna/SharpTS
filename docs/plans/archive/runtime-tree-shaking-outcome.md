# Runtime tree-shaking outcome

This record consolidates the completed runtime tree-shaking plans, phase results, and audits. It
describes the durable architecture and outcome; transient measurements and implementation diaries
remain available in repository history.

## Outcome

Compiled assemblies no longer receive the entire emitted runtime unconditionally. The compiler
derives a conservative `RuntimeFeatureSet` from the typed program and uses it to gate emitted
runtime types, module adapters, helper families, and selected methods inside `$Runtime`.

The detector is intentionally biased toward over-emission. An uncertain construct keeps the
feature enabled, preserving correctness at the cost of size. Feature gates are paired with guarded
dispatch and runtime call sites so a removed helper cannot leave an invalid metadata reference.

## Stable design

1. `RuntimeFeatureDetector` analyzes the complete typed module graph.
2. `ILCompiler` carries the resulting feature set into `RuntimeEmitter` and other emitters.
3. Whole runtime types and method families are emitted only for enabled features.
4. Cross-feature helpers declare every consumer in their gate.
5. Synchronization tests compare feature detection, emitted members, and call sites to prevent a
   gate from producing an unloadable assembly.
6. The build separately records soft dependencies that require `SharpTS.dll`; tree-shaking does
   not turn a normal runtime dependency into an accidental missing dependency.

This structure covers uncommon Node modules, binary-data types, regular expressions, promises,
date/JSON helpers, text encoding, web APIs, and other optional runtime families. The exact catalog
is intentionally kept in source and tests rather than duplicated here.

## What the phases established

- Whole-type gating produced the first substantial size and load-time reductions.
- Central dispatch sites had to be conditional before their target types could be omitted.
- Per-method shaking inside `$Runtime` required group-based gates rather than independent ad-hoc
  conditions because helpers often serve multiple public features.
- Audit work found and closed mismatches between detector flags, emitter gates, module facades,
  and indirect consumers.
- Correctness gates—dual-mode tests, emitted-runtime synchronization tests, and Test262—are more
  durable than any one historical DLL-size measurement.

## Current maintenance rule

When a feature gains a new emitted dependency, update its detection and gate mapping in the same
change. Prefer over-emission to a false negative, add a focused compile-and-load test, and run the
dual-mode suite. Performance and artifact-size comparisons belong in the benchmark documentation,
not this decision record; see the [benchmark harness](../../../benchmarks/cross-runtime/README.md).

Future representation work that affects runtime emission is tracked in the
[active shaped-object plan](../shaped-objects-representation.md).
