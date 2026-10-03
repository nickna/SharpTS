# Iterator take/drop limits — #1746

`original.ts` and `original-zero.ts` preserve both exact issue programs. Fresh
unchanged main accepts take(-1), after correctly producing the valid pipeline
values; the zero controls already match Node. Both baseline saved outputs verify
their IL, exit cleanly within 30 seconds and have empty stderr.

Twelve compiled references also run as isolated saved standalone outputs. They
check negatives, NaN/undefined, fractions and negative zero, infinity/large
limits, primitive/object coercion, coercion count, invalid-limit closing and
exception precedence when closing fails. BigInt/Symbol conversion throws
TypeError. Node runs and TypeScript checks each source separately with
`--lib esnext,dom`. Saved execution requires exact stdout, clean exit within
30 seconds, empty stderr and no SharpTS assembly reference/copy.

The factory receives the original boxed argument, coerces once, rejects NaN,
truncates finite fractions and rejects negative integer limits. Failure closes
the receiver while preserving the incoming exception. The lazy wrappers retain
double integer limits/counters instead of narrowing them to Int32. Native
emission-reuse tests check the updated scoped helper ABI, validation, infinity,
laziness and standalone/hosted declaration ownership.

The reference is the published ES2025
[take](https://tc39.es/ecma262/2025/multipage/control-abstraction-objects.html#sec-iterator.prototype.take)
and [drop](https://tc39.es/ecma262/2025/multipage/control-abstraction-objects.html#sec-iterator.prototype.drop)
algorithms, which agree with the local Node references. The newer draft's finite
safe-integer ceiling is not applied to these original-version expectations.
Normal limit-reached closing remains the separate frozen child #1747. No
interpreter, arbitrary invalid receiver or hosted guest execution repair is
claimed.
