# Numeric arrays inside local records

`LocalRecordArrayAnalyzer` extends the existing `$Array` numeric store to a
bounded set of dense, nonempty literals. The canonical allocation kernel is
eligible without modifying its interface, loops, or allocations. This follows
the conservative escape rule in [number-array-unboxing.md](number-array-unboxing.md).

The array remains one `$Array` object with one owned `double[]`. It is never
replaced by a separately mutable `List<double>`. Ordinary fixed-length literals
use the capacity constructor and private append helper; spreads, holes, and
suspending elements retain their existing construction paths.

## Boundary audit

| Boundary | Treatment for newly eligible literals |
| --- | --- |
| Fresh record pushed into a uniquely named const local list | Allowed only after the existing stable-push analysis accepts the literal. |
| Const alias of a list element | Allowed when its binding is unique in the function. |
| Scalar record fields and indexed numeric-array reads | Allowed. Receivers and keys are evaluated once. Numeric reads guard storage, index integrality, and bounds; fallback preserves the original key. |
| Raw indexed result, including `any` | Allowed; the result is boxed without coercing an unexpected dynamic value. The array itself cannot escape. |
| Array/record/list aliases, arguments, returns, exports, or globals | Rejected; these literals use boxed storage from construction. |
| Closures, callbacks, nested classes/functions, `eval` | Reject the containing function. |
| Iteration, serialization, structured cloning, worker messaging, .NET interop | Reject the escaping use. No new numeric array reaches an inherited `List<object>` consumer. |
| Indexed writes, deletion, length writes, freeze/seal, reflective mutation | Reject the use. Descriptor and Array prototype mutation features also disable this optimization globally. |
| `for…of`, `for…in`, catch bindings, destructuring assignment | Reject the containing function until lexical binding analysis covers these forms. |
| Spread, holes, or elements that suspend | Retain the existing literal emitter. |
| Literal elements read from opaque properties/calls | Retain boxed storage even if annotated `number`; eligibility is limited to numeric constants, native numeric bindings, and arithmetic. The emitter verifies actual local/parameter storage, including locals widened by `any`/`undefined` assignments. |

The analysis checks all uses in the function, not only the uses preceding the
literal. A later escape prevents numeric construction from the outset. General
numeric array support and its existing `EnsureBoxed` transitions are unchanged;
this optimization adds no new escaping numeric representation.

Interface reads select an existing compact carrier by named fields and guard
its actual CLR type, materialization state, and descriptors. Declaration order
does not imply slot order. Only numeric consumers coerce generic fallbacks;
ordinary property reads preserve runtime values even when an interface annotation
has become inaccurate through `any` mutation.

The allocation test measures the whole local-record kernel after warmup and
allows a broad byte range. Semantic tests cover escaping aliases, callbacks,
descriptors, holes, length changes, special numbers, and invalid indices in both
execution modes. Extending eligibility requires auditing the new boundary and
adding semantic coverage before enabling it.
