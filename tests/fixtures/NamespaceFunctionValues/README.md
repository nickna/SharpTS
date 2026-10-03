# Namespace function values (#1774)

These eight unchanged originals reproduce the issue body's members, nested,
merged, live-binding, dynamic-write, generator, async and non-calling shape
probes. Full TypeScript ES2022/CommonJS compilation followed by Node confirms
all expected outputs, including the merged namespace source.

Fresh unchanged main and pre-repair current verify every saved output's IL.
Seven outputs report `TypeError: object is not a function`; the shape control
prints `function object true`. Fresh processes finish within the original
30-second deadline. The historical Windows timeout remains tracked in #1772.

After script-scope function resolution is repaired, all function declarations
are callable. Six originals now match their full reference outputs, with
verified IL, empty stderr, clean exit, no SharpTS assembly reference or copy,
and the same deadline. The mutation originals expose their next independent
failure: live-binding prints `1 1` then `1 2 2`; dynamic-write prints `2 2 2`.
They remain outside passing counts and are retained for #1777. The interpreter
also independently rejects the original merged namespace's `first` lookup;
that original is only counted as passing in compiled mode.

No imported namespace, mutation, missing-member, deletion, enum reverse-map or
class-construction repair is claimed by this task. Those epic children retain
their own original probes and verification.
