# Private-method value investigation controls (#1901)

These are new investigation sources, not recovered historical reproductions.
Implementation is tracked by #1961; the bounded design is in
`docs/plans/private-method-values.md`.

With TypeScript 7.0.2, both positive files pass:

```powershell
tsc --target ES2022 --noEmit --skipLibCheck tests/fixtures/PrivateMethodValues/non-generic.ts
tsc --target ES2022 --noEmit --skipLibCheck tests/fixtures/PrivateMethodValues/generic.ts
```

Node v25.5.0 outputs `true true`, `7`, `11` for `non-generic.ts`, and
`true true`, `seven`, `11` for `generic.ts` (one line each). The dynamic comparison
in the generic case permits comparing different function signatures.

SharpTS baseline `0ad37b574e3c41d267300ada003d63d0f8026562` and `a99c0c17`
reject both in CLI interpretation and compilation with exit 1 and
`Private field '#read' does not exist on class 'Box'`. These failures are evidence
for the implementation task, not passing execution tests.

`outside-access.ts` must remain rejected; TypeScript reports TS18013, and
SharpTS reports that private access is outside a class body.
