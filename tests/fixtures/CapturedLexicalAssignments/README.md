# Assignment through an uninitialized captured let (#1761)

The four `lexical_*` sources preserve the frozen issue's displayed originals.
Fresh unchanged-main/pre-repair current compile and verify all four. The assignment
original silently prints `2`/`3`, omitting the required ReferenceError. Read,
typeof and nested-read controls already preserve their reference output. All
fresh runs have empty stderr and exit within the unchanged 30-second deadline.

The assignment guard now reaches the parent function's shared display object
through the closure's `$functionDC` field. It checks the existing binding after
evaluating the right-hand side and before storing. Initialization replaces the
TDZ sentinel, so subsequent assignment, undefined initialization and assignment
expression results remain valid. Promoted value-type fields are already proven
initialized before their sole closure is created and cannot hold a TDZ sentinel;
the guard skips that inapplicable object probe.

All 58 focused checks pass. Twelve compiled references, twelve isolated standalone
outputs and twelve hosted module controls cover the originals, nested setters,
right-hand-side side effects/throws, declaration without initializer, assignment
results, declared var bindings, shadowing and repeated initializer re-entry.
Full TypeScript compilation and Node confirm every expectation. Saved outputs
require verified IL, exact stdout, empty stderr, no SharpTS reference/copy and
clean exit within 30 seconds. Hosted exports are not executed. Existing native
sentinel metadata/constructor ownership and saved sentinel controls remain covered.

`promoted-numeric-control.ts` preserves the previously retained #1956 source.
Fresh unchanged main rejects it with `StackObjRef` at offset 98 in `create`,
where an unboxed double reached the object sentinel probe. Current verifies
and runs standalone with `13 14`/`true Infinity`, matching TypeScript/Node,
empty stderr and clean exit, without a SharpTS reference/copy. Both existing
interpreted/compiled numeric-closure tests now pass.

The wider lexical/capture/closure/arrow/sentinel selection passes 1,555 tests,
retaining two unchanged-main verifier failures excluded from passing counts:
nested async arrow mutation and the typed callback mismatch preserved with #1757.
`independent/var-forward-control.ts` preserves a separate checker diagnostic:
TypeScript/Node accept its forward var reference and print `1`/`2`/`3`, while
fresh unchanged-main/current reject `x` before emission. The declared-var control
passes; this task does not claim the separate forward-var checker repair.
