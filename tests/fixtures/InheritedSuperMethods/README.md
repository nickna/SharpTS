# Inherited super methods (#1756)

`original.ts` and `declared-parent-control.ts` preserve the frozen issue's
displayed sources. Fresh unchanged main and pre-repair current reject the
original because `value` is absent on the immediate superclass B. The declared
parent control already verifies IL and prints `grandparent`. Current accepts
the unchanged original, verifies IL and prints that same reference result.

Seven compiled and isolated standalone references cover the originals, deeper
inheritance, nearest overrides, the actual receiver, protected access and generic
grandparents. The checker walks the superclass chain, composes instantiated type
arguments and preserves inaccessible private-method rejection. Five negative
signature/access fixtures match TypeScript diagnostics, and the generic-chain
typing control is accepted; its acceptance is type-check coverage, not a saved
runtime claim. Full TypeScript compilation and Node confirm all seven executable
expectations. Saved guests require verified IL, exact stdout, empty stderr,
clean exit within the original 30 seconds, and no SharpTS reference/copy.

Seven hosted compilation controls append an export to exercise module emission,
verify IL and the hosting reference without a SharpTS reference/copy. Hosted
exports are not executed. Existing scoped native parent-method lookup and saved
extracted-super controls remain covered, including repeated standalone/hosted
metadata and constructor input isolation.

The broader selection retains six unchanged-main verifier failures, excluded
from passing counts. Four direct async super-call failures belong to #1757.
`independent/ClassInheritance_AsyncPath.ts` retains an object/string mismatch at
offset 7 in an async function parameter read. The Promise subclass fixture
retains an object/Delegate mismatch at offset 38; its baseline comparison uses
`--noLib` to match the test harness's ambient checker configuration. The default
CLI library instead rejects its generic Promise superclass before emission.
None of the invalid saved outputs is executed. These two unrelated lowering
diagnostics are not repairs claimed by the inherited-member checker task.
