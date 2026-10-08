# Editor semantic queries

`TypeChecker.WithEditorMetadata()` opts into source-bound facts for editor features. It also
enables source member provenance. Ordinary compile and interpreter checks leave both disabled.
`NavigationModelBuilder` publishes `EditorFacts.Freeze()` in the shared `AnalysisSnapshot`;
request handlers consume that completed capture instead of retaining or running a checker.

The mutable builder records facts at actual declaration, expression, scope and invocation
decisions. Publication replaces raw types with bounded TypeScript presentations and copies
binding/member identities and collections. The frozen query index retains captured source
documents and AST owners, without checker, resolver, type environment, mutable class types or
inference callbacks. Its document/owner reference keys and generation prevent a query from
combining old and edited captures. A checker reset starts a new generation.

Suppressed preparatory source checks do not publish query/use facts. Each source document's
query facts and non-declaration member uses are cleared before its authoritative pass, while
exact declaration identities, nominal aliases and original signature maps survive. Trusted
declaration-file collection remains its authoritative pass. Visibility requires actual body
entry and refuses unvisited contexts or skipped same-context locals; syntax only supplies
these vetoes, never a replacement scope or inferred type.

## Available facts

| Query | Source of authority | Explicit limits |
| --- | --- | --- |
| Declaration type | Final checked declaration, after widening/contextual checking | A failed declaration is unavailable; an initializer's narrower type does not replace its declared type. |
| Occurrence type | Successful checking of the exact written expression | Generated/recovered expressions cannot establish an authoritative type. An attempted failure replaces an earlier successful fact. |
| Annotation type use | Actual resolution of the exact written type node | Unknown-name fallbacks do not prove a resolved type. Query capture does not force lazy aliases or run another resolution pass. |
| Visible bindings | Checker-owned environments linked to captured source scopes | Local spellings retain canonical imported identity and value/type facets. Unavailable or TDZ locals still shadow outer names. |
| Receiver members | Already checked receiver type and actual lexical class context | Class accessibility, static/instance/private facets and known generic substitutions are retained. Structural members have no invented class origin. |
| Invocation candidates | Resolved callable/constructable type, before argument validation | Public overloads exclude the implementation signature. Unresolved callee types remain unavailable. |
| Selected invocation | Actual successful checker overload/instantiation decision | Argument/inference errors, holes, recovery, incomplete candidate sets and aggregate callable unions cannot prove a single winner. |

Known source transformations have explicit original-owner bridges for contextual callbacks and
fresh object literals. Within a checked graph, source identity is never recovered by matching a
name, type, return type or source range. Workspace navigation separately joins proven canonical
declarations across fresh graphs by full path/span and member facet. Implicit constructors are
marked as implicit and point to the exact source
class, rather than claiming a constructor declaration exists.

The query layer does not perform another inference pass or evaluate a type merely for display.
Exact public callable surfaces also govern nested declaration, occurrence and receiver
presentations when the checker has flattened an overload family into its implementation.
Annotation eligibility is learned at the existing resolution decision and propagated through
exact source declaration/binding identities. Unknown-name fallbacks cannot become proven
declaration, occurrence, visible-local or receiver-member types merely because the checker
represents them as `any`. Alias expansion retains that eligibility through its existing cache,
without publishing substituted facts for the alias definition's original type nodes.
Direct assertion initializers retain the same eligibility through grouping and non-null
wrappers. This is a source-owner check; arbitrary expression/dataflow propagation still follows
the compiler's existing checked type facts.
Unused lazy type aliases retain their spelling and binding identity with an unavailable resolved
type. Generic union projection currently omits a name when any branch carries a generic
substitution map, even if those maps match, and marks the result partial. Existing unsupported
checker paths remain unavailable, including dynamic `any` receivers,
static `super` member lookup and function/record `new` fallbacks without a checked constructor
decision. Instance `super` candidates follow the checker's method-only domain. Source navigation
still requires the narrower [member provenance contract](editor-member-provenance.md).

Unannotated instance fields retain the checker's declared `any` type, including ordinary classes
and class expressions. Checking a numeric initializer does not publish an inferred instance-field
type; an explicit field annotation provides that precision. Static declaration fields have a
separate inference path. The query layer preserves these actual checked results. A generic
method annotation that the checker cannot resolve remains
unavailable for editor presentation. Runtime constructor checking can replace
a public overload set with its implementation function. Editor capture keeps the exact original
public candidates separately, but that implementation-only validation cannot prove selection
of a public overload. Ambient constructor overloads retain the actual chosen signature.
Accessor storage that merges static/instance declarations with the same spelling cannot supply
an unambiguous declaration type. Computed/quoted/numeric member declaration slots remain outside
this source-name domain. A catch body that the checker does not visit supplies no invented facts.
Legacy private/abstract annotations whose structured nodes are not attached to the checker's
resolution slot can still prove their declaration/member eligibility through that exact owner.
The detached annotation spelling itself has no invented resolved type-use fact.

## Bounds and presentation

Receiver publication retains at most 256 names. Class/interface hierarchy and constituent walks
stop at 32, with reference-identity cycle checks and cancellation. Receiver type graph composition
has a separate node budget; an unsupported or exhausted projection is partial/truncated.
Invocation publication retains at most 32 candidates. Truncation cannot establish selection.

`EditorTypeRenderer` does not call `TypeInfo.ToString()`. Default presentation limits are depth 8,
256 type nodes, 4,096 UTF-16 characters and 32 public signatures/type parameters. A 33rd draft
type parameter preserves the truncation marker without copying an unbounded list. Limits are clamped; recursive
types and large lists terminate with explicit truncation. Signature parameter ranges refer to
the rendered label. Missing/inferred analysis displays `unavailable`; an actual checked `any`
displays `any`. Known types outside the renderer's supported forms also display `unavailable`,
including several weak-collection, generator and host builtin forms. Enum values use `E`, since
the checker has no separate enum constructor/instance representation. Generic method type parameters shadow class substitutions. Presentations can
be partial even when the checker has a type, so consumers must inspect availability/truncation.

Visibility refusal applies to the whole query when any enclosing scope has a skipped local;
it does not remove that name and expose a potentially shadowed outer binding.

The shared cache includes the query index's estimated payload. This estimate approximates
collection/object overhead and is separate from measured retained heap; it is not a process
memory limit. Full-mode hover, completion and signature help consume these values. Capture
itself does not change advertised capabilities.

## Completion consumption

`SemanticCompletionService` projects visible bindings and receiver members directly from
the frozen query index. It filters unavailable facts, invalid identifier spellings and
type-only namespace exports in value access. Results are sorted and deduplicated, contain at
most 256 items and use plain text edits replacing the exact whole identifier. Details are
limited to 1,024 UTF-16 characters. Structural candidates retain no invented class identity.

Completed source syntax supplies the context for ordinary queries. An unfinished member
operator can request a fresh cursor-specific checked program through `SemanticAnalysisService`;
the [syntax contract](editor-syntax-coverage.md) describes that isolated parse and its limits.
A parse-only artifact never supplies receiver proof. A missing, stale or foreign seed uses
the configured cold path, and recovered output is revalidated before returning. Lexical
completion refuses documents with parse diagnostics because a discarded declaration could
hide a same-spelled outer binding. It does not create a second scope engine.
Unused lazy aliases therefore supply no typed completion item. Existing checker call results
also remain authoritative: an annotated source-class function can currently yield checked
`any` at its call site despite its declared signature, so that receiver has no member results.

## Signature-help consumption

`SemanticSignatureHelpService` joins the innermost parser invocation to its exact frozen
invocation owner. It projects available public candidates in original ordinal order, with
checked instantiations when available. Unresolved callable annotations are tracked at existing
type/signature construction decisions by reference identity, including anonymous functions,
returned callable types, alias cache reuse and existing substitution paths. Publication
refuses those candidates and clears a selected signature if its proof is unavailable.
Explicit invocation type arguments retain their own resolution proof; an invalid use cannot
taint a shared original signature. A proven formal signature can be displayed when its
instantiation is unavailable. Instantiated parameter names come only from the exact original
signature and a matching parameter count, without mutating the compiler's type objects.
Proof covers captured callable owners and signature-owned annotations. Shared outer generic
interface constraint/default metadata is not separately tracked, so this is not a claim of
complete generic-constraint provenance.

Argument indices count only the parser's recorded top-level commas. Each rendered signature
maps rest arguments and supplies exact UTF-16 parameter label slices. Recovered/hole-bearing,
incomplete and candidates-only facts never establish selection. A top-level active parameter
can follow the first visible signature for protocol presentation while `activeSignature`
remains absent. Signature help does not run another overload resolver or infer selection from
parameter counts, labels or return types. Unfinished argument lists use the checked cursor
bridge; a cached parse-only artifact is never checked again.
An inner invocation that the checker never visits because an enclosing call fails early
remains unavailable; the service does not substitute the outer call's signatures.

## Verification and measured cost

The #1979 signature-help extension adds 115 cases and passed 3,433 affected tests, the
TypeScript smoke profile (32 corpus cases/17 harness tests) and 9 targeted Test262 cases.
Four real stdio clients passed full/interop-only isolation, optional capability negotiation,
unfinished/nested argument lists, exact UTF-16 label slices, dirty dependency/close behavior
and cancellation. Completion, hover and shared-analysis stdio regressions also passed.

The #1978 completion extension adds 99 focused cases and passed 3,318 affected tests, the
TypeScript smoke profile (32 corpus cases/17 harness tests) and 9 targeted Test262 cases.
Four real stdio completion clients passed full/interop-only mode isolation, incomplete
member access, applied UTF-16/CRLF edits, restricted item kinds and dirty dependency/close
behavior. Hover and shared-analysis stdio regressions also passed. Cursor tests cover
configuration/resolution fidelity, fresh AST ownership, seed validation, coalescing,
cancellation, stale inputs, cache limits and independent metadata leases.

The #1977 hover extension passed 3,219 affected tests, the same TypeScript smoke profile
(32 corpus cases/17 harness tests), and 9 targeted Test262 tests. Real stdio hover clients
passed in full/interop-only modes with Markdown/plain text, including imported dirty types,
exact CRLF/UTF-16 ranges and retained CLR/decorator hover. Existing shared-analysis stdio
navigation, rename and cancellation contracts also passed.

The #1976 foundation passed 97 focused tests, 3,128 affected tests, the TypeScript smoke
profile (32 corpus cases across 17 harness tests) and 9 targeted Test262 tests. Selected
shared runtime parity passed 204/204 in compiled/interpreted modes. Existing full-mode
and interop-only stdio contracts also passed.

The [replayable benchmark](../benchmarks/editor-semantics/README.md) compares the exact
`1cfcc46a` baseline with #1976 ordinary, member-only and editor capture on identical
freshly parsed graphs. Diagnostics, public types and lexical identity fingerprints match
across all variants. The recorded single-machine Windows ARM64/.NET 10.0.12 run found
no ordinary median regression above 10%; ordinary allocations increased by 1.31%/1.15%
for the small/25-module fixtures.

| Added editor capture over member-only capture | Small | 25 modules |
| --- | ---: | ---: |
| Cold check and publication, median | +0.6664 ms | +44.4984 ms |
| Allocated bytes per check | +504,298 | +20,393,888 |
| Retained heap after full GC | +69,912 bytes | +2,830,328 bytes |

Allocation churn, retained heap and index estimates are separate measurements. Immutable
read batches took 0.00975 ms for 21 queries and 2.45651 ms for 525 queries (medians), without
parsing or checking. See the [compact results](../benchmarks/editor-semantics/results-2026-10-07.json)
for p90 values and counts. These warmed-process measurements exclude LSP transport,
handler rendering and snapshot acquisition; #1981 still needs interactive measurements
with snapshot reuse. They do not establish a general speedup or a process heap limit.
