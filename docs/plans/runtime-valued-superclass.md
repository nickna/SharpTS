# Runtime-valued superclass: bounded representation design (#1907)

## Evidence and disposition

The #1849–#1851 progress notes and frozen reconciliation B08 retain an awaited
runtime-superclass gap, but do not expose its exact source or original deadline.
No original artifact was recovered. The new sources in
`tests/fixtures/DynamicSuperclass/` are explicitly investigation controls.

On unchanged baseline `0ad37b57` and current `dced3a75`, the two positive controls
compile and pass IL verification, but standalone execution ignores the selected
parent. Windows ARM64/.NET 10.0.12, Node v25.5.0 and TypeScript 7.0.2 were used.
TypeScript accepts all three final fixtures with
`--target ES2022 --noEmit --skipLibCheck`.

| Fixture | Node / CLI interpretation | Compiled CLI/standalone |
| --- | --- | --- |
| `awaited-parent.ts` | `right` / `2 right` / `true` / `true false` | IL passes; `rejected undefined is not a function`, exit 0 |
| `observed-parent.ts` | `right` / `function right` / `true` / `true false` | IL passes; `undefined undefined` / `false` / `false false`, exit 0 |
| `abrupt-parent.ts` | `parent ` (empty initialization marker), exit 0 | Compilation fails: `Value cannot be null. (Parameter 'key')`, exit 1 |

The observer control avoids stopping at the missing inherited method and exposes
the missing parent-constructor side effect, static inheritance, constructor
prototype and `instanceof` results independently. The abrupt control uses an
explicit `Promise.reject<any>` and typed catch; an earlier exploratory version
with inferred `never` was rejected by TypeScript and is not the final acceptance
source. Zero exit status after the positive fixture's catch handler is not a pass.

The 13 retained known-parent/awaited-heritage/owner-identity tests pass with compiled
IL verification. Their awaited comma-expression programs retain a statically
identifiable final base symbol; they do not prove runtime parent selection.
The new probes were not run through a hosted factory or separately through the
in-process harness. No behavior repair is claimed by this investigation.

The finite implementation successor is
[#1967](https://github.com/nickna/SharpTS/issues/1967), dependent on #1964.

## Representation decision

**Store and use the evaluated superclass in the guest class definition, using
definition-aware construction/prototype dispatch for truly runtime heritage.**
Build on #1964's guest constructor/template boundary. Keep a statically resolved
CLR inheritance path where the checked declaration proves the parent; use a
separate guest dynamic path when the parent value can vary at evaluation.

`EmitClassHeritageExpression` currently evaluates the heritage value and discards
it. Class collection chooses a CLR base from a leaf name/checked static parent.
A CLR template cannot change its base after emission. Guessing a class from the
parameter's spelling, choosing the first possible base or discarding the awaited
value cannot implement the recorded behavior.

For the bounded ordinary-user-class case, evaluate/await heritage once, validate
that it is a supported constructor, and retain its guest constructor definition
and prototype in the child definition. Set the child constructor's guest prototype
to the selected parent constructor and the child instance prototype's parent to
the selected parent prototype. Constructor lookup, static inheritance, inherited
methods and `instanceof` follow those guest links, not CLR assignability alone.

Base initialization must operate on the child's original guest receiver. Add
checked constructor/method entry adapters for the supported emitted user-class
templates that receive that guest receiver explicitly. A composed separate base
object would change `this`, field state and identity; invoking an instance CLR
method on a child that is not its CLR subtype would fail. Adapters should reuse
checked class/member metadata and ordinary body semantics while keeping existing
static CLR dispatch for proven parents. Do not perform Reflection.Emit at guest
runtime or depend on the compiler assembly in standalone output.

Static initialization runs only after heritage succeeds. A rejected await exposes
its original guest error and leaves the initialization marker empty. Analysis,
local-class collection and async emission must represent the runtime parent
explicitly; a missing leaf name must never become a null dictionary key.

## Finite implementation acceptance

The successor depends on #1964 and covers the ordinary emitted user classes in
these fixtures: field initialization, ordinary constructors/methods, static field
inheritance, constructor/instance prototype links and definition-aware `instanceof`.
Support the same runtime-selected parent at a class expression and a local class
declaration. Add two calls selecting Left then Right, real pending suspension,
heritage evaluation count/order, a derived own field/method, base constructor
argument forwarding and an inherited `super` call on the original receiver.

Promote all three fixtures to shared regression tests with 30-second execution
limits. Both engines and verified CLI/standalone output must match Node. Keep the
13 retained known-parent controls and generic/computed one-definition regressions
passing. A compiler exception or a caught missing method is not success.

Run affected heritage/constructor/prototype/class/generic/state-machine tests,
Release build, quality gates and actual AOT analyzer baseline. Report runtime,
serialized IL and hosted execution separately. Preserve original failing outputs
when comparing baselines.

Stop at the specified ordinary-user-class runtime-parent representation and
controls. Arbitrary host/CLR constructors, exotic built-in subclassing, proxy
constructor traps, constructor-return replacement, general mixin typing and new
private-member features are separate work. Repeated definition key isolation is
#1965, structural constructor compatibility is #1963; neither is silently counted
as repaired here.
