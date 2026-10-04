# Super access in synchronous arrows (#1758)

The five `super_arrow*`/`super_nested_arrow*` sources preserve the frozen issue's
displayed originals. Fresh unchanged main rejects the three sources without an
explicit `this` read with `UnrecognizedArgumentNumber`. The two explicit-capture
controls verify IL but throw `TypeError: object is not a function`; fresh saved
runs exit with nonzero status and empty stdout within 30 seconds. The historical
timeout remains a recorded failure, without a claimed process-lifetime cause.

After #1757's implicit receiver capture, pre-repair current verifies the four
method-value cases but still throws that TypeError. Its direct nested-arrow case
retains a display-class/parent receiver verifier mismatch. Invalid saved outputs
are not executed. Current after this repair verifies and runs all five originals,
preserving `5` or `child:5`, exact stdout, empty stderr and clean exit.

Super method values load `this` through the ordinary lexical receiver resolver.
Arrow bodies also carry their enclosing class builder and use the cached #1757
parent-call bridge for direct calls. This keeps the captured receiver and bypasses
later overrides rather than passing the arrow display instance to a parent method.

Twelve compiled references, twelve isolated standalone outputs and twelve hosted
module compilation controls pass all 36 focused checks. They cover the originals,
deeper arrow captures, direct receiver-dependent calls, lexical owner and ignored
call-site receiver, generic owners, string argument order, defaults and callbacks.
Full TypeScript compilation and Node confirm every executable expectation.
Standalone outputs verify IL, exact stdout, empty stderr, no SharpTS reference/copy
and clean exit within the unchanged 30-second deadline. Hosted controls verify
declarations and hosting references; their exports are not executed.

The wider arrow/closure/private/super suite passes 1,441 tests and retains four
unchanged-main verifier failures, excluded from passing counts: #1956 closure
boxing, nested async arrow mutation, the typed callback mismatch retained under
`StateMachineSuperCalls/independent`, and the Promise constructor mismatch retained
under `InheritedSuperMethods/independent`. Existing native parent-method ownership
and saved super controls remain covered. This task does not change their native
runtime lookup boundary or constructor input.

The issue's discarded diagnostic that called a this-dependent parent method
unbound remains excluded; it is not a valid Node control. These references do not
claim that discarded program as a successful behavior test.
