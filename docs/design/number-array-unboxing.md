# Decision record: unboxed `number[]` representation

Status: direction selected; implementation scope remains undecided.

## Context

Compiled TypeScript arrays use a reference identity that is observable through aliases, mutation,
`any`, object fields, callbacks, built-ins, and .NET interop. Storing every numeric element as an
`object` adds boxing and dispatch overhead, but an optimization cannot change those identity and
aliasing semantics.

## Rejected: copy-based typed representation

Representing a `number[]` as a separate `List<double>` (or copying between typed and boxed lists at
boundaries) was rejected as a general solution. A copy creates two containers. Mutating one through
an alias no longer updates the other, and reference equality can change depending on which static
type a value crossed.

Local promotion remains sound when analysis proves the array cannot escape or alias. That is a
useful contained optimization, but it cannot be advertised as the representation for parameters,
fields, returns, closures, or values widened to `any`/`object`.

## Selected direction: identity-preserving elements kind

The sound general direction is one array object whose internal storage can be numeric or boxed:

- A numeric array starts with an unboxed `double` store.
- Statically numeric index reads, writes, length operations, and selected hot mutations use typed
  helpers.
- Crossing a boundary that needs the general JavaScript array representation performs a one-time
  deoptimization into the boxed store on the same array object.
- Every alias continues to observe the same object and subsequent mutations.
- Unsound casts and non-number writes trigger deoptimization or the normal checked failure; they
  never create a second independently mutable array.

This is analogous to an elements-kind transition: representation may change, object identity may
not.

## Current constraints

The emitted array type inherits from a general list shape, and many runtime helpers consume that
base API directly. Those operations cannot be intercepted by overriding an element accessor.
Therefore a numeric store is viable only if the compiler has complete, reviewable deoptimization
points before the object reaches a base-list consumer.

The implementation must preserve:

- reference equality and aliasing across typed and dynamic views;
- holes, length changes, and JavaScript mutation order;
- callback and iterator behavior under re-entrant mutation;
- array built-in semantics in interpreter and compiled modes;
- interop and module boundaries;
- conservative fallback when static type information is incomplete; and
- runtime feature detection/tree-shaking correctness for the added helpers.

The interpreter may retain boxed storage as long as observable behavior stays equal. This is a
compiled representation optimization, not a new public array type.

## Remaining decision

Before implementation, decide whether the bounded deoptimization audit is maintainable enough to
justify a second store. The proposal needs:

1. an enumerated set of compiler boundaries that hand an array to general runtime code;
2. a single deoptimization helper used by every such boundary;
3. focused aliasing, mutation, callback, exception, and interop tests in both modes;
4. Test262 regression checks; and
5. benchmarks demonstrating that the gain survives realistic built-ins and deoptimization.

If that completeness argument cannot be made, keep expanding proven non-escaping local promotion
instead. The rejected copy-based representation must not be revived as a shortcut.
