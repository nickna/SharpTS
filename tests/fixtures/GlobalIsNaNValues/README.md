# Global isNaN function values (#1770)

`original.ts` preserves the displayed original source. Fresh unchanged main and
pre-repair current verify and print false for `root.isNaN("x")`; current preserves
the original Node expectation true with empty stderr and clean exit within 30 seconds.

Eight TypeScript/Node references cover direct and function-value calls, borrowed
call/apply/bind values, global-property/index paths, omitted arguments, primitive
and object coercion, error identity, Symbol/BigInt rejection, cached wrapper
identity and builtin metadata. Number.isNaN retains its non-coercing predicate.
The global predicate uses the shared ToNumber helper and pads omitted value-call
arguments with undefined. Native number/global contracts and existing predicate
controls retain their owned signatures and deployment boundaries.

Standalone checks require verified IL, exact stdout, empty stderr, clean exit and
no SharpTS reference/copy. Hosted variants verify without invoking exports.
See [the outcome record](../../../docs/epic-1866-outcomes.md).
