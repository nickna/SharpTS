# Missing namespace members (#1778)

`original.ts` preserves the source displayed in the frozen issue. Fresh unchanged
main and pre-repair current both save verified IL, print `false object 3`, and
exit with empty stderr within the original 30-second deadline. Current prints
the unchanged TypeScript/Node expectation, `true undefined 3`.

Seven references cover dot/computed lookup, stored and assigned null, exported
undefined, generator reads, and private function visibility from #1775. Full
TypeScript ES2022/CommonJS compilation and Node confirm every expectation.
Compiled and isolated standalone tests require verified IL, exact stdout,
empty stderr and clean exit. Saved outputs have no SharpTS reference or copy.

The native scoped helper receives only its module, namespace owner and undefined
field. It checks exact sentinel identity for absent keys while preserving stored
null and live bound values. A reused emitter verifies fresh declaration ownership
and saved IL for minimal, optional and minimal-again standalone/hosted outputs.
Hosted coverage checks metadata/deployment; guest execution is standalone.
Actual deletion remains the separate #1779 task.
