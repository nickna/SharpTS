# IteratorClose results — #1739

The three `original-*` programs are unchanged issue sources. Node expects
TypeError for BigInt, Symbol and numeric results. Fresh unchanged main accepts
the first two and rejects the third; all three saved outputs verify their IL,
exit within the original 30-second deadline and have empty stderr.

Eleven passing references exercise compiled execution and isolated saved
standalone output. Controls cover all other primitive categories, valid object,
array and function results, return completion, getter receiver/counts and
incoming-throw identity when validation, property lookup or invocation fails.
Saved outputs require exact stdout, empty stderr, clean exit, no SharpTS assembly
reference/copy and the unchanged 30-second deadline. Node and TypeScript also
check the separate destructuring reference.

`destructure-close-independent.ts` is excluded from passing counts. Compiled
destructuring materializes the whole iterable before closing it; this infinite
iterator times out with empty streams on both fresh unchanged main and the
corrected compiler. Node terminates with TypeError. The frozen epic's #1750 owns
that destructuring consumer repair. The interpreter's normal-close result validation is also an
existing separate gap: unchanged main accepts the original BigInt result.

Native emitted-runtime tests cover every primitive category, suppression when
an incoming throw exists, standalone/hosted declaration ownership and emitter
reuse. Hosted guest execution and interpreter close semantics are outside this
compiled correction.
