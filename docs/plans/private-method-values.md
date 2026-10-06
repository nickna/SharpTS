# Private-method values: bounded implementation design (#1901)

## Implementation evidence (#1961)

Private method extraction now returns a canonical, unbound callable after checking
the receiver against the lexical declaration's brand. Ordinary declarations cache
their callable in a non-generic adapter; generic instance access uses the existing
private-instance bridge. Class expressions cache private callables and static
private state on each evaluated class definition. Repeated expression evaluations
therefore retain different brands, values, and captured environments.

The adapter executes the source body with an explicit guest receiver. Public
property reads, writes, calls, and returned closures use that receiver. Private
access continues to use the lexical owner's storage, including after suspension.
Name, arity, defaults/rest, strict receiver semantics, and non-constructibility use
the emitted function contracts. `PrivateClassElementRegistry` checks adapter,
cache, constructor, and initializer ownership and requires their emitted bodies
before completion. No runtime type generation or SharpTS deployment dependency
is introduced.

`PrivateMethodValueTests` executes eleven sources in interpretation, in-process
compilation, verified saved IL, and standalone deployment. The sources cover
generic identity, extraction evaluation once, borrowed receivers, public writes
and calls, private brands, static state, all four method kinds, returned closures,
defaults/rest, and a timer suspension. Separate tests cover same-named module
declarations and checker rejection of outside access, assignment, and undeclared
private names. Node v25.5.0 matches all eleven expected outputs; TypeScript 7.0.2
accepts the two retained positive fixtures and rejects the outside-access control.

A targeted Test262 comparison against unchanged `c782c870`, at the pinned corpus
revision `d5e73fc8d2c663554fb72e2380a8c2bc1a318a33`, improves
`private-method-get-and-call.js` from RuntimeError to Pass in both engines, with
no regressions in the eight selected files. Three metadata files retain their
parser errors (method-trailing semicolons), and three compiled inherited/inner
arrow controls retain their runtime errors. These outcomes are not counted as
passing conformance checks or silently removed from the comparison.

Final verification passes 22 focused tests and 779 affected regressions with
strict compiled IL verification enabled. The retained positive CLI fixtures also
pass saved-assembly IL verification and exact-output standalone execution without
SharpTS.dll. The Release solution build and all code-quality gates pass; the
analyzer-aware restore/rebuild reports zero AOT/trim/single-file warnings and
matches the checked baseline. The bounded regression selection excludes the
pre-existing lock-decorator IL failure reproduced at `69f2b081`; the separate
async-arrow/inheritance IL failures recorded for #1965 remain unchanged.

The remaining runtime-valued local declaration work is tracked separately by
#1967; this evidence does not claim that repeatedly executed local declarations
already receive the class-expression evaluation identity.

## Evidence and disposition

The notes linked by #1901 and #1854 report preserved generic and non-generic
reproducers, but include neither source bytes nor artifact locations. The frozen
#1599 comments, #1854 body/review comments, retained repository fixtures and
accessible local artifacts did not recover those original sources. No source
below is attributed to the historical probes.

New investigation fixtures are in `tests/fixtures/PrivateMethodValues/`.
On baseline `0ad37b574e3c41d267300ada003d63d0f8026562` and revision `a99c0c17`,
CLI interpretation and compilation reject both positive fixtures with
`Private field '#read' does not exist on class 'Box'`, exit 1. TypeScript 7.0.2
accepts both with `--target ES2022 --noEmit --skipLibCheck`.
Node v25.5.0 prints:

| Fixture | Reference stdout |
| --- | --- |
| `non-generic.ts` | `true true` / `7` / `11` |
| `generic.ts` | `true true` / `seven` / `11` |

The generic identity comparison deliberately uses `any`: TypeScript rejects a
direct comparison between `() => number` and `() => string` with TS2367.
`outside-access.ts` is rejected by TypeScript (TS18013) and SharpTS's private
access check. These controls establish current callable/identity semantics and
the access restriction; they do not reconstruct the original observation.

**Disposition: finite implementation transfer to
[#1961](https://github.com/nickna/SharpTS/issues/1961), not a behavior repair.** A checker
patch alone would expose paths which presently read a field dictionary rather
than create a callable. Binding the extracted function to the inspected receiver
would violate both the identity and explicit-receiver controls above.

## Implementation contract

1. Resolve private names to the lexical declaring class, with distinct instance
   and static private environments. `CheckGetPrivate` must return private method
   function types while preserving outside-class rejection and assignment
   rejection. Receiver class identity cannot select a different declaration's
   private member merely because its spelling agrees.
2. Interpreted class evaluation must create one unbound callable per private
   method, retained by the evaluated declaring class. Extraction checks the
   receiver's private brand and returns that callable. `.call`/`.apply`/`.bind`
   provide the invocation receiver; extraction must not pre-bind it. Calling a
   method which only reads public fields with a plain object is valid. Access to
   a private field inside that method performs its own lexical brand check.
3. Compiled method values need a body entry point with an explicit guest `this`
   operand. Ordinary CLR instance-method reflection cannot invoke an unrelated
   plain-object receiver. Emit a static callable entry point with the synthetic
   `__this` convention already supported by `$TSFunction`. In this entry point,
   public member access must use guest property dispatch when the receiver is
   arbitrary; optimizations which require the declaring CLR type need a proof or
   fallback. Private accesses continue using declaration-owned brand storage.
4. Generic method values must use a canonical entry point with runtime type
   parameter erasure. Different closed CLR instantiations of the same evaluated
   TypeScript class must return the same guest callable. Use the existing
   generic private instance bridge for brand/storage access; do not publish a
   method token on an unbound generic owner into closures or state machines.
   Direct typed calls can retain their existing implementations while sharing
   the private body semantics with the callable entry point.
5. Cache the wrapper by declaring class evaluation and private method identity,
   rather than receiver or closed CLR type. Preserve `name` (including `#`),
   arity, strict `this`, and non-constructibility through emitted function
   metadata. Add checked declaration/body-completion ownership for the new
   entry points to `PrivateClassElementRegistry`; generated helpers must remain
   standalone and use the existing emitted runtime contracts.
6. Apply the same value path in ordinary methods, closures and async/generator
   emitters. Preserve private access receiver evaluation once, before returning
   the callable; preserve invocation argument evaluation and suspension through
   the existing function call machinery. Async/generator values retain their
   existing return/suspension protocol.

## Finite acceptance matrix

- Execute the two committed positive fixtures in both engines and standalone
  CLI output, verifying IL and exact stdout. Retain the original checker
  rejection in the investigation record.
- Repeated extraction and extraction from different instances are identical;
  distinct declarations' functions differ. Include different generic type
  arguments and module-local same-named classes.
- `.call`, `.apply` and bound invocation honor a second instance and a plain
  object when the body uses public properties. Test wrong-brand extraction and
  private-field access from a wrong invocation receiver as guest TypeError.
- Reject outside-class access, private method assignment and dynamic `new` of
  extracted methods. Cover instance/static, ordinary/async/generator forms,
  defaults/rest arguments, closures and a real suspension.
- Run affected private member/function regressions, relevant Test262 selections,
  TypeScript checking controls, Release, IL, standalone deployment, code-quality
  and the repository's actual AOT analyzer check.

#1902 owns the inherited interpreter dispatch repair; #1904 owns the recorded
generic static-private lookup case; #1906 owns repeated class evaluation. This
implementation must interoperate with those results, without treating their
separate observations as already repaired. Repeated evaluations must eventually
receive separate callable caches under #1906's evaluation identity contract.
