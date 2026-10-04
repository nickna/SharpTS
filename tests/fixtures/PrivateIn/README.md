# Private-in investigation controls (#1903)

These new sources are not recovered historical reproducers. The implementation
design and recorded evidence are in `docs/plans/private-in-brand-checks.md`.
Implementation is tracked by [#1962](https://github.com/nickna/SharpTS/issues/1962).

Node v25.5.0 and TypeScript 7.0.2 accept the three positive sources. The two brand
files each print `true true` twice followed by `false false` three times.
`evaluation.ts` prints `true 1` and `7`. Run each file separately:

```powershell
node tests/fixtures/PrivateIn/instance-brand.ts
tsc --target ES2022 --noEmit --skipLibCheck tests/fixtures/PrivateIn/instance-brand.ts
```

`outside-name.ts` and `undeclared-name.ts` are negative controls. Node rejects both
with SyntaxError; TypeScript reports TS18016 and TS2339 respectively.

SharpTS baseline `0ad37b57` and `b2d4a987` reject all five at parsing with
`Expect expression`, exit 1, in CLI interpretation and compilation. This does
not establish correct private-name rejection: the parser also rejects the valid
positive programs. The original historical deadline and source remain unknown.
