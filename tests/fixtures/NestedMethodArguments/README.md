# Nested dynamic method arguments (#1727)

`original.ts` is the unchanged issue source. Node expects `12 34 1 2`, `154`,
then `1 2`. Unchanged `0ad37b57` compiles and IL-verifies it, then prints `64`
on the middle line, exits zero and has empty stderr. The three separately
named original controls preserve the exact materialized, spread and extracted
function-value snippets; each expects `154`.

New controls cover same/different arities, deeper nesting, independent retained
arguments objects, receiver/callee/argument getter order, abrupt argument
evaluation, recovery afterward, indirect Array.from callbacks and numeric
coercion that enters guest code. The exact nested decoder control from #1722 is
also retained and now passes. A native non-nested call-site test observes that
repeated dynamic calls receive the same per-arity pooled array, preserving reuse.
Existing pool tests cover thread isolation, one-through-four caching and spread
expansion. No per-call array allocation is introduced for these pooled arities.

The numeric `valueOf` coercion control is compiled-only coverage. Node and the
repaired compiler print `154`; the interpreter prints `NaN` on unchanged main
and current code. `indirect-interpreter-control.ts` preserves the earlier new
combined callback/coercion source and unchanged Node output `154` then `154`.
Its interpreter comparison remains failed because of unary-object coercion,
and is not counted as passing conformance or repaired in this task.

Compile each reference independently:

```text
tsc --noEmit --skipLibCheck --target es2022 source.ts
node --experimental-strip-types --no-warnings source.ts
dotnet SharpTS.dll --no-tsconfig --compile original.ts -o original.dll --standalone --verify
dotnet original.dll
```

Shared cases execute both engines. Nine isolated saved outputs require IL
verification, no SharpTS assembly reference, zero exit and empty stderr,
with sixty-second compilation and thirty-second execution bounds. No hosted
guest execution or interpreter numeric-coercion repair is claimed.
