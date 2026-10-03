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

## #1718 — Typed Reflect.construct class target

**Outcome: repaired with unchanged original expectations.** The default-library
CLI rejects the exact typed source before output generation on unchanged
`0ad37b57` and pre-repair `d4b1c62b`. Its separately labelled Point-as-any control
already compiles, verifies and executes correctly on unchanged main.
TypeScript's broad Function object type is now distinct from an ordinary call
signature; constructor values satisfy it, and the distinction survives generic
substitution and compatibility caching. Plain objects, primitives, class
instances and ordinary callable-signature assignments remain rejected.

The exact original also exposes a baseline interpreter false result for the
Function.prototype.call invalid-constructor control. Existing non-constructor
flags now mark call/apply/bind intrinsics, retaining callable use and rejecting
Reflect construction. All original expected TypeError lines remain true.

The affected run has **333 passes and one unchanged namespace-construction IL
failure (334 total)**, reproduced on unchanged main. All four positive sources
pass both API modes, default-library CLI interpretation and isolated standalone
execution, including class instance identity, a generic Function constraint,
a Function-typed target, a construct-signature target and all three intrinsic
negative runtime controls. Isolated artifacts verify IL, execute with zero exit
and empty stderr, and have no SharpTS assembly reference. Separate TypeScript
and Node references accept the sources. Release builds, final quality gates and
actual AOT analyzer baseline pass with zero analyzer warnings; existing NU1902
remains. `tests/fixtures/TypedReflectConstruct` retains the source and baseline
boundaries. No namespace repair or hosted execution is claimed.

## #1721 — Extracted TypedArray fill through call

**Outcome: isolated reported behavior repaired.** Both exact issue sources
are retained. On unchanged `0ad37b57` and pre-repair `f09e98f6`, the isolated
source compiles and verifies, then throws the reported undefined-function
TypeError. Fresh processes exit within the original 30-second deadline; no
historical delayed-exit root cause is claimed.

TypedArray method values now expose function-method lookup. Explicit call/apply
wrappers validate and use the selected TypedArray receiver; interpreter fill
also uses that receiver. New controls preserve argument order, start/end bounds,
return identity, another numeric array kind and invalid-receiver TypeErrors.
All 326 affected tests pass with compiled IL verification, including all numeric
and BigInt direct extracted-method controls and two isolated standalone CLI
outputs with zero exit, empty stderr and no SharpTS reference. Separate
TypeScript/Node references accept the sources. Release builds, final quality
gates and actual AOT analyzer baseline pass with zero analyzer warnings.

The original combined source remains a **failed compiled comparison** under
this task: fill reaches `3 3`, but the decoder prints an empty line instead of
`A`; Promise still prints `9`. The saved output exits zero with empty stderr,
which does not make its stdout correct. This separate decoder gap is #1722.
`tests/fixtures/ExtractedTypedArrayMethods` preserves both originals, all expected
outputs and this boundary. No hosted execution or broad unbound-method behavior
change is claimed.

## #1722 — Extracted TextDecoder.decode through call

**Outcome: repaired with the exact original expectation.** Unchanged `0ad37b57`
saves and IL-verifies the source, then exits zero with one blank line. The
decoder now reads a TypedArray's backing bytes using its view offset and byte
length. Its emitted callable wrapper accepts the same view, and explicit
call/apply on the reflection-backed decoder method validates and uses the
selected decoder. Interpreter decoding accepts TypedArray views and explicit
receiver forwarding as well.

All **267 affected encoding/function-wrapper tests pass** with compiled IL
verification. Coverage includes the original, call/apply, view bounds,
multi-byte element views, missing/empty input, invalid-receiver guest TypeErrors,
isolated standalone outputs without a SharpTS reference, and native saved-wrapper
checks. Separate TypeScript and Node references accept the fixtures. Release
builds, quality gates and the actual AOT analyzer baseline pass with zero
analyzer warnings. The exact combined original from #1721 now also matches
`7`, `true`, `3 3`, `A`, `9` with zero exit and empty stderr.

The new nested subarray forwarding control failed at this task's commit due to
#1727's pooled arguments overwriting the outer receiver. Its source and failed
observation remain retained; #1727 subsequently repairs that unchanged control.
The decoder-specific passing control precomputes views.
`tests/fixtures/ExtractedTextDecoderMethods` records these boundaries. No
historical delay root cause, general encoding-label/streaming repair or hosted
guest execution is claimed.

## #1724 — Bound dynamic construction receiver

**Outcome: repaired in both engines.** The exact independent source compiles
and IL-verifies on unchanged `0ad37b57`, then prints `undefined 2 false` with
normal exit and empty stderr. Compiled bound construction now prepends bound
arguments and recursively delegates to the target construction protocol, which
creates the receiver and uses the target prototype. Interpreter construction
unwraps bound targets in its synchronous, asynchronous and internal Construct
paths, preserving the captured receiver for ordinary calls.

Both unchanged issue sources match Node: `2 90 false`, and `3` then `2`.
All **107 selected affected tests pass** with compiled IL verification, including
repeated binding, argument order, prototype linkage, explicit object returns,
thrown-object identity, restoration after failure and construction after async
suspension. Five isolated saved outputs verify IL, omit a SharpTS reference and
exit zero with empty stderr. Separate TypeScript/Node references accept all five
fixtures. Required quality gates and actual AOT analyzer baseline pass with
zero analyzer warnings. `tests/fixtures/BoundConstructors` retains originals
and command boundaries. No #1725 non-constructor policy repair, #1799 Proxy
construction/deployment repair or hosted guest execution is claimed.

## #1725 — Dynamic construction of non-constructor functions

**Outcome: repaired in both engines.** Unchanged `0ad37b57` IL-verifies the
exact branch-control source, exits zero with empty stderr and incorrectly prints
three `constructed` lines. The shared compiled constructor predicate now reads
existing non-constructible and state-machine attributes, and follows both bound
wrapper targets. It rejects arrows, async functions and sync/async generators
without removing compiled generator prototype properties. Interpreter new and
Reflect construction share the same function-kind rejection policy.

Both unchanged originals now produce their exact Node expectations. All
**659 selected affected tests pass** with compiled IL verification, including
generator execution, bound positive construction, repeated bound negative
controls, Reflect target/newTarget checks, argument evaluation and catchable
guest TypeError identity. Six isolated outputs verify IL, omit SharpTS references
and exit zero with empty stderr. The separate compiled prototype control matches
unchanged main. TypeScript/Node accept all reference sources; quality gates and
the actual AOT analyzer baseline pass with zero analyzer warnings.

The earlier new combined prototype control retains an independent interpreter
failure: generator prototype properties are already absent there. Its source
and unchanged Node expectation remain outside passing conformance counts.
`tests/fixtures/NonConstructibleFunctions` records that boundary. No interpreter
generator-prototype repair or hosted guest execution is claimed.

## #1727 — Nested pooled method-call arguments

**Outcome: repaired with unchanged originals.** Unchanged `0ad37b57` compiles
and IL-verifies the exact source, then prints `64` instead of `154`, with
normal exit and empty stderr. Dynamic method-call emission now evaluates
arguments left to right into locals before acquiring and filling its pooled
array. Receiver and callee evaluation retain their original order. The pool
continues to reuse the same thread-local arrays for arities one through four.

All **129 selected affected tests pass** with compiled IL verification. The
exact source and three exact independent controls match Node. New coverage
checks same/different arities, deeper nesting, independent retained arguments
objects, observable receiver/callee/argument getters, exceptions during argument
evaluation, indirect callbacks and compiled guest numeric coercion. A native
call-site control proves repeated non-nested calls receive the same pooled
array; existing thread-local ownership/spread checks remain green. Nine isolated
outputs verify IL, omit SharpTS references and exit zero with empty stderr.
The exact nested decoder control retained under #1722 now passes as well.

Separate TypeScript/Node references accept all sources. Required quality gates
and the actual AOT analyzer baseline pass with zero analyzer warnings.
`tests/fixtures/NestedMethodArguments` retains the original baseline output and
boundaries. Its compiled coercion control has a separate interpreter `NaN`
discrepancy, reproduced unchanged on main; that expectation is not weakened
or counted as passing interpreter conformance. No interpreter unary-object
coercion repair or hosted guest execution is claimed.

## #1798 — Reflect.apply generic result typing

**Outcome: repaired with the unchanged original.** Unchanged `0ad37b57`
rejects the exact asserted Proxy source before saving an assembly with
`Cannot assert type 'R' to 'number'.` Ambient overload groups now retain each
signature's own generic binder. Function rest inference collects argument
tuples, homomorphic readonly tuple/array projections preserve their container,
and generic call instantiation preserves call/construct signatures. Typed
targets retain their result type; the original any-valued target's valid
assertion compiles and produces `function 6`.

All **1,051 selected affected tests pass** with compiled IL verification,
including generic/overload/tuple/readonly/utility typing, invalid result
assertions, constraint fallback, scope isolation and existing Reflect.construct
target rejection. Six normally deployed saved outputs verify IL, compare
matching runtime bytes and exit zero with empty stderr within the original
30-second execution deadline. Node and TypeScript accept the six positive
fixtures. Required quality gates and actual AOT analyzer baseline pass with
zero analyzer warnings. `tests/fixtures/TypedReflectApply` retains the original
sources, negative typing controls and the earlier TS2365 preparation diagnostic
without inventing its missing exact source. No standalone Proxy independence,
hosted guest execution or #1799 construction repair is claimed.

## #1799 — Saved Proxy constructor programs

**Outcome: repaired with both unchanged originals.** Unchanged `0ad37b57`
IL-verifies the class-Proxy and aliased-Proxy-factory sources, deploys matching
runtime bytes, produces no stdout and reports the original not-a-constructor
errors. Fresh processes exit abnormally before 30 seconds; the issue's original
deadline/kill observations remain separate, with no shutdown cause inferred.
Both original ordinary/dynamic class controls still print `8` on main.

Compiled Proxy and bound forwarding now uses general dynamic construction,
which recognizes class Type tokens. The constructor predicate recognizes the
exact Proxy factory, class Proxy targets retain callable branding, and both
engines validate target constructibility before a construct trap. Interpreter
function forwarding uses its fresh-receiver construction protocol.

All nine new saved outputs verify IL and match Node, exiting zero with empty
stderr within the original 30-second deadline. Proxy outputs compare deployed
runtime bytes; ordinary class controls omit it. Tests cover nested/bound
construction, arguments, default prototypes/instanceof, ordinary/revocable
proxies, invalid/revoked targets, primitive trap results, thrown-object identity
and recovery. Construct traps receive the exact target, arguments and newTarget.
Two forced-standalone declaration checks preserve IL, omission and the CLI
runtime note without being counted as standalone executable Proxy conformance.

The selected run passes **206 of 207 tests** with compiled IL verification;
the sole union-getter failure in `Proxy_HasTrap_TruthyCoercion(Compiled)` is
identical on unchanged main. #1715 saved callable controls and native ownership,
emitter reuse and hosted declaration/deployment checks pass. Node/TypeScript
accept all nine positive fixtures. Quality gates and the actual AOT analyzer
baseline pass with zero analyzer warnings. `tests/fixtures/ProxyConstructors`
retains original observations and boundaries. No general Reflect.construct
alternate-prototype or hosted guest execution repair is claimed.

## #1801 — Number predicate values

**Outcome: verified prior repair.** The unchanged original and call control
already compile and standalone IL-verify on `0ad37b57`, match both Node output
lines and exit zero with empty stderr within 30 seconds. Prior commit
`1ed687cb` refreshes retained interface values after declaration merging. The
original's direct `Number.isNaN` value lookup now sees the ES2015 additions;
an explicit ES5-only member-value control still reports the missing member.
No historical compiler execution or new production repair is claimed.

All **95 selected scoped tests pass** with compiled IL verification. New
coverage retains originals, all four method values, inferred aliases, cached
identity, names/arities, non-coercing predicate results, invalid members/types,
library selection and declaration-merge shadowing. Three isolated standalone
outputs omit runtime references/copies and match Node with clean exits.
Four positive references pass TypeScript and Node; namespace syntax uses Node
transformation. Quality gates pass. Production is unchanged from #1799's
actual zero-warning AOT baseline check.

`tests/fixtures/NumberPredicateValues` preserves separate new-control failures
reproduced unchanged on main: typeof Number choosing its instance interface,
compiled namespace initializer output omission, and saved exported-namespace
function invocation throwing object-is-not-a-function. Their Node expectations
remain intact and they are excluded from passing saved conformance counts.
No typeof-query, namespace execution or hosted guest execution repair is claimed.

## #1802 — Array static descriptor identity

**Outcome: repaired.** Both unchanged originals reproduce `false true` on
fresh unchanged main, with verified standalone IL, normal exit and empty
stderr. Compiled Array descriptors supplied undefined for `isArray`; they now
use the existing cached static lookup. Interpreter Array methods use the
established built-in static table so descriptor and member reads agree without
a descriptor-specific cache or function-branding change.

All **413 selected tests pass** with compiled IL verification, including Array
calls/mappers, Object descriptors/assign, constructor branding and the retained
native static lookup identity checks. Four isolated standalone outputs match
Node with clean exits within the original 30-second deadline and omit SharpTS
references/copies. Node and TypeScript accept all four fixtures. Quality gates
and the actual AOT analyzer baseline pass with zero analyzer warnings. The
native checks also compare descriptor values across emitter reuse,
optional families and hosted declarations. `tests/fixtures/ArrayStaticDescriptorValues`
retains originals and boundaries; no compiled Array.from/fromAsync/of descriptor
or hosted guest execution repair is claimed.

## #1729 — Dynamic generator iterator lookup

**Outcome: repaired.** Dynamic compiled symbol lookup now exposes the existing
generator iterator interface method through the ordinary callable wrapper;
the interpreter exposes its corresponding built-in iterator. The unchanged
original, independent lookup, captured invocation and public-next control now
match Node. Fresh unchanged-main original/captured failures reproduce the
reported TypeErrors but exit before the original deadline; the historical
timeout is retained without an inferred process-shutdown cause.

All **902 selected tests pass** with compiled IL verification, including
generator sent/return/throw/finally behavior, iterator protocol, stable numeric
iteration, native ownership, emitter reuse and hosted declarations. Seven
isolated standalone outputs omit runtime references/copies and require exact
Node stdout, zero exit and empty stderr within 30 seconds. All seven references
pass Node and TypeScript with non-strict settings preserving the unchanged
unannotated originals. Quality gates and the actual AOT analyzer baseline pass
with zero analyzer warnings. `tests/fixtures/DynamicGeneratorIterator`
retains evidence and scope boundaries; no general receiver-rebinding, intrinsic
method metadata/identity, async-generator symbol or hosted execution repair is
claimed.

## #1730 — Unicode string delegation

**Outcome: repaired.** Compiled synchronous string delegation uses the existing
StringIterator, preserving surrogate pairs and lone surrogates. Interpreter
string intrinsics, spread and loop paths share equivalent code-point iteration.
The unchanged original now prints `1,2,a,😀`, and its numeric control prints
`2 2 55357`; fresh unchanged main reproduces the original failures with verified
IL, successful exit and empty stderr.

All **1,020 selected tests pass** with compiled IL verification, covering
generator lifecycle, iterator protocols, strings and retained #1729 saved
programs. Eight isolated standalone outputs match Node, omit runtime
references/copies and exit cleanly within 30 seconds. Windows saved tests use
a UTF-8 child-console launcher because the testhost's default console replaced
the same DLL's correct emoji output with `??`; the source and expectations
remain unchanged. Nine references pass Node and TypeScript. Quality gates and
the actual AOT analyzer baseline pass with zero analyzer warnings.

`tests/fixtures/GeneratorStringDelegation` retains an independent missing-throw-
method TypeError mismatch, reproduced unchanged on main, outside passing counts.
Completion, return/finally and operand exception controls pass. No correction of
that separate throw protocol, compiled async string delegation or hosted guest
execution is claimed.

## #1732 — Async-generator throw injection

**Outcome: repaired.** Compiled `throw()` injects its value at an ordinary
suspended yield, resumes existing catch/finally routing and settles the
ordinary step/promise result. The exact original and caller-control outputs
match Node. Fresh unchanged main reproduces the original's lone `1` and the
caller's `rejected 7` with verified IL, clean exit and empty stderr.

All **824 selected tests pass** with compiled IL verification, including
awaiting catches/cleanup, nested finally blocks, original error identity,
nullish/falsy caught values, repeated throws from a suspended catch,
not-started/completed controls, native runtime ownership and retained iterator
regressions. Nine references run in both engines; the compiled next/return
control and ten isolated standalone outputs also pass. Saved tests omit
runtime references/copies and require exact Node stdout, clean exit and empty
stderr within the original 30-second deadline. All eleven references pass
Node and TypeScript; quality gates and the actual AOT analyzer baseline pass
with zero analyzer warnings.

`tests/fixtures/AsyncGeneratorInjectedThrow` retains unchanged-main evidence
for two separate failures: compiled awaiting of completed `throw(null)` misses
the catch, and interpreted `return()` leaks `GeneratorReturnException`. Neither
is included in passing counts for that engine. No general request queue,
delegated throw-method protocol or hosted execution repair is claimed. Active
delegation retains its existing throw path so a request cannot linger until
an unrelated ordinary yield.

## #1733 — Async delegation completion

**Outcome: repaired.** Compiled async `yield*` preserves the delegate's final
result value; synchronous generators use their existing public next(v)
protocol for sent values and completion. The interpreter shares sync/async
generator driving and preserves explicit null completion. Original async/sync
aggregates and direct completion records now match Node. Fresh unchanged main
reproduces both `NaN` aggregates and both missing completions with verified IL,
clean exit and empty stderr; the original values-only control remains correct.

All **852 selected tests pass** with compiled IL verification, including native
runtime ownership, generator lifecycles, retained #1732 injection and iterator
regressions. Fourteen references run in both engines and as isolated standalone
saved output, covering completion identity, empty and nested pending delegates,
sent values, natural/error cleanup and cleanup before a later ordinary throw.
Saved outputs omit runtime references/copies and require exact Node stdout,
clean exit and empty stderr within 30 seconds. All sixteen references pass
Node and TypeScript. Quality gates and the actual AOT analyzer baseline pass
with zero analyzer warnings.

`tests/fixtures/AsyncGeneratorDelegationCompletion` retains separate unawaited
async-generator return-promise failures, reproduced on unchanged main even
without delegation and excluded from passing counts. No general promise-return
adoption, external delegated return/throw protocol, custom async adapter,
compiled async string iteration or hosted execution repair is claimed.

## #1734 — Shared async mutable captures

**Outcome: repaired.** Standalone async captures retain existing lexical
display-class references, so repeated methods and sibling closures share the
same mutable binding. Homes resolve by AST identity and references by type;
numeric fields keep the boxed async-arrow storage contract. The unchanged
original loop now prints `6` within its original 30-second deadline, and the
bounded original prints `1 false 2 false 3 false 4 true`. Fresh unchanged main
reproduces the loop timeout with empty streams and the bounded reset-to-one
output, with verified IL; no shutdown root cause is inferred.

All **22 focused tests pass**, covering eleven references in both engines and
as isolated standalone saved outputs, including independent factories, sibling
sync/async reads/writes, parameters, multiple lexical homes and pending awaits.
Saved tests omit runtime references/copies and require exact Node stdout,
clean exit and empty stderr within 30 seconds. Node and TypeScript accept all
eleven references. The broader run has **446 passes and one unchanged-main
IL failure**: `AsyncArrow_NestedWithMutation(Compiled)` retains the readonly
outer-state-machine address diagnostic at offset 90 already verified for
#1714. Quality gates and the actual AOT analyzer baseline pass with zero
analyzer warnings.

`tests/fixtures/AsyncMutableCaptures` records scope and evidence. The remaining
boxed-state-machine failure, absent lexical display-class homes, per-iteration
mutable async cells and hosted guest execution are not claimed as repaired.

## #1735 — Awaited for-await elements

**Outcome: repaired.** For-await loop binding applies the existing recursive
Promise unwrapping to element types and their union members. Ordinary for-of
retains Promise elements, and awaiting a tuple does not recursively transform
its nested members. Both unchanged originals now match Node; fresh unchanged
main reproduces the reported numeric diagnostics with no output assemblies,
while both exact any-typed controls already execute correctly.

All **832 selected tests pass** with compiled IL verification, covering async
iteration, iterable typing, generators and retained saved regressions. Eight
runtime references run in both engines and as isolated standalone output;
five uncalled declared-type controls check inference separately. Seven
negative bindings fail in both test-harness modes and produce no CLI assembly.
Saved runtime outputs omit references/copies and require exact Node stdout,
clean exit and empty stderr within 30 seconds. Node accepts the thirteen
positive references; TypeScript accepts them and rejects all seven negatives.
Quality gates and the actual AOT analyzer baseline pass with zero analyzer
warnings.

`tests/fixtures/ForAwaitElementInference` retains an independent primitive
member-validation gap: number.then is accepted unchanged on main even without
iteration, unlike TypeScript. Those two references are excluded from passing
counts. No general primitive-member validation, arbitrary thenable/generic
Awaited expansion, non-iterable async-source diagnosis or hosted execution
repair is claimed.

## #1739 — IteratorClose primitive results

**Outcome: repaired.** The emitted close helper rejects BigInt and Symbol as
well as the existing primitive categories. Its symbol dependency comes from
the compilation's scoped owner. The unchanged three original programs now
match Node; fresh unchanged main accepts BigInt/Symbol and rejects the numeric
control. An incoming throw keeps its identity when closing returns a primitive,
throws, or encounters a throwing getter.

The selected iterable-consumer run has **1,065 passes and one unchanged-main
failure (#1956)**, with compiled IL verification enabled. Eleven compiled
references also run as isolated standalone output with exact Node stdout,
empty stderr, no SharpTS reference/copy and clean exit within 30 seconds.
Native tests validate every primitive category, valid-object behavior,
standalone/hosted declaration ownership and emitter reuse. Node and TypeScript
check all references. Quality gates and the actual AOT baseline pass with zero
analyzer warnings.

`tests/fixtures/IteratorCloseResults` retains the infinite-destructuring probe
outside passing counts: fresh unchanged main and the corrected compiler both
time out before closing, with empty streams. Frozen child #1750 owns that
consumer correction. The interpreter's broader normal-close validation and
hosted guest execution are not claimed as repaired.

## #1742 — Indexed getters in array and argument spread

**Outcome: repaired.** Array collection checks the scoped descriptor store
before using backing storage. Descriptor-bearing arrays use ordinary indexed
reads with live length, so own getters run in order and can grow or shrink the
source. Iterator override selection precedes this path. Descriptor-free numeric
argument spreading retains the bulk path; dense collection materializes holes
as actual undefined entries. Both original failures now match Node, while the
unchanged direct-read and Array.values controls remain correct.

All **487 selected tests pass** with compiled IL verification. Eleven compiled
references also run as isolated standalone outputs with exact Node stdout,
empty stderr, clean exit within 30 seconds and no SharpTS reference/copy. Native
reused-emitter tests cover both List and emitted-array descriptor overlays,
minimal/optional/mutation/hosted declaration ownership and scoped dependencies.
Node and TypeScript check every source. Quality gates and the actual AOT
analyzer baseline pass with zero analyzer warnings.

`tests/fixtures/ArraySpreadDescriptors` records getter side effects, live
length, throw identity, custom iterators, repeated spreads, sparse arrays and
numeric controls. No interpreter, hosted guest execution, unrelated array
method-descriptor or arbitrary iterable-protocol repair is claimed.

## #1745 — Iterator forEach return value

**Outcome: repaired.** Compiled iterator forEach returns the compilation's
undefined singleton after normal exhaustion. The helper receives that scoped
field directly and preserves its callback loop and public ABI. Both unchanged
original result failures now match Node; the values-only control remains correct.

All **87 selected tests pass** with compiled IL verification, including iterator
helpers and native emission reuse. Six compiled references run as isolated saved
standalone output, with exact Node stdout, clean exit, empty stderr, no SharpTS
reference/copy and the original 30-second deadline. Native tests check singleton
identity for empty/nonempty inputs across minimal, optional, repeated and hosted
declarations. Node and TypeScript check all sources. Quality gates and the actual
AOT analyzer baseline pass with zero analyzer warnings.

`tests/fixtures/IteratorForEachReturn` retains two separate contextual-void
callback checker failures. TypeScript accepts value-returning block callbacks;
fresh unchanged main and current reject them before emission. They are excluded
from passing totals. No callback typing, abrupt-close policy or hosted guest
execution repair is claimed.

## #1746 — Iterator take/drop limit validation

**Outcome: repaired.** Both compiled factories receive the boxed limit and
perform abstract numeric coercion once, reject NaN, truncate fractions and
reject negative integer limits. Validation/coercion failure closes the receiver
while preserving the incoming error. Lazy wrappers retain double integer
limits/counters, preserving infinity and large finite limits without Int32
narrowing. Both unchanged originals now match Node. This follows the published
[ES2025 take/drop contract](https://tc39.es/ecma262/2025/multipage/control-abstraction-objects.html#sec-iterator.prototype.take);
the newer draft's finite safe-integer ceiling is not applied to original-version
expectations.

All **111 selected tests pass** with compiled IL verification, including helper
behavior, native scoped emission and retained #1745 coverage. Twelve compiled
references also run as isolated standalone output, with exact Node stdout,
empty stderr, no SharpTS reference/copy and clean exit within 30 seconds. Native
reused-emitter tests check the revised boxed factory/double constructor ABI,
negative/NaN rejection, infinity and output isolation across minimal, optional
and hosted declarations. Node and TypeScript check every source. Quality gates
and the actual AOT analyzer baseline pass with zero analyzer warnings.

`tests/fixtures/IteratorLimits` records numeric and coercion boundaries, invalid
closing, and exception precedence. Frozen child #1747 still owns normal
limit-reached closing. No interpreter, arbitrary invalid-receiver or hosted
guest execution repair is claimed.

## #1747 — Closing iterator take at its limit

**Outcome: repaired.** Compiled take closes its underlying iterator when
resumed after reaching the limit, including zero limits. It marks completion
before advancing or closing, preserves next/return errors and skips closing
after normal exhaustion. Scoped adapters and lazy helpers forward cleanup;
flatMap closes its inner source before its outer source, preserving the first
close error. Closed helpers stay completed. Both unchanged originals match
Node; fresh unchanged main retains the missing close and passes the direct
return control.

The selected iterator, for-of and generator run has **2,032 passes and one
unchanged-main failure (#1956)** with compiled IL verification enabled.
Thirteen compiled references also run as isolated standalone output with exact
Node stdout, empty stderr, no SharpTS reference/copy and clean exit within
30 seconds. Native reused-emitter tests verify forwarding, close counts,
completion and scoped declaration ownership. Node and TypeScript check every
source. Quality gates and the actual AOT analyzer baseline pass with zero
analyzer warnings.

`tests/fixtures/IteratorTakeClose` records lazy resume timing, normal exhaustion,
custom iterators, generator finally blocks, primitive/error return results and
pipeline cleanup. No interpreter, all-helper abrupt-completion or hosted guest
execution repair is claimed.

## #1748 — Iterator.from array adapters

**Outcome: repaired.** The compiled factory selects custom Symbol.iterator once,
adapts default arrays using the live indexed array iterator, and preserves
compatible iterator identity. Custom iterator objects capture their next method
once. Array completion clears the last yielded value. All three unchanged
originals now match Node and finish normally within 30 seconds. The original
fresh-main timeout, identity mismatch and catch-control TypeError remain
recorded independently.

The selected iterator run has **723 passes and one unchanged-main failure
(#1956)** with compiled IL verification. Thirteen compiled references also run
as isolated standalone outputs with exact Node stdout, empty stderr, no SharpTS
reference/copy and clean exit within the original 30-second deadline. Native
reused-emitter tests check adapter/iterator identity, exhaustion, scoped
ownership and the unchanged public ABI. Node and TypeScript check all sources.
Quality gates and the actual AOT analyzer baseline pass with zero analyzer
warnings.

`tests/fixtures/IteratorFromArray` records live mutations, indexed getters,
custom iterator selection, generator/helper identity and captured next.
It also retains a separate any-typed toArray dispatch failure, excluded from
passing counts: unchanged main and current throw the same TypeError with empty
stdout. No full primitive/override compatibility, dynamic helper dispatch,
interpreter or hosted guest execution repair is claimed.

## #1750 — Incremental array binding and cleanup

**Outcome: finite design transfer, not repaired.** The concrete contract is
linked in [incremental-array-binding.md](plans/incremental-array-binding.md)
and will be included in the consolidated PR. No separate successor issue was
published. Exact partial/empty programs still consume four next calls without
closing, in both interpreted and compiled execution. Fresh unchanged main and
current preserve the same wrong results; the original rest and weak generator
controls remain correct. Saved original outputs verify their IL, have empty
stderr and exit cleanly within 30 seconds.

Additional default-order, nested-order and abrupt-default probes establish why
limited materialization or a final close call cannot satisfy binding semantics.
The recovered infinite close-result probe still times out at the unchanged
30-second deadline with empty streams. Every failing execution is retained
outside passing counts. The linked plan specifies cursor/completion state,
binding scope, interleaved defaults, nested close order, error precedence,
existing consumer transforms and finite acceptance/stopping conditions.

All **313 selected tests pass** with compiled IL verification. Four independent
passing controls run in both engines and as isolated saved standalone outputs
with exact stdout, empty stderr, clean exit, no SharpTS reference/copy and the
original 30-second deadline. Node and TypeScript check all ten fixture sources.
Quality gates pass; production code is unchanged, so the verified #1748 actual
AOT baseline remains applicable. No iterator-close or hosted execution repair
is claimed. Automatic approval review rejected publishing a separate issue;
the design remains a reviewable part of the requested consolidated PR.

## #1751 — Guest errors for non-iterable destructuring sources

**Outcome: repaired.** Ordinary guest object/class storage now requires a guest
iterator method instead of inheriting iterable behavior from CLR storage.
Missing/non-callable methods and invalid iterator results throw guest TypeError.
Custom selection runs once with the original receiver and a captured next
method. Valid strings, collections, typed arrays, Buffer and native CLR
iterable/iterator controls retain their paths. The public normalizer ABI is
unchanged and receives scoped protocol, error and optional collection inputs.
All three unchanged originals now match Node; fresh unchanged main retains the
original Error names and accepted empty object.

All **591 selected tests pass** with compiled IL verification, including
retained destructuring, iterable, spread, adapter and native emission coverage.
Fourteen compiled references also run as isolated standalone output, with exact
Node stdout, empty stderr, no SharpTS reference/copy and clean exit within the
original 30-second deadline. Native reused-emitter tests validate guest-source
rejection, Queue/string controls, output isolation, selected optional types and
standalone/hosted declaration ownership. Node and TypeScript check every source.
Quality gates and the actual AOT analyzer baseline pass with zero analyzer
warnings.

`tests/fixtures/ArrayDestructureSourceValidation` records the original programs,
primitive/object shapes, empty patterns, valid collections, getter/factory
counts, receiver/throw identity and malformed iterator results. The chosen null
message matches Node, but general engine message wording is not a contract.
Incremental closing remains the #1750 design, dynamic array overrides #1753,
and fresh LINQ acquisition #1754. No interpreter or hosted guest execution
repair is claimed.

## #1752 — Typed array Symbol.iterator assignments

**Outcome: repaired.** TypeScript accepts both unchanged original programs.
Array/tuple iterator factories now retain their element type and require a
synchronous iterable iterator result. Symbol writes use ordinary property
storage; tuple and unique-symbol key aliases retain iterator mutation detection.
Seven references execute in both engines and as isolated saved standalone
outputs with verified IL, exact Node stdout, empty stderr, clean exit within
the original 30 seconds and no SharpTS reference/copy. Eight negative controls
are rejected by the checker and CLI before emission. Fresh unchanged main
retains the original typed rejection and passing any-alias control.

The selected array, tuple, generator-expression and iterator run has **2,992
passes and one unchanged-main failure (#1956)** with compiled IL verification.
All 31 focused tests pass. Node and TypeScript check the sixteen test sources.
Quality gates and the actual AOT analyzer baseline pass with zero analyzer
warnings.

`tests/fixtures/ArrayIteratorAssignment` separately preserves an accepted
Array.values factory's runtime failure: unchanged main and current saved
outputs both throw an undefined-function TypeError with empty stdout and
exceed the original deadline. Its saved typing/emission control passes, but it
is excluded from passing runtime counts. Both interpreters also reject it.
A direct lifted wrong-element assignment remains accepted by the in-process
checker before declaration refinement, although CLI/TypeScript reject it; a
preceding named generator supplies the concrete negative element control.
No general forward-inference, dynamic-source normalization (#1753) or hosted
guest execution repair is claimed.

## #1753 — Iterator overrides on dynamic array sources

**Outcome: repaired.** Dynamic array destructuring now honors own iterator
overrides through the existing materializer whenever analysis cannot prove
the array iterator unchanged. Descriptor API use also disables the iterator
fast path. The normalizer retains its public ABI and receives a scoped mutation
flag. Native reused-emitter controls retain identity when the iterator is
unchanged and verify collection, scoped ownership and standalone/hosted output
isolation when mutation is possible. Both unchanged originals now match Node;
fresh unchanged main retains `1 2` for the dynamic source and `8 9` for its
typed alias control.

All **1,008 selected tests pass** with compiled IL verification. All 31 final
focused checks pass, including thirteen saved standalone programs with exact
Node stdout, empty stderr, clean exit within the original 30 seconds and no
SharpTS reference/copy. Twelve references also execute in the interpreter.
Node and TypeScript check every fixture source. Quality gates and the actual
AOT analyzer baseline pass with zero analyzer warnings.

`tests/fixtures/DynamicArrayDestructureOverride` preserves getter/factory and
next selection counts, receiver identity, aliases, empty/fresh iterators,
factory throws, invalid methods and unchanged arrays. The compiled iterator
getter passes; unchanged main and current interpreters both reject it, outside
interpreted passing counts. A separate prototype lookup reproduction also
remains outside passing counts: saved any-alias outputs on unchanged main and
current verify their IL and cleanly print `1 2` instead of Node's `8 9`.
No general prototype lookup, incremental closing (#1750), fresh CLR enumerable
acquisition (#1754) or hosted guest execution repair is claimed.

## #1754 — Native CLR enumerable acquisition

**Outcome: finite design transfer, not repaired.** The concrete acquisition
contract is linked in [clr-enumerable-acquisition.md](plans/clr-enumerable-acquisition.md)
for inclusion in the consolidated PR. No separate successor issue was published.
The exact raw LINQ input still produces an empty array instead of `0, 1, 2` in
fresh unchanged main and current, across all six native configurations per
compiler. Every generated output is saved, IL-verified and checked for scoped
ownership and standalone/hosted deployment-reference isolation. Compiler and
output hashes are recorded. These are native emitted-helper reproductions;
no guest-program failure or hosted guest execution is claimed.

Initialized, active and exhausted enumerator controls pass, as does Queue.
Repeated raw enumerable use remains empty. Public-interface role adapters
demonstrate repeated sequence acquisition and cursor consumption with explicit
intent; they do not replace or repair the unchanged original. The retained
probe reports `OriginalMatchesExpected: false` separately from successful
evidence collection and passing controls. A blanket IEnumerable-first dispatch
would restart active/exhausted cursors. The linked plan specifies the native
interop boundary, explicit acquisition role, legacy-policy decision, scoped
emission and finite acceptance/stopping conditions.

All **109 selected tests pass** with compiled IL verification, including native
initialized/active/exhausted controls and retained iterator helper consumption.
Quality gates pass. Production code is unchanged, so the verified #1753 actual
AOT analyzer baseline remains applicable. The unresolved original and repeated
raw-source failures remain outside passing counts.

## #1740 — Prefixed Readable import dispatch

**Outcome: repaired.** Built-in import pre-scanning now uses the canonical bare
key for node:-prefixed imports, registering named bindings before function
bodies are emitted. Shared state-machine dispatch resolves methods on dynamic
receivers instead of assuming names such as push designate an array operation.
Readable.push therefore buffers the chunks and receives its null EOF signal.
All six unchanged executable originals now match Node, including typed/from,
construction/push, bare alias, catch and stage controls. The original's UTF-8/LF
source hash matches the frozen issue. Fresh unchanged main and pre-repair current
retain empty-output/from failures and the staged undefined-function TypeError.

All **3,995 selected tests pass** with compiled IL verification, covering
retained modules, streams, registry dispatch and array operations. All sixteen
focused tests pass. Seven references run in both engines and as isolated saved
standalone outputs with verified IL, exact stdout, empty stderr, no SharpTS
reference/copy and clean exit within the original 30 seconds. Node checks all
eight sources. Quality gates and the actual AOT analyzer baseline pass with
zero analyzer warnings.

`tests/fixtures/PrefixedStreamImports` preserves every original and adds the
bare construction/push control. The top-level-await original still has its
recorded pre-emission diagnostic and remains outside passing execution counts.
No iterator-next, Windows process-lifetime or hosted guest execution repair is
claimed. Historical matching-body evidence describes the earlier migration;
this semantic repair changes emitted method bodies.

## #1771 — Indirect eval optional runtime deployment

**Outcome: repaired.** Emitted value-form eval acquisition now records the
optional interpreter bridge requirement. Synchronous and state-machine bodies,
named global reads and computed/dynamic global keys deploy the matching runtime
in normal mode. Saved outputs retain no hard SharpTS reference. The unchanged
displayed original now prints `3`, with verified IL, empty stderr and clean exit
within its 30-second deadline. Fresh unchanged main and pre-repair current both
retain the missing-runtime error; those fresh processes finish within 30 seconds.
The historical timeout remains a separate #1772 observation.

All **21 focused tests pass**. The broader selected suite passes **307 tests**
and retains the independently established unchanged-main async-arrow mutation
IL failure (readonly address at offset 90); it remains outside passing counts.
Nine references run in both engines and isolated normal outputs. Three isolated
standalone controls preserve non-string identity, caught missing-bridge failure,
runtime omission and direct static eval's caller-local access. Node and
TypeScript independently check twelve sources, including the intentionally
different standalone missing-bridge control. Quality gates and the actual AOT
analyzer baseline pass with zero analyzer warnings.

`tests/fixtures/IndirectEvalDeployment` preserves the issue body's displayed
source and independent controls. Normal deployment is conservative when eval
escapes as a value, even if a particular later input is non-string. Explicit
standalone behavior and non-string identity remain intact. No broader dynamic
direct-eval lexical-scope, hosted execution or Windows lifetime repair is claimed.

## #1774 — Callable namespace function values

**Outcome: callability repaired here; later original mutation failures repaired
by #1777.**
Script initialization retains a source path for diagnostics but now resolves
function declarations in shared script scope. Namespace population consequently
stores callable function wrappers. Ordinary, synchronous-generator and async
declarations work through direct, aliased, nested and merged access; the original
shape control prints `function function true` with preserved identity.

All **17 focused cases pass**: six compiled originals, five interpreted
references and six isolated saved standalone originals. The interpreter's
independent merged-namespace binding failure is excluded. Every saved original
retains verified IL and its 30-second deadline. Fresh main and pre-repair current
reproduce seven callability errors and the shape mismatch. After repair, the two
mutation originals reach their function bodies but initially print stale/ignored
values. #1777 subsequently repairs them and enables their complete unchanged
expectations in compiled and isolated regression coverage. Their exact sources
and full reference expectations are preserved in
`tests/fixtures/NamespaceFunctionValues`.

The selected suite passes **309 tests** and retains seven established
unchanged-main namespace-class object/System.Type IL failures, tracked in
#1781. Full TypeScript compilation and Node confirm all eight original
expectations. Quality gates and the actual AOT analyzer baseline pass with zero
analyzer warnings.
No module namespace, property mutation, class construction or process-lifetime
repair is claimed.

## #1775 — Private functions in namespace initializers

**Outcome: repaired by #1774's script-scope resolution, with export visibility
completed here.** Fresh unchanged main reproduces all three exact original
undefined-function errors. Current prints the expected ordinary, generator
and async results. Private function declarations remain available to lexical
namespace lookup and are no longer published as object properties; exported
functions retain callability and identity.

Seven compiled references and seven isolated saved standalone outputs verify
the originals, nesting/shadowing, merging, function visibility and declaration
identity. Together with retained #1774 coverage, all **31 focused cases pass**.
All saved outputs verify IL, preserve exact stdout and empty stderr, omit hard
SharpTS references and copies, and exit cleanly within 30 seconds. Full
TypeScript compilation and Node confirm all seven expectations. The broader
selected suite passes **1,928 tests**, retaining the seven established namespace
class IL failures owned by #1781. Quality gates and the actual AOT analyzer
baseline pass with zero analyzer warnings.

`tests/fixtures/NamespaceInitializerFunctions` retains the unchanged originals
and the separate missing-member typeof reproduction for #1778, excluded from
passing counts. Fresh original diagnostics finish within 30 seconds; historical
Windows lifetime observations remain with #1772. No broader private-variable
publication, cross-declaration private scope, mutation or hosted execution
repair is claimed.

## #1776 — Imported namespace member values

**Outcome: repaired.** Namespace declarations now participate in module export
name discovery and publish their populated object into the import/export slot.
Named namespace exports store the same object. Merged declarations share one
export field. The unchanged two-file original prints `8 9 true`; the constant
control retains `8 true`. Fresh unchanged main and pre-repair current reproduce
the null-receiver error, with verified IL and empty stdout. Fresh diagnostics
finish within 30 seconds, leaving the historical lifetime observation to #1772.

All **24 focused cases pass**, including nine compiled references, nine isolated
saved standalone outputs, and six retained namespace declaration/deployment
checks. Full TypeScript compilation and Node confirm every pair. Saved guest
outputs have verified IL, exact stdout, empty stderr, clean exit within the
original deadline and no SharpTS reference/copy. The broader selected suite
passes **1,948 tests** and retains the seven established namespace class IL
failures owned by #1781. Quality gates and the actual AOT analyzer baseline pass
with zero analyzer warnings.

`tests/fixtures/ImportedNamespaceFunctions` preserves original and direct,
named, nested, generator, async, merged and renamed controls. Existing hosted
coverage checks declarations/deployment, without claiming guest execution.
Cross-module same-name namespace identity remains in the #1908 design transfer;
no mutation, class identity or process-lifetime repair is claimed here.

## #1777 — Namespace property writes and live exported bindings

**Outcome: repaired.** Emitted dot and computed writes dispatch through the
scoped namespace storage owner. Exported variables bind to their generated
public static backing fields, sharing values with member-body reads and writes.
The unchanged original now prints `8 9`, compared with `null 3` on fresh main
and pre-repair current. Added properties, existing values, function replacement,
strict writes, nested namespaces and separate-object controls match Node.
The two exact #1774 mutation originals now pass their complete expectations;
all **22 #1774 regression cases pass**, including all eight compiled and saved
originals and six interpreted references.

All **49 focused cases pass** with IL verification, including eight compiled
references, eight isolated saved standalone outputs, scoped namespace metadata
and native object-writing controls. Native namespace helpers preserve Get/Set
identity and instance isolation, verify Bind metadata/ABI and observe live
field updates in both directions. Reused native emitters cover minimal,
optional and minimal-again configurations for standalone and hosted outputs,
without hard SharpTS references or stale declaration ownership. Full
TypeScript compilation and Node confirm every source. Saved guest outputs
verify IL, exact stdout, empty stderr, no SharpTS reference/copy and clean exit
within the unchanged 30 seconds.

The selected suite passes **1,990 tests** and retains the seven established
namespace class IL failures owned by #1781. Quality gates and the actual AOT
analyzer baseline pass with zero analyzer warnings.
`tests/fixtures/NamespaceMutation` preserves originals and controls. No broader
private-variable publication, cross-module namespace identity, missing-value,
deletion or hosted guest execution repair is claimed. Missing values and actual
deletion remain the separate #1778/#1779 tasks.
