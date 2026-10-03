# Private-method values: bounded implementation design (#1901)

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
