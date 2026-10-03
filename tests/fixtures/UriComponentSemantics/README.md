# URI component semantics (#1767)

The four underscore-named files preserve the issue's original displayed sources.
Fresh unchanged main and pre-repair current reproduce all four output mismatches
with verified IL, empty stderr and clean execution. Current retains the original
Node expectations and 30-second deadline.

`UriComponentSemanticsTests` supplies twelve TypeScript/Node references covering
malformed percent escapes, invalid and overlong UTF-8, encoded surrogates, lone
UTF-16 surrogates, valid boundary code points, literal surrogate code units,
coercion ordering, borrowed calls and globalThis calls. Existing URI tests retain
the seven passing programs and native metadata/deployment controls. The historical
report's separate direct-call typing limitations remain outside this runtime
repair; any-typed function-value coercion controls pass.

The emitted helpers keep their two owned declarations and precise conversion
inputs. Invalid encoding uses the existing host-exception bridge to expose a guest
URIError. Standalone checks require exact stdout, empty stderr, clean exit and no
SharpTS reference/copy. Hosted variants verify without invoking exports.

The behavior follows [ECMAScript's URI algorithms](https://tc39.es/ecma262/multipage/global-object.html#sec-encode).
See [the outcome record](../../../docs/epic-1866-outcomes.md).
