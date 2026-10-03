# Bound dynamic constructors (#1724)

`bound-original.ts` and `combined-original.ts` are the unchanged issue sources.
Node expects `2 90 false` for the independent source, and `3` then `2` for the
combined source. Unchanged `0ad37b57` saves and IL-verifies the independent
program, exits zero with empty stderr, and prints `undefined 2 false`.
The interpreter previously rejected bound construction outright.

New controls cover repeated binding and argument prepending, target prototype
linkage, fresh receiver identity, preserving the captured receiver for ordinary
calls, explicit constructor object returns, thrown object identity, construction
after a failure, and construction after an async suspension. Every reference
source is checked separately with TypeScript and Node:

```text
tsc --noEmit --skipLibCheck --target es2022 source.ts
node --experimental-strip-types --no-warnings source.ts
dotnet SharpTS.dll --no-tsconfig --compile bound-original.ts -o original.dll --standalone --verify
dotnet original.dll
```

Shared tests run both engines. Isolated saved-output tests require IL
verification, no SharpTS assembly reference, zero exit and empty stderr, using
sixty-second compilation and thirty-second execution bounds. Native construction
ownership/restoration tests remain in the affected selection. This task does not
claim to resolve dynamic construction of non-constructible arrows, async or
generator functions (#1725), Proxy constructor deployment (#1799), or hosted
guest execution.
