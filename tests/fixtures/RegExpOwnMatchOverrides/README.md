# RegExp own Symbol.match overrides (#1794)

`native_own_null.ts` preserves the displayed original source and its expected
`true` output. Fresh unchanged main and pre-repair current verify but print
`false`; repaired current prints `true`, exits cleanly and has empty stderr
within the original 30 seconds.

Twelve TypeScript/Node references cover null and undefined overrides, callable
and non-callable values, deletion, getter order, receiver binding, abrupt
completion, prototype replacement, fresh fallback matchers and ordinary native
matching. The fallback uses RegExpCreate semantics: undefined becomes an empty
pattern and other values undergo ToString, including native RegExp arguments.
An existing callable hook continues to handle native matching.

Standalone checks require verified IL, exact stdout, empty stderr, clean exit
and no SharpTS reference/copy. Hosted variants verify without invoking exports.
Native RegExp and shared string-dispatch ownership/signature controls remain
unchanged. See [the outcome record](../../../docs/epic-1866-outcomes.md).

`independent/empty-regexp-source.ts` prints `true` in Node but `false` on fresh
unchanged main and current, both with verified IL, clean exit and empty stderr.
The additional retained `undefined-pattern-observes-prototype.ts` expects
`(?:)|abc` in Node; current invokes the hook but reports `|abc`. These source
representation diagnostics are excluded from passing coverage. The original
expectations remain retained; no empty-source repair is claimed.

The [RegExpCreate and RegExpInitialize algorithms](https://tc39.es/ecma262/multipage/text-processing.html#sec-regexpcreate)
describe the fallback construction. Reference outputs were independently
checked using TypeScript 7.0.2 and Node 25.5.0.
