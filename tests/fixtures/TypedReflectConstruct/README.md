# Typed Reflect.construct target (#1718)

`original.ts` is the unchanged issue source. Node v25.5.0 expects:

```text
7 true
true
true
true
true
```

The default-library CLI rejects the typed class target before output generation
at unchanged `0ad37b57` and pre-repair `d4b1c62b`. `dynamic-control.ts` changes
only Point to Point as any and compiles, verifies and executes on unchanged main;
its expected output is identical. No expectation in the original is relaxed.

TypeScript's broad Function type is now represented distinctly from an ordinary
variadic call signature, so constructor values satisfy Function without becoming
assignable to an arbitrary callable signature. The distinction survives generic
substitution and uses a distinct compatibility cache representation. The new
`function-type-control.ts` covers a Function-typed class, a generic Function
constraint and a construct-signature target, retaining instance identity.
CLI negatives still reject an ordinary callable assignment, plain object,
primitive and class-instance targets. The callable-assignment control first
accepts a broad Function assignment to exercise cache separation.

The original also exposes an interpreter constructor-capability defect:
Function.prototype.call was accepted by Reflect.construct. A baseline noLib
interpreter control prints false on the last line. Call/apply/bind intrinsics
now carry the existing non-constructor flag, preserving callable use while
rejecting construction. `intrinsic-controls.ts` checks all three; Node prints
three true lines.

All four positive sources run through both API modes, default-library CLI
interpretation and IL-verified isolated standalone outputs with no SharpTS
assembly reference. Separate TypeScript 7.0.2 checks and Node references accept
the sources. Isolated compilation/execution limits are 60/30 seconds; the
historical issue did not specify a deadline. The affected 334-case run has
333 passes and one unchanged namespace-construction IL failure, independently
reproduced on unchanged main. No hosted execution or namespace repair is claimed.
