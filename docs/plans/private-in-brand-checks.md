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
