# Borrowed string protocol receivers (#1796)

`borrowed_receiver.ts` preserves the displayed original source. Node expects
`number:77`; fresh unchanged main and pre-repair current verify but print
`string:77`. Repaired current prints `number:77` with empty stderr and clean
exit within the unchanged 30 seconds.

Twelve TypeScript/Node references cover original object/primitive receiver
identity, getter and conversion ordering, abrupt completions, nullish receiver
validation before hook lookup, call/apply/bind, boxed String direct/dynamic calls,
ordinary fallback and omitted patterns. MatchAll, split and replaceAll controls
retain their original receiver through custom dispatch. These controls do not
claim a repair of every replace/search pattern fallback algorithm.

Match/search helpers now accept an object receiver, validate nullish values,
invoke custom hooks and only then convert the receiver for ordinary fallback.
Undefined-padding metadata retains omitted patterns. Direct String emitters
also preserve boxed identity before protocol dispatch. Native checks explicitly
verify the two corrected object/object signatures and padding metadata across
standalone/hosted reuse; existing owners and declarations remain unchanged.

Saved checks require verified IL, exact stdout, empty stderr, clean exit and
no SharpTS reference/copy. Hosted checks verify without invoking exports.
See [the outcome record](../../../docs/epic-1866-outcomes.md) and
[ES2025 String.match](https://tc39.es/ecma262/2025/multipage/text-processing.html#sec-string.prototype.match).
References were checked with TypeScript 7.0.2 and Node 25.5.0.

The two `independent/typed-*.ts` sources are accepted by TypeScript/Node but
rejected before emission on unchanged main and current: indexing a returned
RegExpMatchArray interface is rejected, and boxed String.matchAll is unavailable
in the CLI library surface. These diagnostics are excluded from passing coverage.
Runtime controls use an any result binding and an any prototype method read;
the original #1796 source and expectation remain unchanged.
