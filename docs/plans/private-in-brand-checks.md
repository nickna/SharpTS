# Private-in brand checks: bounded implementation design (#1903)

## Evidence and disposition

The [original note](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5851436486)
and [#1853](https://github.com/nickna/SharpTS/pull/1853) report an unsupported
private-in probe without its source bytes, expected output or original deadline.
The frozen comments, phase body/comments, retained fixtures and accessible local
artifacts do not recover it. The sources in `tests/fixtures/PrivateIn/` are new
investigation controls; they are not attributed to that historical probe.

On unchanged baseline `0ad37b574e3c41d267300ada003d63d0f8026562` and
current `b2d4a987`, all five fixtures stop with `Expect expression` at the first
private identifier in a relational expression, exit 1, in CLI interpretation
and compilation. Neither reaches runtime or IL verification. In-process and
hosted execution were not credited with separate runs; the common parser blocks
both pipelines. An issue search found #1903 as the existing exact report and
no later private-in implementation.

**Disposition: finite implementation transfer to
[#1962](https://github.com/nickna/SharpTS/issues/1962).** Parsing alone cannot repair
this behavior: there is no AST node, checker rule or execution/emission path for
private-in. Existing private reads throw on an unbranded object, whereas private-in
must return false for an object without the requested private name. The retained
fixtures and the finite work below define the successor's acceptance boundary.

## Reference controls

Node v25.5.0 and TypeScript 7.0.2 (`--target ES2022 --noEmit --skipLibCheck`)
were used on Windows ARM64. Each positive reference run finished successfully.

| Fixture | Node stdout | TypeScript |
| --- | --- | --- |
| `instance-brand.ts` | `true true` twice, then `false false` three times | Accepts |
| `static-brand.ts` | `true true` twice, then `false false` three times | Accepts |
| `evaluation.ts` | `true 1`, then `7` | Accepts |
| `outside-name.ts` | SyntaxError, exit 1 | TS18016 |
| `undeclared-name.ts` | SyntaxError, exit 1 | TS2339 |

The [ECMAScript relational-expression rule](https://tc39.es/ecma262/2023/multipage/ecmascript-language-expressions.html#sec-relational-operators-runtime-semantics-evaluation)
evaluates the right operand, requires an object and resolves the private name in
the current private environment. Existence uses that private name's identity.
The instance fixture checks a derived instance, an unrelated same-spelled owner,
ordinary public properties and a proxy around the branded object. The static
fixture distinguishes the declaring generic constructor/alias from a derived
constructor, instance and unrelated function. The evaluation fixture checks one
operand evaluation and TypeError for all seven primitive categories.

## Finite implementation

1. Add `Expr.PrivateIn(Token Name, Expr Object)` and the catalog entry. Recognize
   `PrivateIdentifier in ShiftExpression` in relational parsing, respecting
   existing `in` restrictions and precedence. A bare private identifier remains
   invalid; do not lower this to public property access or an invented guest call.
2. Resolve the name lexically in the checker against instance/static private
   fields and methods. Return boolean, validate the right operand's type under
   TypeScript's rules, and reject an undeclared or outside private name. Keep
   ordinary `in` checking unchanged.
3. Update every affected AST visitor, resolver, feature detector, transformation,
   arrow lifter, expression dispatcher and suspension analysis. Use catalog
   coverage tests to catch omitted nodes. Preserve right-operand traversal and
   checked type information during cloning.
4. Interpret with the lexical owner retained by #1902. Validate guest object
   values before querying private storage. A missing brand is false; primitive
   input throws guest TypeError. Static private names test the exact declaring
   constructor, including aliases, rather than superclass membership.
5. Emit ordinary, async, generator and async-generator paths with the same
   semantics. Spill the right operand once. Use the checked private registry and
   generic instance bridge; query storage without the throwing access helper.
   Do not infer a private field's presence solely from CLR type assignability.
   Static generic constructor identity must follow #1904's checked owner rules.
6. Private fields and methods have different initialization timing. Add a
   focused field-initializer control: not-yet-installed fields are absent even
   when that owner's method brand is installed. If current storage cannot
   represent this, change its initialization boundary within this feature rather
   than treating all declared fields as already present.

## Acceptance and stopping rule

Promote the positive fixtures to shared tests with a 30-second execution limit
per case. Add parser/checker negative controls for outside, undeclared and bare
private names, precedence and forbidden `in` contexts. Add lexical module-owner,
closure/suspension and initialization-order controls. Positive cases must match
Node in both engines and verified CLI standalone output; negative objects return
false and primitive operands throw guest TypeError, without host exceptions.

Run affected private-member, parser/checker and AST catalog suites, targeted
pinned Test262 private-in tests against an unchanged baseline, Release build,
code-quality gates and actual AOT analyzer baseline. Report compilation, runtime,
IL verification and any hosted checks separately. A parser rejection is evidence
of the gap, not a runtime pass.

Stop after the specified grammar, declared-name lookup and brand cases pass.
Private-method values remain #1961; repeated class-evaluation identity and
computed-key behavior remain #1906. General parser compatibility, broader class
features and unrelated conformance changes do not enter this successor.

## Implementation verification (#1962)

The implementation adds the relational AST node, lexical checked-owner lookup,
right-operand traversal and presence queries in both engines. Nested class
expressions retain their enclosing evaluation owner; derived instances use the
declaring template's brand, and generic static probes use the exact guest owner.
Instance fields install in source order after their initializer completes, while
the method brand is available before those initializers run.

Validation on Windows ARM64, 2026-10-06:

| Check | Recorded result |
| --- | --- |
| Release build of `SharpTS.Tests` | Succeeded, zero errors; existing NU1902 package advisory |
| `PrivateInTests`, `PrivateInParserTests`, `AstDispatchTests` | 95 passed, zero failed |
| Private/generic/class-expression/arrow/repeated-class selection | 2,043 passed, zero failed, after the #1965 capture follow-up |
| Runtime reference fixtures | All 14 matched Node v25.5.0 stdout, exit 0 and empty stderr |
| Code-quality gate | Passed: 27 duplicate groups, zero errors |
| Actual AOT/trim/single-file analyzer restore and rebuild | Passed: unchanged zero-warning baseline |
| Pinned TypeScript checker smoke gate | Passed: all 32 corpus cases, clean TypeScript 6.0.3 checkout |

The shared runtime tests execute both engines, serialize and verify emitted IL,
and execute the resulting assembly. Thirteen fixtures also execute without a
deployed `SharpTS.dll`; the proxy fixture uses the deployed runtime. Module-owner
controls run in both engines and verify the compiled multi-file assembly. The
default execution deadline is 30 seconds. Runtime reference sources were emitted
with TypeScript 7.0.2 (`--target ES2022 --skipLibCheck --noImplicitAny false`);
the final flag permits the timing fixtures' unannotated private fields without
changing their runtime source. Node's native type stripper was not used as the
reference parser for the generator fixtures.

Two added static-write controls reject assignment before a private field's
initializer has completed, leave its brand absent, and evaluate the RHS once.
They also cover an abrupt RHS and successful assignment after installation for
a generic named owner and repeated class expressions. The preserved pre-fix
compiler (`28DF7ED461F26E6AEE0B34B29FA287925CC2C4D1FC9DB30298A13D5768E621E3`)
failed these semantic controls in both engines even though its emitted IL
verified. The candidate matches Node; the two reference emissions additionally
set `--useUnknownInCatchVariables false` for the caught-error message assertion.

The pinned Test262 comparison selected all 19 `class-fields-private-in` files at
revision `d5e73fc8d2c663554fb72e2380a8c2bc1a318a33`. In each engine the unchanged,
preserved `c782c870` binary reported 11 parse errors and eight deferred negative
cases; the candidate reported eight passes, three parse errors and the same
eight deferred negatives. All worker processes exited 0 with empty stderr and
no runtime failures or timeouts. Two remaining errors require private accessor
declaration parsing; the third uses `function await()` as an ordinary identifier.
The deferred negative cases are not credited as validated semantics; local
parser/checker negative tests provide the separate negative coverage.

Evidence is retained under `artifacts/issue1962-lexical-*`,
`artifacts/issue1962-static-write/` (focused TRX, reference and pre-fix outputs),
`artifacts/issue1965-shadow-capture/broader-tests.log`,
`artifacts/validation/code-quality-final.log` and `artifacts/validation/issue1962/`.
The latter includes exact commands, binary hashes and every Test262 classification.
The final candidate rerun preserves the same eight passes, three parse errors and
eight deferred negatives in each engine. A separate 28-process CLI matrix checks
the three retained positives against Node, interpretation, verified standalone
emission, and hosted initialization with default declarations and `--noLib`.
Hosted execution uses the public factory and `InitializeAsync`, with a 30-second
process deadline; it is separate from ordinary entry-point execution. These
checks do not claim native AOT publication. The consolidated PR records the
full-suite results separately. The checker gate uses the clean pinned TypeScript
6.0.3 corpus and its committed diagnostics without live `tsc` or npm.
