# Extracted TextDecoder.decode (#1722)

`original.ts` is the unchanged issue source. Node prints `A` followed by a
newline. Unchanged `0ad37b57` compiles and IL-verifies it, then exits zero with
only a newline and empty stderr. Fresh reproduction establishes that output
discrepancy, without attributing historical execution delays.

`forwarding.ts` is a new control. Expected lines are `AB`, `B`, `XABY`, `0 0`
and four `true` lines. It checks call/apply, another decoder, TypedArray byte
offset/length, a multi-byte element view, missing/empty input and guest TypeError
for invalid receivers. Views are precomputed to isolate decoder behavior.
`nested-arguments-control.ts` preserves the first version of that new control:
Node expects the same output, but the compiled same-arity nested subarray call
overwrites the outer receiver and throws before stdout. This remains a failed
comparison belonging to the separately tracked argument-pool issue #1727.

Compile each source separately with TypeScript, then run Node:

```text
tsc --noEmit --skipLibCheck --target es2022 source.ts
node --experimental-strip-types --no-warnings source.ts
dotnet SharpTS.dll --no-tsconfig --compile original.ts -o original.dll --standalone --verify
dotnet original.dll
```

Shared regressions execute both engines. Isolated CLI regressions require IL
verification, no SharpTS assembly reference, zero exit and empty stderr; they
use a sixty-second compile limit and thirty-second execution bound. Native
saved-type controls also exercise the emitted decoder wrapper with TypedArray
bytes in standalone and hosted declaration configurations. No hosted guest
execution, general encoding-label repair or streaming decoder repair is claimed.

The exact combined original retained under `ExtractedTypedArrayMethods` now
matches Node: `7`, `true`, `3 3`, `A`, `9`, with zero exit and empty stderr.
