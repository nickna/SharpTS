# Namespace class identity controls (#1908)

These new investigation fixtures are not recovered historical sources. See
`docs/plans/namespace-class-identity.md` for the bounded implementation decision.
Implementation is transferred to [#1968](https://github.com/nickna/SharpTS/issues/1968).

TypeScript 7.0.2 accepts all four entry points with ES2022/CommonJS output.
Node v25.5.0 prints `1 2 9` for `siblings.ts` and `siblings-nongeneric.ts`, and
`1 2` for both module import orders.

Baseline `0ad37b57` and `a90e3dc7` reject both sibling sources at compilation with
the duplicate property-dispatch declaration diagnostic. The module sources pass
IL verification but throw at execution with a null namespace value. Interpretation
passes the sibling sources but rejects the exported namespace declaration.
No compiled runtime pass or exact historical-source recovery is claimed.

Following #1776 and #1781, both sibling controls still reject duplicate class
dispatch. Both module controls now verify IL and exit cleanly with empty stderr
within 30 seconds, but print `2 2` in left-first order and `1 1` in right-first
order, compared with the unchanged reference `1 2`. The remaining declaration/
namespace identity repair stays in #1968; these results are diagnostic failures.
