# Repeated class evaluation: representation decision (#1906)

## Evidence

The #1849–#1851 notes record this remaining behavior without an exact source or
deadline. #1859's retained tests distinguish separate expression syntax nodes;
they do not execute one node twice. The frozen reconciliation's B07 row retains
that distinction. No original repeated-evaluation artifact was recovered.

`tests/fixtures/RepeatedClassEvaluation/` contains explicitly new probes and an
unchanged program from the retained generic-key regression. Windows ARM64,
Node v25.5.0, TypeScript 7.0.2, .NET 10.0.12 were used. Baseline `0ad37b57` and
current `9a431d62` have the same results below.

| Source | Node / CLI interpretation | Verified compiled standalone |
| --- | --- | --- |
| `constructors-only.ts` | `false` | `true`, exit 0 |
| `computed-keys.ts` | `false 2` / `5 5 undefined undefined 5` | `true 2` / `5 undefined 5 undefined 5`, exit 0 |
| `generic-one-definition.ts` | `1` / `1 5 5 undefined` | Same output, exit 0 |
| `identity.ts` | `false` / `false` | IL verification fails: `$Program.$InitScript_identity`, BackwardBranch, Offset 709; no runtime pass claimed |

The constructors-only source isolates identity from the inline prototype-call
verification failure without replacing that retained failure. TypeScript accepts
both identity sources. It rejects both computed-field sources with TS1166:
dynamic field keys lack a simple literal/unique-symbol type. Those are JavaScript
runtime controls accepted by SharpTS and Node, not TypeScript acceptance claims.
The compiled wrong-output cases pass IL verification; that is not runtime parity.

All 153 retained class expression, computed-member, local-class and class-
initialization tests pass with compiled IL verification. Their hosted lifecycle
coverage remains credited to those tests; the new probes were not run through a
hosted factory or separately through the in-process harness.

## Representation decision

**Use a fresh guest constructor/definition object for each evaluation, sharing a
pre-emitted CLR class template.** Keep the syntax-node/declaration identity used
by checking and metadata registries distinct from this runtime definition identity.
TypeScript generic instantiation uses the same guest definition object; different
evaluations of the expression create different objects.

`ILEmitter.EmitClassExpression` currently returns `Type.GetTypeFromHandle` for
the node's pre-collected builder. Capture fields are static. Deferred generic
field keys live on `$Program`; the registrar overwrites them on each evaluation.
Consequently an old constructor cannot retain its own definition state. Renaming
builders or fixing equality alone cannot recover its captured keys.

The definition object owns the evaluation's constructor prototype, guest static
storage, captured environment, ordered computed keys and evaluated superclass.
Generated instances retain a reference to that definition. New/Reflect construction,
class value equality, `prototype`/`constructor`, `instanceof`, property dispatch and
direct optimized construction must use the guest definition where observable.
The CLR template and its checked method tokens remain compilation-owned.

Ordinary type-erased and generic CLR instantiations may share generated bodies,
but cannot define the guest identity or hold mutable per-evaluation keys in a
template-wide static field. Constructor ABI/entry helpers must pass the selected
definition to an instance before field initialization. Existing direct construction
can remain optimized when a single definition is proven, provided the same guest
definition/prototype is attached. Future private-name support must key brands to
definition identity rather than assuming one brand per syntax-node CLR type.

Alternatives rejected: generating a new CLR type at guest runtime requires a
runtime emitter in standalone output and conflicts with AOT constraints; caching
the current `Type` plus overriding equality still shares prototypes and keys;
storing a "current definition" in thread/static state loses old constructors
and suspended executions. A definition wrapper changes runtime representation,
so this investigation transfers implementation instead of claiming a small repair.

## Finite successor tasks

1. **[#1964 — Constructor identity and instance association](https://github.com/nickna/SharpTS/issues/1964).** Introduce the definition
   value/template boundary; route class-expression evaluation, runtime new and
   required direct construction through it. Preserve name/arity, prototypes,
   constructor back-references and definition-aware `instanceof`. Test one node
   twice in ordinary functions, closures, loops, async/generator suspension and
   modules. Keep generic instantiations of one definition associated with that
   same definition. Do not add arbitrary dynamic superclass support (#1907).
2. **[#1965 — Definition-owned captured keys and initialization](https://github.com/nickna/SharpTS/issues/1965).** Depends on #1964.
   Move deferred field/method/accessor keys and class capture snapshots out of
   template-wide static state. Instances of an earlier returned constructor must
   use its own key snapshot after later evaluation. Evaluate keys/heritage/static
   initialization once per definition in source order; instance creation and
   generic type arguments do not reevaluate them. Preserve abrupt/suspended
   definition behavior and module-local owner separation. Keep the exact existing
   `GenericComputedFieldKeys_AreSharedAcrossTypeArguments` expectation.

Promote the two wrong-output probes to shared regression coverage after repair,
with 30-second runtime-case deadlines; match Node in both engines and verified
CLI/standalone output. Run the 153 retained controls plus affected constructor,
generic/static/property/AST paths, Release build, quality gates and actual AOT
analyzer baseline. Report hosted execution, runtime output and IL separately.

Stop after fresh identity and isolated definition keys pass these finite controls.
General class-expression compatibility, arbitrary heritage, private-method values,
new syntax and unrelated conformance failures remain their existing tasks. The
inline prototype-call IL failure is a separate verification defect,
[#1966](https://github.com/nickna/SharpTS/issues/1966); its failure
is retained rather than used to claim this representation work is repaired.
