# Unicode string delegation — #1730

`original.ts`, `numeric.ts`, `array-control.ts` and `spread-control.ts` retain
the exact issue sources and Node expectations. Fresh unchanged main
(`0ad37b57`) IL-verifies and exits normally with empty stderr, but the original
splits the supplementary character and the numeric probe prints `3 1 55357`.

Compiled synchronous string delegation now uses the existing StringIterator
instead of CLR UTF-16 character enumeration. The interpreter shares one
code-point iterator between its string intrinsic, spread and loop paths,
preserving lone surrogates rather than replacing them via EnumerateRunes.
Fixtures cover mixed BMP/supplementary/lone-surrogate input, empty and nested
delegation completion, return/finally, and operand exceptions. Shared tests
exercise both engines; eight standalone saved tests require verified IL,
absence of runtime references/copies, exact Node stdout, zero exit and empty
stderr within 30 seconds. Windows uses a PowerShell UTF-8 child-console
launcher for these tests: the identical saved original prints the correct
emoji outside the testhost, while its default console replaces it with `??`.
The launcher preserves the guest source, output expectation and deadline.

All nine reference fixtures pass Node and TypeScript with explicit non-strict
settings. `throw-finally-independent.ts` is an additional retained failure,
excluded from passing conformance counts. Node throws TypeError because a
string iterator has no throw method; unchanged main and the current compiler
instead propagate the injected value. The Unicode first-value length improves
from 1 to 2, but the independent TypeError mismatch remains. No missing-throw-
method protocol, compiled async string-delegation, or hosted execution repair
is claimed.
