# Primitive string symbol hooks (#1795)

`primitive_prototype.ts` preserves the displayed original source and Node's
expected `7`. Fresh unchanged main and pre-repair current verify but print `-1`;
repaired current prints `7`, with empty stderr and clean exit within 30 seconds.

Twelve TypeScript/Node references cover Number, Boolean, String, BigInt and
Symbol prototype hooks, strict accessor/method receiver identity, inherited
Object.prototype hooks, thrown identity, non-callable and nullish hooks,
deletion, nullish candidates, boxed/custom controls and borrowed value calls.
Shared match/search/replace/replaceAll/split dispatch observes non-nullish
primitive candidates. MatchAll observes their method hooks while IsRegExp
continues to ignore primitive @@match properties.

Symbol indexed reads begin at the primitive's intrinsic prototype and retain
the original receiver for getters. The shared dispatch's two misleading
object-only comments are corrected. Existing string-dispatch owned signatures
and seven scoped metadata inputs remain unchanged; indexed lookup receives
the precise existing primitive families it needs, with BigInt emission governed
by its supplied implementation.

Standalone checks require IL verification, exact stdout, empty stderr, clean
exit and no SharpTS reference/copy. Hosted checks verify without invoking exports.
See [the outcome record](../../../docs/epic-1866-outcomes.md) and the
[ES2025 String search protocol](https://tc39.es/ecma262/2025/multipage/text-processing.html#sec-string.prototype.search).
References were checked with TypeScript 7.0.2 and Node 25.5.0.
