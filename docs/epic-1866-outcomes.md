# Frozen behavior backlog outcomes (#1866)

This record accompanies the consolidated implementation PR. Each child is handled
independently and committed after its verification. The starting baseline is
`0ad37b574e3c41d267300ada003d63d0f8026562` (the current main when work began).
The epic's frozen source baseline remains `83a41096108fe6de739fa7cfcb148c4bb1193121`.
Design results and prior repairs are distinguished from new behavior repairs.

## #1900 — Interpreter non-writable static descriptors

**Outcome: repaired.** The original progress note links #1861 but contains no
source text. The unchanged `NonWritableDescriptorWinsOverFieldStorage` source
retained by that PR is preserved in `ClassStaticDescriptorTests`: after defining
`Box.count` as 12 and non-writable, assigning 9 through an alias must print
`12` and `true`. On the starting baseline the interpreter printed `9` and `true`;
compiled execution already printed the expected result.

Interpreted class static properties now share storage with the ordinary property
descriptor implementation. Assignment respects own and inherited non-writable
descriptors, throws guest TypeError in strict mode, and preserves writable
inherited shadows. Dot, index and read-modify-write dispatch use the same
descriptor checks. Descriptor reads and partial redefinitions retain the value
and attributes; deletion observes configurability.

Verification on Windows ARM64, .NET SDK 10.0.401/runtime 10.0.12:

- 32 focused class/static/property tests passed, with compiled IL verification.
- 891 affected class, accessor, decorator and assignment tests passed.
- Repository code-quality gates passed (zero errors).
- Twelve selected Test262 descriptor/class metadata cases per engine matched an
  isolated build of the unchanged baseline at pinned corpus
  `d5e73fc8d2c663554fb72e2380a8c2bc1a318a33`. The ten ordinary descriptor cases
  passed in each mode; class-name metadata retained two interpreted failures,
  one compiled runtime error and one compiled pass. Those existing non-target
  failures are not counted as successful conformance tests.
- Node v25.5.0, CLI interpretation and CLI compilation followed by standalone
  execution all printed `12` and `true` for the retained source; CLI IL
  verification passed. The original 30-second test-harness deadline is retained.
- Hosted execution was not exercised: this change is an interpreter property
  dispatch repair and adds no deployment or hosted ABI behavior.

The controls uncovered independent pre-existing compiled gaps, tracked outside
the frozen epic as [#1958](https://github.com/nickna/SharpTS/issues/1958)
(inherited non-writable static descriptors) and
[#1959](https://github.com/nickna/SharpTS/issues/1959)
(attribute-only static definitions losing the existing value), and
[#1960](https://github.com/nickna/SharpTS/issues/1960)
(strict compiled read-modify-write operations ignoring rejection). Those failing
compiled observations are not counted as repaired or passing coverage here.

## #1901 — Private-method values

**Outcome: investigation and finite design transfer to
[#1961](https://github.com/nickna/SharpTS/issues/1961).** The linked notes and
retained artifacts did not recover the historical source bytes. New, explicitly
identified generic/non-generic fixtures demonstrate the present checker gap on
both the starting baseline and `a99c0c17`. TypeScript 7.0.2 accepts them;
Node v25.5.0 establishes shared callable identity and caller-selected receivers.
Both SharpTS CLI paths reject them before execution, exit 1. The outside-access
control is rejected by both checkers.

The [bounded implementation design](plans/private-method-values.md) specifies
lexical private ownership, unbound callable identity, canonical guest-this
compiled entry points, generic erasure, metadata lifecycle, and a finite
positive/negative acceptance matrix. No production behavior is claimed fixed,
and no in-process compiled, standalone runtime or hosted passing result is
credited to these rejected programs. The original deadline/source are unknown;
new CLI diagnostics completed promptly, without altering historical evidence.

## #1902 — Inherited private-method interpreter dispatch

**Outcome: repaired.** The unchanged `PrivateBrandsSpanTypeArgumentsAndRemainLexical`
source retained by #1854 is copied into shared coverage. Its compiled execution
already printed `ok`, `42`, `7`, `brand`, `rhs`; the interpreter printed `wrong`
in place of `7` on `b4400b61`. New same-spelled base/derived private member and
closure/suspension controls independently reproduced the receiver-based dispatch.

Method wrappers now retain their lexical declaring class through binding and
invocation. Invocation environments carry that private owner without adding a
variable scope, so closures and suspended generators keep their declaration.
Private method calls check the declaring owner's brand before dispatch; private
field reads/writes in that method use its same owner. Derived instances receive
each ancestor's separate private storage on the original receiver. Implicit
constructor handling avoids allocating and initializing a redundant base instance.

Verification on the same Windows/.NET environment as #1900:

- 101 affected private member tests passed with compiled IL verification,
  including the retained source, generic type arguments, module-local owners,
  illegal receiver rejection, initialization count and suspension.
- Four single-file controls matched Node in CLI interpretation and verified
  standalone execution (zero exit status); the retained source kept its exact
  stdout and the 30-second test-harness budget.
- Test262 `private-method-brand-check.js` and
  `private-method-brand-check-super-class.js` changed from Fail to Pass in the
  interpreter. Two static-private controls retained existing interpreted Fail
  results. All four compiled outcomes matched the unchanged baseline (two Pass,
  two RuntimeError). The retained compiled superclass probe in shared coverage
  passes independently; the failing Test262 program is not counted as repaired.
- Code-quality gates passed with zero errors; the actual AOT/trim/single-file
  analyzer inventory is zero and matches the enforced repository baseline.
- The full hermetic core run completed: 23,438 passed, three skipped and one
  failed. The sole failure is the documented Windows ARM64 compiled escaped
  numeric-closure crash, [#1956](https://github.com/nickna/SharpTS/issues/1956),
  already reproduced on unchanged main and outside this epic's frozen scope.

The source notes did not expose the original standalone artifact, so that artifact
is not credited with fresh execution. The retained regression source is explicit;
hosted execution was not exercised. Static-private receiver semantics and the
transferred private-method-value implementation remain separate work.

## #1903 — Private-in brand checks

**Outcome: finite implementation transfer to
[#1962](https://github.com/nickna/SharpTS/issues/1962); behavior remains unsupported.**
The original #1599/#1853 note has no recoverable source, output or deadline.
New investigation fixtures explicitly distinguish instance/static private fields
and methods, generic constructor aliases, derived/foreign objects, proxies,
primitive errors, operand evaluation and invalid lexical names.

On unchanged `0ad37b57` and `b2d4a987`, CLI interpretation and compilation reject
all five at parsing (`Expect expression`, exit 1). Node v25.5.0 matches the
documented positive outputs; TypeScript 7.0.2 accepts those three sources and
rejects the outside/undeclared controls with TS18016/TS2339. Parser rejection of
the negative files is not credited as semantic validation. No SharpTS runtime,
IL-verification or hosted pass is claimed.

The finite design in `docs/plans/private-in-brand-checks.md` names each affected
AST/checker/interpreter/emitter boundary and acceptance control, including generic
brand storage and field installation timing. The successor is separate from
private-method values and repeated class-evaluation identity. Fixtures and
reference commands were exercised; `git diff --check` passed. No production code
changed, so the previously completed core/quality/AOT checks remain applicable.

## #1904 — Generic static-private lookup

**Outcome: demonstrated lookup gap repaired; historical source remains missing.**
The #1853/#1854 notes do not provide the original generic static-private source
or deadline. #1861's retained tests cover TypeScript `private static` visibility
and public static storage, not the reported ECMAScript `static #name` case. New
controls on unchanged `0ad37b57` and `ebc68d10` rejected generic static-private
field reads/writes and method calls before execution. Node and TypeScript accept
them. They are explicitly new controls, not recovered historical reproducers.

The checker recognizes the generic constructor while retaining its lexical
declaration identity, field type and method argument checks. Static private
metadata now resolves to the same closed, type-erased declaring owner used by
public static members. Ordinary, async, generator and async-generator emission
use those field/method tokens. A typed generic constructor alias takes static
dispatch even when the class also has instance-private storage.

Verification:

- 218 affected tests passed with compiled IL verification, including generic
  and non-generic controls, distinct type arguments, module-local same-named
  owners, aliases with instance-private storage, closure/suspension and negative
  type/arity/access checks. Release builds completed with the existing NU1902
  warning and no errors.
- Four single-file controls match Node in CLI interpretation and verified
  standalone execution with zero exit status. The mixed-storage alias control
  additionally matches Node's `7` after compilation and IL verification.
- Five pinned Test262 static-private controls were compared with unchanged
  baseline: all compiled outcomes remain identical (three Pass, two RuntimeError).
  Interpretation has four Pass and one Fail; the inner-arrow improvement from
  baseline's three Pass/two Fail belongs to the earlier #1902 lexical-owner repair.
  Remaining static receiver errors are preserved, not counted as repaired here.
- Final code-quality gates and the actual AOT/trim/single-file analyzer baseline
  passed with zero errors and zero analyzer warnings.

No hosted execution is claimed. The full core run recorded under #1902 predates
this checker/emitter change; this child uses the affected suite above. Broader
static-private receiver semantics and the separately transferred private-method
value/brand-query features remain outside this focused lookup repair.

## #1905 — Distinct generic constructor compatibility

**Outcome: finite implementation transfer to
[#1963](https://github.com/nickna/SharpTS/issues/1963); valid distinct pairs remain rejected.**
#1857 repaired identical constructors and #1858 preserved declaration identity.
Their bodies and retained regressions explicitly leave structural assignment
between distinct constructors open. The historical note has no exact pair or
deadline. The retained asserted runtime-reassignment source is identified in
`docs/plans/generic-constructor-compatibility.md`; the new unasserted fixture is
labelled as a modified control, not an unchanged historical reproducer.

Seven fixtures pin the boundary using TypeScript 7.0.2. Three public/equal-constraint
pairs are accepted and execute in Node v25.5.0 with documented outputs. Four
private-origin/static-type/narrower-constraint/instance-result mismatches are
rejected with TS2322. SharpTS baseline `0ad37b57` and `6b7cc25e` reject all seven
in both CLI modes before runtime, exit 1. Positive runtime or IL passes are not
claimed; the invalid pairs' rejection is preserved.

All 24 retained generic identity/module-constructor tests pass with compiled IL
verification. The finite successor design requires one consistent generic
construct-signature/result/static-side relation, declaration-sensitive caching,
private origin preservation and runtime binding checks. No production code
changed; reference fixture commands and `git diff --check` passed. Hosted
execution was not exercised.

## #1906 — Repeated class evaluation identity and captured keys

**Outcome: investigation/design completed, implementation transferred to
[#1964](https://github.com/nickna/SharpTS/issues/1964) and dependent
[#1965](https://github.com/nickna/SharpTS/issues/1965); compiled behavior remains wrong.**
The historical notes do not expose the original source/deadline. New labelled
probes establish both gaps on unchanged `0ad37b57` and `9a431d62`: interpretation
matches Node, but IL-verified standalone output reuses one constructor (`true`
instead of `false`) and replaces the earlier constructor's computed-key snapshot.
The existing generic one-definition program still matches its original output
across different type arguments. TypeScript accepts the identity probes and
rejects the dynamic computed field keys with TS1166; those keys are explicitly
JavaScript runtime controls, not TypeScript acceptance claims.

The bounded decision in `docs/plans/repeated-class-evaluation.md` uses fresh guest
definition values with pre-emitted CLR templates, definition-associated instances
and per-definition keys/captures. It rejects runtime type emission and shared
"current definition" state. The two implementation issues have finite controls,
dependencies and stopping rules; this design is not described as a repair.

All 153 retained class-expression/computed/local-class/initialization tests pass
with compiled IL verification. The inline prototype comparison additionally
fails CLI verification with BackwardBranch at Offset 709 on both baselines;
that complete failure is retained in `identity.ts` and separately tracked as
[#1966](https://github.com/nickna/SharpTS/issues/1966), outside the frozen epic.
No runtime pass is credited to that artifact. No production code changed;
reference commands and `git diff --check` passed. The new probes were not
executed through a hosted factory or separately via in-process compilation.

## #1907 — Runtime-valued dynamic superclass

**Outcome: investigation/design completed, implementation transferred to
[#1967](https://github.com/nickna/SharpTS/issues/1967); compiled inheritance remains wrong.**
The original progress note and retained reconciliation expose no exact source
or deadline. Three new, explicitly labelled fixtures pin the ordinary user-class
case on unchanged `0ad37b57` and `dced3a75`. TypeScript 7.0.2 accepts all final
sources, and Node v25.5.0 and CLI interpretation agree. Both compiled positive
controls pass IL verification but omit the selected parent constructor, inherited
method/static member, constructor prototype and `instanceof` relationships.
The first catches `undefined is not a function`; its zero exit status is not a
pass. The abrupt awaited parent instead fails compilation with a null `key`
exception, while the reference preserves the rejection before static initialization.

The finite decision in `docs/plans/runtime-valued-superclass.md` retains the
evaluated parent in #1964's guest class definition and uses receiver-preserving
construction/member adapters for runtime heritage. It specifies ordinary
user-class fields, constructors, methods, statics, prototypes and `instanceof`,
plus awaited/abrupt controls, without expanding into arbitrary host constructors
or a general mixin system. The successor depends on #1964; no repair is claimed.

All 13 retained known-parent/awaited-heritage/owner-identity tests passed with
compiled IL verification. These have statically identifiable parents and do not
prove runtime selection. Final fixture reference commands and `git diff --check`
passed. No production code changed. The new probes were not exercised through a
hosted factory or separately through in-process compilation.

## #1908 — Namespace duplicate property-dispatch declarations

**Outcome: demonstrated current diagnostic family; finite implementation transfer
to [#1968](https://github.com/nickna/SharpTS/issues/1968), not a repair.**
The historical note, #1853/#1854 review evidence and #1859 do not expose the
original namespace source/deadline. #1859's retained expression cases are not
treated as proof that namespace declarations were repaired.

Four labelled entry-point controls are recorded in
`docs/plans/namespace-class-identity.md`. Generic and non-generic sibling
namespaces plus a top-level same-named class interpret correctly but fail
compilation with the duplicate property-dispatch diagnostic on unchanged
`0ad37b57` and `a90e3dc7`. Both module import orders pass IL verification but
throw on a null exported namespace class; interpretation rejects exported
namespace declaration naming. TypeScript accepts all four; Node prints `1 2 9`
for sibling controls and `1 2` for module controls. No compiled runtime pass is
claimed. The module failure is related to #1776, not a proven exact duplicate.

The finite successor covers declaration-owned namespace class identity and
lookup/export paths, coordinates with #1776 and preserves #1781's separate
construction control. Existing metadata ownership work is not reopened.
The retained namespace/expression/runtime suite has 124 passes and the same six
object/System.Type verifier failures on both baselines (130 total). All six
failures remain explicitly recorded. No production code changed; reference
commands and `git diff --check` passed. New controls were not exercised through
a hosted factory or separately via in-process compilation.

## #1909 — CLI Array inheritance versus compiler API

**Outcome: default-library CLI declaration gap repaired.**
Recovered the typed storage program retained in #1817's
`PackedSparseSubclassAndRestStorageVerifyAndRunStandalone` test. Its original
60-second compilation and 30-second execution limits remain. The separate
historical non-generic source was not recovered; a labelled new control
reproduces its recorded diagnostic on unchanged `0ad37b57`.

The default-library CLI rejects the typed source at `0ad37b57` and `313373c5`,
while `--noLib` and the single-file API accept it. Loaded declarations represent
Array's value through a constructible interface, so class heritage previously
rejected it. The checker now identifies the actual default-library Array binding
before user declarations, then uses the existing runtime Array subclass bridge.
Local shadowed bindings keep their own checking, and excess type arguments remain
rejected. Declaration libraries are retained, not disabled.

Verification: 183 affected Array storage/subclass/iteration/CLI tests passed with
compiled IL verification, including four isolated standalone cases covering
generic/non-generic declarations with default libraries and `--noLib`. The retained
typed source and new non-generic control match separate TypeScript 7.0.2/Node
v25.5.0 references in CLI interpretation and IL-verified standalone execution,
with zero exit status. Release project builds, code-quality gates and the actual
AOT/trim/single-file analyzer baseline passed (zero analyzer warnings). The
existing NU1902 package warning remains. Hosted execution was not exercised.

## #1910 — Duplicate `$Module_promises` declarations

**Outcome: demonstrated declaration defect repaired; historical imports remain missing.**
The #1821/#1822 evidence retains the duplicate metadata name but no exact
colliding imports or deadline. Labelled new fs/DNS/timers Promise-module controls
compile, pass IL verification and execute successfully at unchanged `0ad37b57`
and `acae1bdd`, while their saved assemblies contain six `$Module_promises`
TypeDef rows. This is a reproduced declaration defect; no runtime failure is
invented for these controls.

ESM and CommonJS module declaration paths now allocate unique CLR type names
while lookup/export fields stay keyed by canonical module path. The current
saved metadata has one base name and five distinct suffixed names. The change
does not rewrite general class/function/enum name resolution.

All 328 affected module/CommonJS/Promise/registry tests passed with compiled IL
verification. Isolated tests assert unique saved TypeDef names, execute both
built-in import orders and cover local same-filename/suffix-looking names in
both orders and both module formats. Local programs remain runtime-independent
under `--standalone`; built-in programs use default CLI deployment for DNS's
existing optional runtime requirement. Node matches their documented outputs.
Release builds, quality gates and the actual AOT analyzer baseline passed with
zero analyzer warnings. No hosted execution is claimed. The fixture README
preserves the source/deadline gap and separates metadata correctness from runtime
success.

## #1713 — Callable Array.from mappers

**Outcome: original compiled defect repaired.** The issue's unchanged source
passes Node and TypeScript and interprets correctly at unchanged `0ad37b57` and
pre-repair `d41c975e`; both compiled baselines pass IL verification but throw on
the bound mapper. Validation now recognizes the emitted callable carriers and
retains receiver-aware invocation. The indirect Number value now uses explicit
numeric coercion through the existing numeric runtime owner instead of returning
null. Original output remains `2,4`, `2,4`, `1,2`.

All 211 affected mapper/function-wrapper/invocation/numeric tests passed with
compiled IL verification, including two isolated CLI outputs with no SharpTS
assembly reference. Controls cover bound arguments and receiver precedence,
bound array methods, extracted call wrappers, omitted/undefined mappers, invalid
explicit mappers on empty inputs, BigInt and Symbol coercion. Release builds,
quality gates and actual AOT/trim/single-file analyzer checks passed with zero
analyzer warnings; the existing NU1902 package warning remains.

A separately labelled class-mapper control exposes an unchanged interpreter
call-versus-construction defect: Node/compiled print `0` then `true`, while both
interpreter baselines print `0` then `accepted`. Its source is retained in
`tests/fixtures/ArrayFromCallableMappers`, with compiled-only coverage and an
explicit limitation. Automatic approval review rejected publishing this adjacent
issue as outside the frozen epic authorization; it remains a local finding.
No proxy-mapper or hosted execution coverage is claimed.

## #1714 — Extracted call/apply wrapper composition

**Outcome: repaired.** The unchanged issue source prints `6 15` in Node;
unchanged `0ad37b57` and pre-repair `ee154e04` saved outputs instead print
`null null` after successful compilation and IL verification. The wrapper
fallback now invokes the canonical receiver-aware helper, reserved before
wrapper emission. Function.prototype wrappers select their actual Reference
receiver, and generic bound callables retain the explicit receiver and prepended
arguments. Bind validates call/apply wrapper types after their declaration.

The exact original plus new target/bind/error/strict-this controls pass both
API engines and three isolated CLI outputs, with IL verification, zero exits,
empty stderr and no SharpTS assembly reference. Separate TypeScript and Node
references accept all sources and preserve the expected numeric outputs and
exception identity. The wider affected suite records **406 passes and one
failure (407 total)**: `AsyncArrow_NestedWithMutation(Compiled)` fails IL
verification with a readonly state-machine address at offset 90. The same
signature is reproduced on unchanged `0ad37b57`; no passing full affected suite
or async-closure repair is claimed. Quality gates and actual AOT/trim/single-file
analyzer checks pass with zero analyzer warnings. Existing NU1902 remains.
Fixtures and scope are retained in `tests/fixtures/ExtractedFunctionWrappers`;
Proxy and hosted execution are not claimed by this task.

## #1715 — Callable Proxy call/apply/bind programs

**Outcome: current original-source behavior repaired; historical timeout not
reproduced.** Both exact issue programs compile and pass IL verification at
unchanged `0ad37b57` and pre-repair `a9baf9a0`, then terminate with bind-validation
TypeError rather than timing out. A labelled operation-isolation control proves
call and apply already succeed and bind is the earliest failing operation.
The interpreter independently loses the bound receiver (NaN or a null-receiver
property error on the original programs).

Compiled bind now validates a Proxy through its soft-runtime IsCallable contract.
Both interpreter bound-call paths retain the Proxy's receiver and arguments.
The original outputs remain `12 30 48` and `3 9 15`; both modes and isolated
saved outputs match Node. Three isolated tests preserve runtime-bearing
deployment, byte-check the copied compiler runtime, verify IL and require zero
exit, empty stderr and completion within the original 30-second execution
limit. Controls include a direct callable, nested Proxies, bound arguments,
receiver preservation, thrown-object identity and non-callable Proxy rejection.

The broader suite has **128 passes and one existing union getter IL failure
(129 total)**, reproduced unchanged on main. A final 36-case focused run also
passes, including boxed/RuntimeValue bound-call entry points. Separate
TypeScript/Node references accept the sources, Release builds and quality gates
pass, and the actual AOT analyzer baseline has zero analyzer warnings.
`tests/fixtures/CallableProxyWrappers` preserves the historical-versus-current
failure distinction. No historical timeout root cause, runtime-independent
standalone Proxy deployment or hosted execution is claimed.

## #1717 — Repeated bound-function names

**Outcome: repaired in both engines.** The unchanged original source exposes
an empty second compiled name on unchanged `0ad37b57` and pre-repair `f020187`;
the interpreter instead drops a bound prefix. The generic emitted wrapper now
resolves the immediate target name and adds its own prefix, while interpreter
rebinding retains the full immediate BoundFunction.Name. Original expected
output remains `bound sample 2 bound bound sample 1` then `7 true true 6`.

All 101 affected naming/function-owner/wrapper tests pass with compiled IL
verification, including two isolated CLI outputs without a SharpTS reference.
A new four-level binding control verifies every prefix, length's zero floor,
argument prepending, independent expandos and unchanged invocation. Both API
modes and default-library CLI sources match separate TypeScript/Node references.
Release build, quality gates and actual AOT/trim/single-file analyzer baseline
pass with zero analyzer warnings. The existing NU1902 warning remains. Source
and command boundaries are preserved in `tests/fixtures/NestedBoundFunctionNames`;
no hosted execution or unrelated target-name mutation repair is claimed.
