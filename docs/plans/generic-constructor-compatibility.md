# Distinct generic constructors: bounded relation design (#1905)

## Evidence and disposition

[#1857](https://github.com/nickna/SharpTS/pull/1857) repaired the identical
constructor relation; [#1858](https://github.com/nickna/SharpTS/pull/1858) preserved
anonymous declaration identity through forward checking. Both explicitly left
distinct-constructor structural compatibility open. The linked historical note
contains no exact structural pair or deadline. The retained
`ModuleGenericClassExpressionTests.RuntimeBindingsAndArgumentsArePreserved`
reassignment case uses an explicit `as any as typeof Box` assertion. Its runtime
success is not evidence that unasserted structural assignment is accepted.

New sources in `tests/fixtures/GenericConstructorCompatibility/` pin the missing
relation. The reassignment fixture is an explicitly modified control, not an
unchanged historical reproducer: it removes the assertion, renames the replacement
type parameter and spells out the constructor property assignment so Node can
execute it with type stripping.

**Disposition: finite implementation transfer to
[#1963](https://github.com/nickna/SharpTS/issues/1963).** The current relation only
accepts identical `GenericClass` objects or matching nonzero declaration IDs.
No distinct-constructor relation exists. Replacing that condition with a name or
shape equality check would erase the very private identity repaired by #1858.
The successor must relate the construct signature, result and static side under
one consistent type-parameter substitution.

## Verified reference boundary

Windows ARM64, Node v25.5.0, TypeScript 7.0.2, .NET 10.0.12. Each source was checked
separately with `tsc --target ES2022 --noEmit --skipLibCheck`.

| Fixture | TypeScript | Node stdout for valid source |
| --- | --- | --- |
| `public-pair.ts` | Accepts | `7` |
| `public-reassignment.ts` | Accepts | `first:1`, `second:2` |
| `constrained-pair.ts` | Accepts | `ok` |
| `private-pair.ts` | TS2322: separate private declarations | — |
| `static-pair.ts` | TS2322: incompatible static field | — |
| `narrower-constraint.ts` | TS2322: constructor cannot accept arbitrary target T | — |
| `result-pair.ts` | TS2322: incompatible constructed-instance method result | — |

On unchanged baseline `0ad37b57` and current `6b7cc25e`, both CLI interpretation
and compilation reject every pair before execution, exit 1. The four invalid
pairs retain the correct rejection; the three valid pairs remain unsupported.
No SharpTS runtime or IL verification pass is claimed for those positive sources.
The 24 retained identity/module-constructor tests pass with compiled IL
verification, including the asserted runtime-reassignment case and private
identity/cache regressions. Hosted execution was not exercised.

The [TypeScript compatibility documentation](https://www.typescriptlang.org/docs/handbook/type-compatibility.html)
provides the structural/private-origin context. The actual compiler results above
pin these constructor-value pairs; instance-only rules are not substituted for
constructor/static-side checking.

## Finite implementation

1. Keep the identical-declaration fast path and declaration-sensitive cache keys.
   Add a distinct generic constructor relation after universal `any`/null handling
   and before rejection. Never identify two declarations by their display names.
2. Project each class's construct signature from checked constructor metadata,
   including implicit/inherited constructors, required/optional/rest parameters,
   class type parameters, constraints and defaults. Reuse the existing signature
   alpha-renaming and contextual-instantiation machinery rather than comparing
   parameter names or erasing parameters to `any`.
3. Relate the constructed instance under that same substitution. Compare public
   fields, methods/accessors and inherited members in the required direction;
   preserve private/protected and ECMAScript private-name origins. Do not project
   branded instances to anonymous records without origin metadata. Reject the
   private fixture while retaining valid own aliases and common-base origins.
4. Compare the constructor's required static members with their static visibility
   and declaration origins. Instance-field visibility maps cannot stand in for
   the static maps. Reject the static fixture, including its same-named anonymous
   and module-local variants.
5. Return true only when construct signature, instance result and static side all
   relate. Thread contextual inference through all three; do not independently
   infer a different source type argument for each side. Preserve recursive
   relation termination without caching a name-based successful comparison.
6. Verify that accepted constructor aliases/reassignments use the actual runtime
   binding. The existing asserted reassignment source is a runtime control, not
   permission to hard-code construction to the target type's emitted owner.

## Acceptance and stopping rule

Promote all seven fixtures into positive/negative checker and shared runtime
coverage. Positive assignment must work in an initializer, later reassignment,
function argument and return; exercise single-file and module-local declarations
in both import orders. Add a repeated self-comparison before each invalid pair to
guard compatibility caches, and preserve the existing #1857/#1858 regressions.
Add focused constructor arity/rest/default, private `#name`, static visibility and
generic-base controls where required by the relation above. Use a 30-second case
deadline for runtime tests and report runtime separately from IL verification.

Run affected compatibility/signature/generic constructor suites, CLI/standalone
positive output checks against Node, Release build, code-quality gates and the
actual AOT analyzer baseline. An invalid pair's rejection alone does not prove
the valid relation; a valid pair's IL verification alone does not prove runtime
reassignment.

Stop after this declared distinct-generic-constructor relation passes the pinned
boundary and controls. Repeated class evaluation/computed keys remain #1906;
arbitrary dynamic superclass support remains #1907. General structural class,
conditional-type or inference conformance is outside this successor.
