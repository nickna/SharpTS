# Direct super calls in state machines (#1757)

The four `super_async*`/`super_generator` runtime originals and the hosted
`hosted_super_async.ts` preserve the frozen issue's displayed sources. Fresh
unchanged main rejects all five at IL verification with `ThisMismatch`, plus
object/parent receiver mismatches in the ordinary state machines. Invalid
saved assemblies are not executed. The four extracted-method controls already
verify; the three executable controls retain their exact Node stdout, empty
stderr and clean exit within the original 30-second deadline.

Current compiles and verifies all originals. The four runtime originals print
`11`, `7`, `3 4 5`, and `3`/`4`, respectively, with empty stderr and clean exit.
The hosted original and hosted extracted control verify declarations and
deployment without executing their exports.

The compiler emits a cached bridge on the lexical class. Its own `this` can
legally make the nonvirtual parent call; state machines call that bridge using
their captured receiver. Generic owners implement a nongeneric compiler-only
contract so the caller retains the actual closed owner type. Bridges remain
outside the published JavaScript method registry. Super accesses participate
in lexical `this` capture, and argument temporaries survive await/yield.

Fourteen compiled and fourteen isolated standalone references cover the
originals, lexical owner versus a later override, receiver identity, string
parameters, default parameters, inherited and generic parents, a generic owner,
async arrows in async methods, extra argument side effects and suspension in a
later argument. Full TypeScript compilation and Node confirm every expectation.
Saved outputs require verified IL, exact stdout, empty stderr, no SharpTS
reference/copy and clean exit within 30 seconds. Fourteen additional hosted
controls exercise module emission and hosting references; hosted exports are
not executed. The focused suite passes 84 checks including the retained native
parent-method ownership and inherited-super controls.

The broader run passes 1,588 tests and retains three verifier failures excluded
from passing counts: the existing #1956 numeric closure boxing defect, nested
async arrow mutation, and `independent/ArrowTypedReturn_AsMethodCallback.ts`.
Fresh unchanged-main/current saved compilation rejects the callback fixture
with the same `$TSFunction`/`System.Delegate` mismatch at offset 167. Its forced
in-process diagnostic is at offset 207. It is not executed as a verified saved
guest. The four previously retained direct async super-call tests now pass.
