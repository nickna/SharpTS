# Generic constructor compatibility boundary (#1905)

These are new, explicitly labelled controls. They do not recover the historical
source or deadline. See `docs/plans/generic-constructor-compatibility.md` for the
verified boundary and finite successor design.
Implementation is tracked by [#1963](https://github.com/nickna/SharpTS/issues/1963).

Run each source separately with TypeScript 7.0.2:

```powershell
tsc --target ES2022 --noEmit --skipLibCheck tests/fixtures/GenericConstructorCompatibility/public-pair.ts
```

TypeScript accepts `public-pair.ts`, `public-reassignment.ts` and
`constrained-pair.ts`. Node v25.5.0 prints `7`, `first:1`/`second:2`, and `ok`
respectively. The remaining four files must be rejected with TS2322. SharpTS
baseline `0ad37b57` and `6b7cc25e` reject all seven in both CLI modes before runtime.
That preserves the invalid pairs but leaves valid distinct constructors unsupported.
