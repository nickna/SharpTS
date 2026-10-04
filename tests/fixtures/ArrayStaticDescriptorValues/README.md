# Array static descriptor identity — #1802

`original-alias.ts` and `original-direct.ts` retain the exact issue sources.
Node prints `true true` / `true 2` and `true true`, respectively. The original
historical compilers and fresh unchanged main (`0ad37b57`) saved and IL-verified
both programs but printed `false true` for the Array comparison, with normal
exit and empty stderr. The fresh probes use the original 30-second deadline.

Compiled Array descriptors synthesized `undefined` as the `isArray` value.
They now call the existing static lookup helper and its MethodInfo-keyed
function factory. The interpreter's Array methods use the established
BuiltInStaticBuilder lookup, preserving their non-constructor branding.
Descriptor lookup has no independent function cache.

The other fixtures cover computed and aliased access, repeated descriptors,
function identity, name/length, data-property attributes, invocation, absent
function prototype, and the passing Object.assign control. All four pass
TypeScript and Node. Shared tests exercise both engines; isolated saved tests
verify IL, omit SharpTS references and runtime copies, and require Node output,
exit zero and empty stderr within 30 seconds. Native lookup tests retain the
original cache checks and compare Array descriptor values across emitter reuse,
optional families, and standalone/hosted declarations.

This repair concerns `Array.isArray`; no compiled Array.from/fromAsync/of
descriptor correction or hosted guest execution is claimed.
