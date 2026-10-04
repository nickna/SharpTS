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
These failures are subsequently repaired by #1777, which enables the complete
unchanged expectations for all eight compiled and isolated originals. The
interpreter independently rejects the merged namespace's `first` lookup and
the dynamic-write path; those originals remain outside interpreted passing
counts.

No imported namespace, mutation, missing-member, deletion, enum reverse-map or
class-construction repair is claimed by this task. Those epic children retain
their own original probes and verification.
