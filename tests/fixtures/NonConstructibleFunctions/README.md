# Dynamic non-constructor function values (#1725)

`branch-original.ts` and `boolean-original.ts` preserve both exact issue sources.
Node expects three `threw TypeError true` lines and `true true true`, respectively.
Unchanged `0ad37b57` saves and IL-verifies the branch source, then exits zero
with empty stderr and three `constructed` lines.

New controls cover arrows, async arrows/functions, sync/async generators,
repeated binding, Reflect.construct target/newTarget validation, argument
evaluation before rejection, and ordinary/bound positive constructors. Shared
tests execute these sources in both engines. All original expectations remain
unchanged. Six isolated saved outputs additionally require IL verification,
no SharpTS assembly reference, zero exit and empty stderr; compilation uses a
sixty-second bound and execution a thirty-second bound.

`prototype-preservation.ts` is compiled-only coverage: the existing emitted
generator prototypes remain present while arrow/async prototypes remain absent.
Unchanged main and the repaired compiler both print `true true true true`.
`prototype-interpreter-control.ts` retains the earlier new combined control;
Node expects five `true true true` lines, `true true true true`, then `7 7 7`.
The interpreter instead prints `false false true true` on the prototype line
because it already lacks generator prototype properties. That comparison remains
failed and is outside the construction-policy repair; its Node expectation is
not weakened or counted as passing interpreter conformance.

Compile each reference independently:

```text
tsc --noEmit --skipLibCheck --target es2022 source.ts
node --experimental-strip-types --no-warnings source.ts
dotnet SharpTS.dll --no-tsconfig --compile branch-original.ts -o original.dll --standalone --verify
dotnet original.dll
```

Generator execution, original bound-constructor controls and shared native
constructor metadata tests remain in the affected selection. No hosted guest
execution or interpreter generator-prototype repair is claimed.
