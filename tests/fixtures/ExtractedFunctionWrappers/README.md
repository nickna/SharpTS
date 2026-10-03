# Extracted function method wrappers (#1714)

`original.ts` is the unchanged issue source. Node v25.5.0 prints `6 15`;
unchanged `0ad37b57` and pre-repair `ee154e04` saved outputs print `null null`
with exit zero after successful compilation and IL verification. This is a
runtime result defect, not a verifier failure.

`combinations.ts` is a new control with these Node outputs:

```text
9 18
15 51
12 13
27
51
24
```

It covers apply around extracted call/apply, another selected target, bound
call/apply wrappers, an extracted bind method, argument prepending and the
receiver precedence of repeated binding. `errors-and-this.ts` prints three
`true` lines then `true true`, preserving object exception identity and strict
null/undefined receivers through nested wrapper calls.

The repair reserves the canonical receiver-aware invocation signature before
wrapper body emission and uses it when the existing dispatch chain reaches a
nested wrapper or built-in value. Non-function binding retains its explicit
receiver, and Function.prototype wrapper calls select the actual Reference
receiver. Call/apply types are emitted before bind so bind can validate them
without unchecked metadata access. Runtime ownership completion remains intact.

All three sources run through both compiler API modes and isolated CLI output
with default libraries and `--verify --standalone`; the isolated artifacts have
no SharpTS assembly reference. New compilation/execution limits are 60/30
seconds; the original issue specified its command but no deadline. No hosted
execution or Proxy repair is claimed.
