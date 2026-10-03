# Callable Proxy wrapper programs (#1715)

The two `*-original.ts` files preserve the issue's exact programs. Node v25.5.0
expects `12 30 48` for the delegating trap and `3 9 15` for the direct-result trap.
Separate TypeScript 7.0.2 checks accept both originals and the new controls.

The historical Windows thirty-second timeout is not reproduced on current main.
Fresh runtime-bearing outputs at unchanged `0ad37b57` and pre-repair `a9baf9a0`
compile and pass IL verification, then terminate with a guest bind-validation
TypeError, not a timeout or passing output. `operation-isolation.ts` is a new
labelled control: baseline saved output prints `call 12`, `apply 30`, then
`bind rejected true`; Node and repaired output print `call 12`, `apply 30`,
`bind 48`. This isolates the current boundary without assigning a root cause to
the older timeout.

Baseline interpretation also exposes receiver loss at bind: the delegating
original prints `12 30 NaN`, and the direct original throws while reading `x`
from a null receiver. The repair retains the explicit receiver in both boxed
and RuntimeValue bound-call paths, including null receivers, and rejects a
Proxy whose target is non-callable. Compiled bind now asks the soft runtime's
IsCallable contract before constructing its receiver-preserving bound wrapper.

Both originals and `controls.ts` run in both API engines and isolated CLI output.
Controls retain a direct function, nested delegating Proxies, prepended bound
arguments, object exception identity and non-callable Proxy rejection. Isolated
tests retain the normal runtime-bearing deployment, compare the copied
SharpTS.dll bytes with the compiler runtime, verify IL, require empty stderr and
zero exit, and keep the original thirty-second execution deadline. The new
compilation limit is sixty seconds. No standalone runtime-independence or hosted
execution is claimed.

The broader 129-case run has 128 passes and one existing
`Proxy_HasTrap_TruthyCoercion(Compiled)` union getter IL failure (MethodFallthrough
at offset 77 and StackUnderflow at 64), reproduced on unchanged main. The final
36 focused controls also pass, including explicit checks of both interpreter
bound-call entry points. This is a repair of current original-source behavior;
it does not claim isolation of the historical timeout's root cause.
