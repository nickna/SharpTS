# Incremental binding and close order — #1750

This is a design transfer with retained failures, not a behavior repair.
`original-partial`, `original-empty`, `original-rest` and `original-generator`
preserve the exact issue programs. Fresh unchanged main and current print
`1 4 0` and `4 0` for the failures; Node prints `1 1 1` and `0 1`.
Both original controls already pass, including the weak generator-finally
control which cannot distinguish early closing from eager exhaustion.

All four saved originals verify their IL, finish within 30 seconds, have
empty stderr and require no SharpTS runtime. The three added ordering probes
also verify/finish cleanly, but retain wrong default/nested/abrupt-close order
in both compilers. `infinite-close.ts` expects TypeError in Node and instead
times out at the original 30 seconds in unchanged main and current with empty
streams. All retained failing executions are excluded from passing totals.

Four passing controls run in both engines and as saved standalone outputs:
the two original controls, array rest, and defaults after immediate exhaustion.
Saved checks require exact stdout, empty stderr, a clean exit within 30 seconds,
verified IL and no SharpTS reference/copy. Node and TypeScript check all ten
sources independently. The finite implementation contract is in
[`incremental-array-binding.md`](../../../docs/plans/incremental-array-binding.md).
That plan preserves binding scope and incremental cursor state through nested
defaults and cleanup, with explicit boundaries for existing consumers.

No production code changed. No iterator consumption/close, full destructuring,
hosted execution or unrelated ownership repair is claimed.
