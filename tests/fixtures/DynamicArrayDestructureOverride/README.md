# Dynamic array iterator overrides — #1753

The exact `original.ts` and `original-alias.ts` preserve the issue programs.
Fresh unchanged main and pre-repair current produce `1 2` from the dynamic
original and the correct `8 9` from its typed alias control. Each saved output
verifies its IL, exits cleanly within the original 30 seconds and has empty
stderr. Node and TypeScript accept all reference sources separately.

Thirteen compiled references run as isolated saved standalone outputs with
verified IL, exact Node stdout, empty stderr, clean exit within 30 seconds and
no SharpTS reference/copy. Twelve also run in the interpreter. Coverage includes
both originals, a typed source, key/tuple aliases, a captured next getter,
empty/fresh iterators, factory throws, invalid methods and unaffected arrays.
The compiled getter/receiver control selects the getter and factory once.
Both interpreter versions still reject that getter control as a non-callable
iterator method; it is excluded from interpreted passing counts.

The dynamic normalizer receives a scoped mutation flag and materializes array
sources through the existing protocol collector when an override is possible.
Descriptor APIs also disable the iterator fast path. Native reused-emitter
tests cover unchanged array identity, collection when mutation is possible,
the public ABI, exact dependencies and standalone/hosted output isolation.

`prototype-override.ts` and its any-alias companion
`prototype-override-independent.ts` record a separate prototype lookup failure.
Node expects `8 9`; unchanged main and current saved alias outputs verify their
IL and cleanly print `1 2`. The direct typed prototype source was rejected on
unchanged main and becomes accepted after #1752, but still prints `1 2`.
These sources remain outside passing counts. This task does not claim a general
prototype lookup repair, incremental binding/closing (#1750), fresh CLR
enumerable acquisition (#1754), or hosted guest execution.
