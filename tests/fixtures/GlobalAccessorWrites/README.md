# Global accessor assignment (#1769)

`original.ts` preserves the displayed issue source. Fresh unchanged main and
pre-repair current verify and print `2 2`/`true`; current prints the original
Node expectation `8 8`/`true`, with empty stderr and clean exit within 30 seconds.

Nine references cover direct, aliased and computed assignments, the Node global
alias, setter-only descriptors, receiver identity, throwing setters, re-entry,
dot read-modify-write and strict setter calls. The readonly/getter-only control
checks preserved values and deletion; it does not assert strict rejection behavior.
Existing native and saved global-object controls retain plain writes, readonly
data descriptors, identity, optional selection and deployment.

`independent/compound-string-index.ts` is excluded from passing coverage.
TypeScript/Node accept it with `6 2`, while fresh unchanged main and current
reject it before emission with `Array index must be a number`. The dot control
passes. No separate checker repair is claimed.

See [the outcome record](../../../docs/epic-1866-outcomes.md).
