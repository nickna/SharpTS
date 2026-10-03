# Repeated evaluation controls (#1906)

The three identity/repeated-key probes are new investigation sources. The
`generic-one-definition.ts` program is copied from the retained
`GenericComputedFieldKeys_AreSharedAcrossTypeArguments` regression. See
`docs/plans/repeated-class-evaluation.md` for evidence, representation and transfers.
Implementation is split into [#1964](https://github.com/nickna/SharpTS/issues/1964)
and dependent [#1965](https://github.com/nickna/SharpTS/issues/1965). The separate
inline prototype-call verifier error is [#1966](https://github.com/nickna/SharpTS/issues/1966).

Node v25.5.0 prints `false` for `constructors-only.ts`, `false`/`false` for
`identity.ts`, `false 2`/`5 5 undefined undefined 5` for `computed-keys.ts`, and
`1`/`1 5 5 undefined` for `generic-one-definition.ts`. SharpTS interpretation matches.

Baseline `0ad37b57` and `9a431d62` compiled standalone output prints `true` for
constructors-only and `true 2`/`5 undefined 5 undefined 5` for repeated keys,
despite passing IL verification. The one-definition control still passes.
`identity.ts` instead fails IL verification with BackwardBranch at Offset 709.

TypeScript 7.0.2 accepts the identity sources and rejects both dynamic computed
field sources with TS1166. Do not treat those JavaScript output controls as
TypeScript acceptance cases or an IL pass as correct execution.
