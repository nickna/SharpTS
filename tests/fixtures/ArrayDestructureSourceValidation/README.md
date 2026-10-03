# Non-iterable guest sources — #1751

The three `original*.ts` files preserve the exact issue programs. Fresh
unchanged main and pre-repair current retain `Error` for null, undefined and
number, and accept an empty object. Saved baseline programs verify their IL,
finish within the original 30 seconds and have empty stderr.

Fourteen compiled references also run as isolated saved standalone outputs.
They check the original failures, other primitives, next-only and array-like
guest objects, empty patterns, invalid iterator methods/results, factory throw
identity and one-time getter/factory selection. Valid array, string, Set, Map,
custom iterator and typed-array paths remain controls. Saved execution requires
verified IL, exact Node stdout, empty stderr, clean exit within 30 seconds and
no SharpTS reference/copy. Node and TypeScript check each source separately.
The null control's chosen message matches Node; cross-engine message wording
is not a general compatibility contract.

The normalizer requires a guest iterator method for ordinary object storage
and class-field objects. It preserves native CLR enumerable/iterator interop
and selected typed-array/Buffer collection paths. It selects a custom method
once, invokes it with the source receiver, and adapts its captured next method
before collection. A returned iterable without next is rejected. Native
reused-emitter tests validate guest rejection, Queue and string controls,
public ABI, scoped dependencies and standalone/hosted declaration ownership.

Incremental consumption and close order remain the #1750 design; dynamic
array iterator overrides remain #1753; fresh LINQ acquisition remains #1754.
No interpreter or hosted guest execution repair is claimed.
