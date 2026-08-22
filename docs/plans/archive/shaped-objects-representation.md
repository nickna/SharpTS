# Shaped-object investigation and compact-record outcome

Status: **archived outcome.** The hidden-class investigation was completed, but the proposed
general replacement of dynamic-object dictionaries did not ship. The compiler instead gained
conservative, typed compact-record specializations that capture the useful closed-shape cases
without changing the general JavaScript object representation.

## Original goal

The investigation targeted escaping, property-heavy objects whose dictionary storage and boxed
values dominated compiled JSON and tree workloads. A standalone spike showed that interned shapes,
pre-sized slots, and monomorphic inline caches could materially improve construction and repeated
property reads. An interpreter prototype also showed that shape storage without a call-site cache
was performance-neutral because the tree walker still had to resolve each property name.

The next prototype found a more important constraint: common compiled object literals were bare
`Dictionary<string, object?>` values, not instances of the emitted `$Object` type assumed by the
initial design. Replacing that representation would have required changing every consumer of plain
objects, including enumeration, spread, cloning, descriptors, deletion, and dynamic access. The
general hidden-class design was therefore not adopted.

## What landed

SharpTS now specializes only shapes that static analysis can prove safe:

- Non-escaping object-literal locals can use generated `$Shape_N` value types with typed fields.
- Closed JSON record graphs can use generated scalar-record carriers and direct parse/stringify
  paths, including native primitive fields where their types are stable.
- Stable module-private, call-only functions can carry exact compact-record types through proven
  parameter and return edges, including conservative recursive graphs.
- Stable array operations can retain compact records when analysis proves that the values remain
  in the specialized pipeline.
- Runtime feature detection records possible materialization and escape sites. Unknown or dynamic
  behavior disables the proof or routes through `EnsureMaterialized` before ordinary JavaScript
  object semantics become observable.

The implementation is centered on the compilation feature detector, JSON shape analysis,
`ExactCompactRecordFunctionAnalyzer`, and the emitted compact/scalar record runtimes. Compiler tests
cover direct access, recursive signatures, JSON round trips, stable collection paths, mutation and
escape fallbacks, materialization, and IL verification.

## Resulting architectural rule

Generated CLR shapes are internal compiler representations, not a replacement public object model.
They may be used only behind conservative whole-program proofs and must preserve property order,
aliases, recursive identity, evaluation order, exceptions, and `null`/`undefined` behavior. A value
that crosses an unproven call, export, dynamic property, mutation, or reflection-like boundary must
use or materialize the ordinary runtime representation.

The interpreter continues to use its ordinary object representation. The proposed general dynamic
hidden-class tree, polymorphic inline caches, and dictionary-mode deoptimization remain unimplemented
and would require a new design and measurement cycle before being reconsidered.

## Validation outcome

The shipped slices were accepted only with semantic regression tests, compiler proof/fallback tests,
IL verification, and benchmark evidence. Current performance work remains tracked by the standing
performance issue; volatile benchmark numbers belong in benchmark results rather than this record.
