# Async-generator throw injection — #1732

`original.ts` and `caller-control.ts` retain the exact issue programs and Node
expectations. Fresh unchanged main (`0ad37b57`) passes saved IL verification
and exits within the original 30-second deadline with empty stderr. Its
original prints only `1`; the caller control prints `1`, `rejected 7`, `after`.

Compiled `throw()` now injects its value at an ordinary suspended yield and
drives the existing result/promise path. The resumed body uses existing
catch/finally routing, including awaiting handlers and cleanup. Fixtures cover
object identity, nullish/falsy caught values, nested cleanup, repeated throws
from a suspended catch, uncaught errors, and not-started/completed controls.
Nine fixtures run in both engines; the next/return control additionally runs
in compiled mode. Ten standalone saved tests require verified IL, absence of
runtime references/copies, exact Node stdout, zero exit and empty stderr
within 30 seconds. All eleven references pass Node and TypeScript.

`completed-null-rejection-independent.ts` retains a separate failure: unchanged
main and current compiled output omit the last catch log when awaiting
`throw(null)` on a completed generator. The interpreted next/return control
also fails unchanged on main by exposing `GeneratorReturnException`, while
compiled and saved output match Node. These observations are excluded from
passing conformance counts for the affected engine. No general async request
queue, delegated throw-method protocol, interpreted return, completed null
rejection, or hosted guest execution repair is claimed. Active delegation
keeps its existing throw path rather than leaving an injection pending for an
unrelated later ordinary yield.
