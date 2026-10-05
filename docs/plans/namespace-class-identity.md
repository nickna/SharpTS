# Namespace class identity investigation (#1908)

## Evidence

The #1599 note [5851883354](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5851883354)
records namespace duplicate property-dispatch declarations. The retained #1853,
#1854 and #1859 bodies/review evidence do not provide the original namespace
source or deadline. Earlier artifact searches recovered no exact source.
#1859's retained `ClassExpressionOwnerIdentityTests` are class expressions, not
namespace class declarations, so that repair does not establish a B09 duplicate.

New controls in `tests/fixtures/NamespaceClassIdentity/` are labelled investigation
sources. At unchanged `0ad37b57` and `a90e3dc7`, sibling namespace classes named
`Box` plus a top-level `Box` interpret as `1 2 9`, but compilation fails with
`Invalid or duplicate class property dispatch declaration`, exit 1. Both generic
and non-generic controls expose this collision. TypeScript 7.0.2 accepts them;
the emitted ES2022/CommonJS reference runs in Node v25.5.0 with `1 2 9`.

Two modules exporting `Library.Box` use different module-qualified class names,
so compilation passes IL verification in both import orders. Execution instead
throws `TypeError: Cannot read properties of null (reading 'Box')`, with empty
stdout; it is not a runtime pass. Interpretation rejects the namespace export
with `Cannot get name of declaration type Namespace`, exit 1. TypeScript accepts
both orders and Node prints `1 2`. These failures remain visible; they are related
to #1776's existing imported namespace value/member path, whose exact original
source will be verified separately under that child. They are not proven exact
duplicates of its function-member case.

The retained namespace/class-expression/namespace-runtime selection has 124
passes and six compiled IL failures on both unchanged `0ad37b57` and current
`a90e3dc7`, with `SHARPTS_VERIFY_COMPILED=1`. The failing existing NamespaceTests
are the class method/accessor/constructor namespace-variable cases,
`NamespaceWithClass`, `DeepNestedNamespaceClass` and `NamespaceClassInheritance`.
All retain object/System.Type StackUnexpected errors, the same boundary recorded
by #1781. They remain failures, not 130 verified passes. No hosted execution was
performed for the new controls.

After #1776 and #1781, the same sources were rechecked without changing their
reference expectations. The six existing namespace-class verifier failures now
pass, and #1781's exact original prints `3 5 true` after IL verification. Both
sibling controls still reject duplicate class property dispatch. The module
controls now verify IL and exit cleanly with empty stderr within 30 seconds,
but `main-left.ts` prints `2 2` and `main-right.ts` prints `1 1`, compared with
the unchanged Node expectation `1 2`. This confirms that the remaining design
boundary is declaration/namespace identity, after fixing export initialization
and the separate construction stack mismatch. These module diagnostics remain
excluded from passing behavior counts.

## Finite implementation boundary

Give namespace class declarations canonical emitted owners tied to checked
declaration identity, independently of their observable simple names. Retain
namespace/module context per declaration, including later class-method,
constructor and property-dispatch phases where `_currentNamespacePath` is no
longer active. Route checked class/generic constructor and instance references
through that owner rather than a last-writer simple-name alias.

`DefineNamespaceFields` currently calls `DefineClass` without adding namespace
identity. `GetQualifiedClassDeclarationName` qualifies only the module unless a
block-scoped declaration mapping exists. `ResolveClassName` and `TypeMapper`
likewise resolve by simple/module names. This explains the sibling declaration
collision, but merely suppressing duplicate validation or prefixing one registry
does not prove correct constructor/member ownership.

Repair the named collision across `Left.Box`, `Right.Box` and top-level `Box`,
with generic and non-generic variants, nested namespaces, reopened namespaces
containing different declarations, same-named private storage, inherited members
and methods constructing their own namespace class by bare name. Preserve
observable class names, type checking, private origins and constructor arguments.
Do not merge independent declarations or relax checked registry completion.

For the two retained module controls, coordinate with #1776's existing export
initialization investigation. Namespace values must remain module-local and
export the correct class in both orders. Fix the interpreter namespace declaration
name path for these same inputs. Keep #1781's object/System.Type construction
probe separate and run its original regression when the constructor path changes.
Do not create a second general module-metadata ownership migration (#1894).

Acceptance requires the four new fixtures to match the documented Node output in
both engines, API module execution and IL-verified standalone output within
30 seconds, preserving the failing baseline outputs. Add affected namespace/
class/generic/module/private regressions, keep #1859's expression controls, and
run Release, quality and actual AOT baseline checks. Report hosted execution only
if exercised. Stop at these declaration/lookup/export paths; broad namespace
assignment/deletion/enum behavior belongs to the existing epic children.

This is an explicit implementation transfer, not a behavior repair. Original
evidence remains missing; the new controls demonstrate the same diagnostic family
without claiming to reconstruct the historical source.

The finite successor is [#1968](https://github.com/nickna/SharpTS/issues/1968).

## Implementation verification (#1968)

The checked declaration now supplies the canonical emitted class owner. Namespace
and module scopes are retained through method, constructor and property-dispatch
emission, and checked references resolve that owner. Namespace object and variable
storage is module-local. Interpreter exports accept namespace declarations, and
reopened namespaces retain earlier exported class bindings. Emitted class metadata
keeps the guest simple name independently of the canonical CLR name.

Both sibling fixtures now print `1 2 9`; both module import orders print `1 2`.
These unchanged fixtures pass API execution in both engines and CLI compilation,
IL verification and standalone execution, with empty stderr and a 30-second
per-case deadline. Additional shared controls cover nested/reopened namespaces,
private storage/origins, typed signatures, inheritance, bare self-construction,
module namespace variables and constructor aliases with observable names.

The affected namespace/class-expression/generic/private/module selection passes
711 tests with `SHARPTS_VERIFY_COMPILED=1`, including #1781's original standalone
construction regression. The quality gate passes. Release and actual AOT analyzer
baseline checks pass; the analyzer inventory has zero warnings. Hosted execution is not claimed
for the new fixtures. The historical baseline evidence above remains unchanged.
