# Namespace deletion (#1779)

`original.ts` preserves the source displayed in the frozen issue. Fresh unchanged
main and pre-repair current (including #1778's missing-value repair) both save
verified IL, print `true false`, and exit with empty stderr within the original
30-second deadline. Current prints the unchanged Node result, `true true`.

Eleven compiled references and isolated saved standalone outputs cover repeated
and computed deletion, aliases, unrelated members, added null/undefined,
function and nested values, separate objects, recreated live bindings and
strict function deletion. Non-configurable, sealed and frozen controls preserve
existing entries and throw TypeError in strict functions. Full TypeScript
ES2022/CommonJS compilation and Node confirm every expectation. Saved guest
outputs require verified IL, exact stdout, empty stderr, clean exit within the
deadline, and no SharpTS reference or copy.

Native namespace helpers inspect `_members` independently of Get, proving actual
removal for ordinary and bound keys. Has distinguishes present undefined/null;
Bind seeds storage, Delete updates exported fields, and Set recreates the live
property. Repeated emitted object-deletion helpers cover all four named/computed
and strict/sloppy dispatches, integrity failures and missing deletion on frozen
objects across minimal/optional standalone and hosted configurations. Hosted
coverage verifies metadata/deployment rather than guest execution.

The `independent` directory preserves three diagnostics excluded from passing
counts. Fresh unchanged main and current both reproduce them with verified IL,
empty stderr and clean exit within 30 seconds:

| Diagnostic | Reference expectation | Main/current observation |
| --- | --- | --- |
| `descriptor-value.ts` | `false false 8` | `false false 3` |
| `reflect-sealed.ts` | `false false true 3` | `true true true 3` |
| `script-strict.ts` | `TypeError`, `TypeError`, `true 3` | `true 3` |

Namespace descriptor reads do not expose an overridden value; Reflect's fallback
misclassifies failed sealed deletion when it cannot see the namespace own
descriptor; saved script initialization loses top-level strict deletion behavior.
The repaired storage deletion and native strict/sloppy success/failure coverage
do not claim to repair those separate descriptor/Reflect/script-lowering paths.
