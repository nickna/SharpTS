# Generator using cleanup (#1785)

`generator_close.ts`, `generator_complete_control.ts` and
`generator_finally_control.ts` preserve the displayed original sources and Node
expectations. Fresh unchanged main and pre-repair current verify both using
programs but omit `dispose`; the explicit-finally control already prints the full
expected output. Saved executions retain the original 30-second limit.

Eighteen TypeScript/Node references cover natural completion, early return,
unstarted generators, exactly-once cleanup, injected throw, receiver identity,
captured methods, multiple declarations, reverse order, nested/shadowed bindings,
nullish resources, getter acquisition, partial registration failures, class
generator methods, for-of closing, catch scopes, no-yield generators and disposal
exceptions. TypeScript 7.0.2 and Node 25.5.0 establish every expectation before
SharpTS execution using ES2022/CommonJS and the explicit disposal libraries.

Resources and captured methods persist in generator fields. Implied finally
blocks use existing suspension and non-local-exit routing without replacing
source yield/loop/closure nodes. Ordinary and generator acquisition share the
same compiler helper; existing emitted disposal runtime ownership is unchanged.

The nested explicit-finally throw control also reproduces a pending-return bug
on fresh unchanged main: cleanup runs but the exception is lost. The repaired
handler routing clears the superseded exit, so both that control and throwing
disposers propagate the original exception after outer cleanup.

Saved checks require verified IL, exact stdout, empty stderr, no SharpTS
reference/copy and clean exit. Hosted checks verify declarations without invoking
exports. This covers synchronous using in synchronous generators; no async
disposal repair or metadata ownership audit closure is claimed.
