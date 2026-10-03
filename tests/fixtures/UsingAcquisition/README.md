# Using acquisition (#1784)

`accessor_capture.ts` and `method_capture.ts` preserve the displayed original
sources. Node expects `get`, `body`, `5` and `body`, `first`. Fresh unchanged main
and pre-repair current verify but print `body`, `get`, `5` and `body`, `replacement`.
All saved executions retain the original 30-second limit.

Twelve TypeScript/Node references cover acquisition before the body, captured
method identity, original receiver, nullish resources, getter exceptions, partial
registration, multiple declarations, reverse cleanup order, deletion, nested
scopes, function returns, inherited accessors and bound methods. Invalid or
missing methods fail during registration. TypeScript 7.0.2 and Node 25.5.0 confirm
the expectations using ES2022/CommonJS with ES2022, ESNext.Disposable and DOM.

Compiler cleanup invokes the saved method without another property read. A native
compiled-function test verifies that a selected CLR IDisposable fallback survives
later installation of a guest method, and that a selected guest method survives
removal without additionally calling IDisposable. Existing disposal runtime
ownership, helper signatures, scoped inputs and declaration order remain unchanged.

Saved checks require verified IL, exact stdout, empty stderr, no SharpTS
reference/copy and clean exit. Hosted checks verify declarations without invoking
exports. Generator cleanup and ordinary-loop protected-region branching are
tracked separately by #1785 and #1786.
