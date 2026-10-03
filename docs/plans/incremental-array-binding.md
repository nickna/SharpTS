# Incremental array binding and cleanup — #1750

## Evidence and disposition

This is a finite design transfer, not a behavior repair. The four exact issue
sources are retained in `tests/fixtures/ArrayDestructureIteration`. Fresh
unchanged main (`0ad37b574e3c41d267300ada003d63d0f8026562`) and revision
`a02f48ac` reproduce both failures, while retaining both original controls.
Every saved original verifies its IL, exits cleanly within 30 seconds and has
empty stderr. Node v25.5.0 supplies the reference output; TypeScript 7.0.2
accepts each source separately with strictness disabled to preserve the old
reference checker's defaults.

| Source | Node stdout | Unchanged main and current stdout |
| --- | --- | --- |
| `original-partial.ts` | `1 1 1` | `1 4 0` |
| `original-empty.ts` | `0 1` | `4 0` |
| `original-rest.ts` | `3 6,9 4` | `3 6,9 4` |
| `original-generator.ts` | `closed` then `2` | `closed` then `2` |
| `default-order.ts` | `7 next,default,close` | `7 next,next,next,default` |
| `nested-order.ts` | `1 outer-next,inner-next,inner-close,outer-close` | `1 outer-next,outer-next,inner-next,inner-next,inner-next` |
| `default-throws.ts` | `true 1 1` | `true 3 0` |

The original generator control is weak: eager exhaustion also runs its finally
block. It must remain a passing control, not evidence of early closing.
`infinite-close.ts`, recovered during #1739, expects `TypeError` for a Symbol
close result. Unchanged main and current instead exceed the original 30-second
deadline with empty streams. This failure remains outside passing counts.

## Why the binding representation must change

`Parser.Destructuring.cs` lowers declaration patterns to a scope-free Sequence:
first `__arrayDestructure(source)`, then positional reads, defaults and slice
for rest. The runtime helper materializes non-indexable iterables completely;
the interpreter also calls `GetIterableElements(...).ToList()`. The resulting
array has lost the original iterator and its completion state before the first
binding begins. Pattern assignment uses the same eager normalization convention.

A bounded materializer cannot interleave default evaluation with advancement,
close nested iterators in the correct order, or preserve binding-error
precedence. Merely closing after materialization retains all of those failures
and cannot terminate an infinite source. The compiler additionally flattens
Sequence for binding/capture registration and reconstructs it during hosted
top-level lowering. Cleanup metadata attached only to that container would be
lost without auditing these paths. The numeric destructuring optimization also
recognizes this lowered shape and needs an explicit applicability proof.

## Finite implementation contract

1. Preserve array declaration patterns as a structured binding region, or an
   equivalent explicit operation sequence which retains the acquired iterator
   across binding evaluation. Keep the current enclosing binding scope: wrapping
   declarations in an ordinary scoped block changes visibility. Preserve tuple
   inference, defaults, lexical capture registration and single RHS evaluation.
2. Acquire the guest iterator and capture next once without advancing it. Track
   its completion state separately from the current value. A binding advances
   once; an elision advances without reading value; a default runs immediately
   after the corresponding undefined value. No later advance may precede it.
3. For a partial or empty pattern whose iterator is not complete, perform
   IteratorClose once. Binding/default/nested errors keep their original identity
   if closing throws or returns a primitive. Normal closing validates the
   guest return result using the existing scoped close helper. A next/result
   failure follows IteratorStepValue's completion rule rather than indiscriminate
   finally-based closing.
4. Bind nested patterns before resuming the outer pattern. Close an unfinished
   inner iterator before its outer iterator; on failure, close outer with the
   incoming completion. Rest drains incrementally into a fresh array and skips
   closing after normal exhaustion. Defaults after exhaustion must not restart
   advancement. Each acquired iterator owns its own completion flag.
5. Implement the same region in ordinary compiled execution and interpretation.
   Preserve region/cursor state through compiler capture and module transforms.
   For functions, parameter patterns and for-of bindings that reuse declaration
   lowering, verify scope and per-invocation independence. Existing async and
   generator lowering must retain the region around suspending defaults and
   nested bindings; either preserve its state correctly or transfer that
   separately with an explicit supported boundary before broad promotion.
6. Retain numeric/index fast paths only when their proof establishes identical
   advancement, binding side effects and completion behavior. Arbitrary user
   callbacks in defaults cannot be reordered across iteration. Reuse scoped
   protocol/record/close dependencies without restoring whole runtime holders.

The reference for binding order and completion is the published
[ES2025 binding algorithms](https://tc39.es/ecma262/2025/multipage/ecmascript-language-statements-and-declarations.html#sec-destructuring-binding-patterns-runtime-semantics-iteratorbindinginitialization).
The materializer is also used by array assignment patterns: audit and record
that consumer before replacing shared parser machinery. General assignment
target setters and expression evaluation are outside this declaration-focused
successor unless required to avoid a regression in shared lowering.

## Acceptance and stopping rule

Promote partial, empty, default-order, nested-order, default-throws and infinite
fixtures only after they match Node within 30 seconds. Keep the four passing
controls independent. Add next/return getter counts and receiver identity,
elisions with throwing value getters, exhausted patterns, nested close errors,
default mutation, bound/captured bindings, parameters and for-of patterns.
Preserve the original sources and weak generator control unchanged.

Run shared interpreted/compiled tests, verified saved standalone output with
exact stdout/empty stderr/no SharpTS reference or copy, affected destructuring,
capture, parameter, generator/async, module and AST-catalog checks, quality gates
and the actual AOT baseline. Report hosted execution separately from declaration
ownership. Successful IL verification alone is not a runtime pass.

Stop when the named declaration-pattern consumption, binding order and closing
contract passes. Non-iterable guest errors remain #1751, typed Symbol.iterator
assignment remains #1752, any-array override selection remains #1753, and fresh
CLR enumerable acquisition remains #1754. This transfer does not reopen
ownership migrations or start a general destructuring conformance campaign.
