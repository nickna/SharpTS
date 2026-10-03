# Extracted TypedArray fill (#1721)

`original.ts` and `combined-original.ts` preserve both issue programs.
Node v25.5.0 prints `3 3` for the isolated fill source. Unchanged `0ad37b57`
and pre-repair `f09e98f6` outputs compile and verify, then terminate with
`TypeError: undefined is not a function` within the original 30-second limit.
The historical delayed process exit is not reproduced or normalized to a pass.

The repair routes TypedArray method values through function-method property
lookup. Explicit call/apply wrappers check and use the selected TypedArray
receiver, keeping argument order and method return identity. Interpreter fill
also reads its actual receiver instead of the array captured at lookup.
`forwarding.ts` is a new control for another Uint8Array, a Float64Array, start/end
arguments, apply and invalid explicit receivers. Baseline interpretation passes
the original but mutates the wrong array and accepts invalid receivers in this
control; repaired interpretation and compilation match Node.

The existing direct extracted-call path is retained and verified across all
numeric and BigInt kinds, views, copies, set and join. Explicit receiver checks
occur at the call/apply wrapper boundary; this task does not change general
unbound-method semantics or claim a complete TypedArray callback family.

The combined source is still a failed compiled behavior comparison at this
commit: unchanged main prints `7`, `true`, then throws at fill; repaired output
prints `7`, `true`, `3 3`, an empty decoder line, then `9`, with exit zero and
empty stderr. Node requires `A` on the decoder line. That separate gap is #1722;
the combined source is preserved without relaxing its expectation.

All 326 affected TypedArray/property/invocation/wrapper tests pass with compiled
IL verification. Two isolated default-library CLI outputs use `--standalone`,
have no SharpTS reference, execute with zero exit and empty stderr, and retain
the original 30-second execution deadline. New compile limits are sixty seconds.
Separate TypeScript 7.0.2 and Node references accept the original and forwarding
sources. No hosted execution or historical timeout root cause is claimed.
