# Inferred-type pilot measurement

Measured against the fixed TypeScript v6.0.3 revision
`050880ce59e30b356b686bd3144efe24f875ebc8`, using the twelve explicit files in
`config/types-subset.json`. All twelve pass the existing diagnostic comparison.
All twelve have the type outcome `Mismatch`; the pilot measures existing gaps
and does not claim inferred-type parity.

The upstream references contain **584 observations**: **164 exact matches**,
**72 type-text differences**, **161 missing source observations**, and
**187 unsupported displays**. There are **zero extra observations**, harness
errors or missing baselines. SharpTS produces 423 corresponding source facts;
unsupported facts are included in that count and never count as matches.

| Pinned case | Match | Text difference | Missing | Unsupported |
|---|---:|---:|---:|---:|
| `es2017/es2017DateAPIs.ts` | 3 | 0 | 2 | 0 |
| `es2019/globalThisTypeIndexAccess.ts` | 0 | 1 | 1 | 0 |
| `es2020/constructBigint.ts` | 12 | 1 | 11 | 4 |
| `es2021/logicalAssignment/logicalAssignment9.ts` | 5 | 4 | 3 | 0 |
| `es6/Symbols/symbolDeclarationEmit1.ts` | 4 | 0 | 1 | 0 |
| `types/conditional/inferTypes1.ts` | 28 | 25 | 79 | 89 |
| `types/conditional/inferTypesWithExtends2.ts` | 0 | 0 | 4 | 2 |
| `types/keyof/keyofIntersection.ts` | 0 | 0 | 2 | 18 |
| `types/literal/literalTypes1.ts` | 36 | 25 | 2 | 0 |
| `types/typeRelationships/assignmentCompatibility/intersectionIncludingPropFromGlobalAugmentation.ts` | 3 | 6 | 2 | 4 |
| `types/typeRelationships/subtypesAndSuperTypes/stringLiteralTypeIsSubtypeOfString.ts` | 60 | 9 | 3 | 70 |
| `types/typeRelationships/subtypesAndSuperTypes/subtypesOfUnion.ts` | 13 | 1 | 51 | 0 |

## Triage of every text difference

The first fix for **50 differences** belongs in checking/inference. Seven scoped
follow-ups preserve the pinned source evidence instead of expanding this pilot
into a general checker campaign:

| Cluster | Observations | Follow-up |
|---|---:|---|
| Indexed global-object access becomes `any` | 1 | [#3024](https://github.com/nickna/SharpTS/issues/3024) |
| Logical property assignment results/member types become `any` | 4 | [#3025](https://github.com/nickna/SharpTS/issues/3025) |
| Mutable object fields and inferred returns retain fresh literal types | 5 | [#3026](https://github.com/nickna/SharpTS/issues/3026) |
| Switch case/default bodies retain the original literal union | 18 | [#3027](https://github.com/nickna/SharpTS/issues/3027) |
| Recursive conditional mapping retains unresolved `Jsonified<...>` branches | 12 | [#3028](https://github.com/nickna/SharpTS/issues/3028) |
| Higher-order indexed/conditional inference retains `infer R` or loses constraints | 7 | [#3029](https://github.com/nickna/SharpTS/issues/3029) |
| Inherited `hasOwnProperty` replaces `PropertyKey` with `any` | 3 | [#3030](https://github.com/nickna/SharpTS/issues/3030) |

The remaining **22 differences** first need display/source-query context work:

- `inferTypes1`: two observations, alias-name retention for `Example` and the
  upstream spread-element query context for `...args`.
- `literalTypes1`: five `Falsy` alias presentations and two union-order differences.
- `intersectionIncludingPropFromGlobalAugmentation`: three union-order differences.
- `stringLiteralTypeIsSubtypeOfString`: two empty-object whitespace differences
  and seven optional-parameter presentations with `strict: false`. The latter
  need the selected nullability policy when displaying declarations; they are
  not evidence that an omitted/defaulted parameter has the wrong runtime value.
- `subtypesOfUnion`: one merged function/namespace nominal display (`typeof f`).

These classifications do not normalize the comparator. Every original expected
and actual string remains in its structured diff and baseline fingerprint.

## Coverage and unsupported results

The first work for all 161 missing observations belongs in source fact coverage:
built-in callee fast paths (`Date`, `BigInt`, `Symbol`), type-query value names,
assignment-left access nodes, computed member names, and parameter/member names
inside function/type-literal signatures. An absent fact is not assumed to have
the containing expression's type.

The 187 unsupported results comprise:

- 135 unavailable checked facts, predominantly lazy alias declarations and
  error-recovery paths. Alias collection does not eagerly resolve a body:
  experiments showed that doing so changes diagnostics on `inferTypes1`.
- 48 bodyless public overload surfaces whose retained return type is `Inferred`.
  They do not borrow the implementation's return type or silently become `any`.
- Four unnamed nominal interface shapes with no retained source display name.
  They do not produce empty type strings.

Anonymous records, named constructor/instance facets, explicit public overloads,
generator domains and ordinary structural types have focused core display tests.
Type-only interface/type-parameter declaration names are excluded according to
the pinned writer's selection rules; existing editor metadata remains available
for other query APIs.

## Verification and review workflow

The core query/display/declaration filters pass, including shadowed bindings,
class constructor versus instance names, member access, alias resolution,
public overloads, recovery holes, culture independence, untruncated literals and
anonymous cycles. The complete standalone TypeScript conformance project passes
408 tests, including the unchanged 534-case diagnostic aggregate. Its diagnostic
baseline and externally consumed vocabulary are unchanged.

Two consecutive explicit type-baseline generations are byte-identical:
SHA-256 `1f22bd910842bc560f91a06bd80f1bb6d57d56ed503c71e928d1f4f521726db1`.
Normal execution then verifies the committed counts and complete-result
fingerprints with update mode disabled. The README documents generation,
structured JSON output and full failure diffs. Future semantic or coverage fixes
must update their focused core tests, inspect the detailed diff and explicitly
review the separate type baseline.
