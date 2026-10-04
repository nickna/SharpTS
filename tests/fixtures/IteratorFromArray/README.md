# Iterator.from array adaptation — #1748

The three `original*.ts` files preserve the exact issue sources. Fresh unchanged
main returns the array itself, prints `true true` for identity, and catches an
undefined-function TypeError in the stage control. The original next-call
program exceeds its unchanged 30-second deadline with empty stdout and an
uncaught TypeError. Those baseline observations remain recorded separately.

Thirteen compiled references also run as isolated saved standalone outputs.
They check next values, compatible iterator identity, fresh repeated adapters,
live growth/shrinkage, permanent exhaustion with an undefined completion value,
indexed getters and thrown identity, custom iterator selection/getter counts,
generators, helpers and captured next methods. Node and TypeScript check every
source separately with `--lib esnext,dom`. Saved execution requires verified IL,
exact stdout, empty stderr, clean exit within the original 30 seconds and no
SharpTS reference/copy.

The factory selects custom Symbol.iterator once. Default arrays use the
existing live indexed array iterator; compatible native iterators retain
identity. Custom iterator objects are adapted once with their captured next
method. Dependencies remain scoped and the public factory ABI is unchanged.
This follows the bounded array/identity behavior in the published
[ES2025 Iterator.from algorithm](https://tc39.es/ecma262/2025/multipage/control-abstraction-objects.html#sec-iterator.from).

`any-toarray-independent.ts` retains a separate dynamic helper-dispatch gap:
Node succeeds, while unchanged main and current throw an undefined-function
TypeError, exit with an unhandled-error status and have empty stdout. It is
excluded from passing counts. New array controls use the inferred iterator
type when calling helper methods. No full Iterator.from primitive/override
compatibility, dynamic helper dispatch, interpreter or hosted guest execution
repair is claimed.
