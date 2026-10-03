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
