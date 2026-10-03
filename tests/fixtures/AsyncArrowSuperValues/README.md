# Super method values in async arrows (#1759)

The three `super_async_arrow_*` sources preserve the frozen issue's displayed
originals. Fresh unchanged-main and pre-repair current outputs verify IL, exit
normally within 30 seconds and have empty stderr. Two emit no stdout; the rejection
control prints `rejected TypeError object is not a function`. Explicit `this`
capture does not correct the lookup. No timeout or process-lifetime cause is
claimed. The two ordinary async-method controls already verify and preserve
their exact `11`/`7` output, empty stderr and clean exit on unchanged main.

Current reuses the async arrow's existing `EmitThis` receiver loader for a super
method value. It supports both standalone lexical capture fields and outer async
state-machine receivers. The runtime lookup boundary and wrapper constructor
remain unchanged. The three originals now preserve `7`, `child:7` and `7`,
respectively, including success instead of rejection in the third source.

All 24 focused checks pass: eight compiled references, eight isolated standalone
outputs and eight hosted module compilation controls. They include values read
before/after await, an arrow inside an async method, a generic enclosing class,
and the ordinary async-method controls. Full TypeScript compilation and Node
confirm every expectation. Saved outputs verify IL, exact stdout, empty stderr,
no SharpTS reference/copy and clean exit within the original 30 seconds. Hosted
controls verify declarations/deployment without executing their exports.

`independent/LockDecorator_AsyncMethod_MultipleInstances_IndependentLocks.ts`
retains an unrelated async constructor argument lowering failure. Fresh unchanged
main/current saved compilation with `--noLib --decorators` matches the harness's
ambient library and Stage3 decorator setting, and rejects the same object/string
arguments at offsets 30 and 52. Invalid outputs are not executed. This diagnostic
is excluded from passing counts and does not establish an async-arrow regression.

The wider async-arrow/async-method/super selection passes 509 tests, retaining
the lock-test constructor failure, the existing nested async arrow mutation
verifier failure, and the Promise constructor mismatch preserved with #1756.
Existing native parent-method ownership and saved super controls remain covered.
Quality gates and the actual AOT analyzer baseline pass with zero analyzer warnings.
