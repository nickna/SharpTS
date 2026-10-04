# Runtime superclass investigation controls (#1907)

These are new controls, not recovered historical sources. See
`docs/plans/runtime-valued-superclass.md` for the finite representation decision.
Implementation is transferred to [#1967](https://github.com/nickna/SharpTS/issues/1967).
TypeScript 7.0.2 accepts all three final sources when checked separately with
`--target ES2022 --noEmit --skipLibCheck`.

Node v25.5.0 and SharpTS interpretation print:

- `awaited-parent.ts`: `right`, `2 right`, `true`, `true false`.
- `observed-parent.ts`: `right`, `function right`, `true`, `true false`.
- `abrupt-parent.ts`: `parent `, with an empty static-initialization marker.

Baseline `0ad37b57` and `dced3a75` compiled positive sources pass IL verification
but produce the wrong output. The first catches `undefined is not a function`;
the observer prints `undefined undefined`, `false`, `false false`, omitting the
parent-constructor line. Both exit 0 because the fixture handles rejection.
The abrupt source instead fails compilation with a null `key` exception, exit 1.
Neither an IL pass nor a caught rejection establishes runtime success.
