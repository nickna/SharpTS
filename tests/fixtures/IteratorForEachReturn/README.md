# Iterator forEach result — #1745

The three `original*` references preserve the exact issue sources. Fresh
unchanged main prints `4,6 false` and `true false` for the two result failures,
while the callback-values-only control already passes. Saved baseline outputs
verify their IL and exit cleanly within 30 seconds with empty stderr.

Six passing compiled references also run as isolated standalone outputs. They
check empty/nonempty iteration, callback values and indices, normal generator
exhaustion and exception identity. Saved execution requires exact Node stdout,
empty stderr, clean exit within 30 seconds and no SharpTS assembly reference or
copy. Native emitter-reuse tests check the exact undefined singleton for empty
and nonempty inputs, including hosted declarations and optional features.

Both `*-value-callback-independent.ts` references are excluded from passing
counts. Node executes and TypeScript accepts these block callbacks that return
a value, but unchanged main and the corrected compiler reject them as a value
not assignable to void before creating an assembly. The positive added controls
use void callback blocks; all original callback sources remain unchanged.
TypeScript uses `--lib esnext,dom` for the Iterator Helpers declarations.

No contextual callback typing, abrupt-close policy or hosted guest execution
repair is claimed.
