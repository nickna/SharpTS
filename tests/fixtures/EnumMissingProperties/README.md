# Missing enum properties (#1789)

`missing.ts` preserves the displayed original and its `undefined undefined`
expectation. Fresh unchanged main verifies and exits cleanly but prints `threw`;
current after #1790 verifies and prints the complete expected output, with empty
stderr and clean exit within the original 30 seconds.

Six script and two module references were established with TypeScript 7.0.2 and
Node 25.5.0 before SharpTS execution. They cover literal/dynamic/aliased/namespace
and module-qualified valid/missing keys, string numeric keys, nonfinite misses,
undefined identity and typeof. Canonical enum objects use ordinary property reads
after #1790; no separate native helper ABI change is necessary. Native direct
helper missing-key exception coverage remains a separate implementation contract.

Saved outputs require verified IL, exact stdout, empty stderr, clean exit and no
SharpTS reference/copy within 30 seconds. Hosted checks verify declarations and
deployment without invoking exported functions.
