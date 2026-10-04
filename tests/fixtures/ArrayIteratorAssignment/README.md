# Typed array iterator assignments — #1752

`original.ts` and `original-alias.ts` preserve the exact issue sources.
TypeScript accepts both. Fresh unchanged main rejects the typed original
before emission and executes the alias control with `8 9`; the pre-repair
current evidence records the same distinction.

Seven runtime references pass in both engines and as isolated saved standalone
outputs: numeric/string arrays, readonly arrays, tuples, a unique-symbol key
alias and a copied factory. Saved outputs require verified IL, exact Node
stdout, empty stderr, clean exit within the original 30 seconds and no SharpTS
reference/copy. Eight negative controls are rejected by TypeScript, the checker
and CLI before emission. Readonly numeric indices remain readonly; assigning
the iterator method is legal.

The checker exposes the array iterator factory with its element type and
requires a synchronous iterable iterator result. Compilation routes symbol
keys through ordinary property storage and preserves mutation detection for
tuples and unique-symbol key aliases.

`iterator-return.ts` is an additional accepted typing/emission control, not a
passing runtime reference. Its any-alias counterpart in
`iterator-return-independent.ts` produces `8 9` in Node, but unchanged main and
current saved outputs both print no stdout and throw an undefined-function
TypeError before exceeding the unchanged 30-second deadline. Both interpreter
versions also reject this native array iterator result. These failures remain
outside runtime passing counts.

`lifted-wrong-element-independent.ts` preserves a separate checker timing gap:
the in-process checker accepts an assignment before refining the parser-lifted
generator declaration's yield type. The CLI and TypeScript reject it. The
passing wrong-element rejection control checks a preceding named generator,
so its concrete string yield type is available at assignment. This task does
not claim a general forward-reference inference repair, dynamic array source
normalization (#1753), or hosted guest execution.
