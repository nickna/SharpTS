# Source class member provenance

`TypeChecker.WithMemberProvenance()` records source class origins separately from
lexical bindings. The language server freezes both indexes into one checked
analysis. This is metadata for editor consumers; it does not change diagnostics,
type results or runtime behavior. Ordinary compiler checking leaves capture off.

Member uses also require parser-owned authoritative source views from
`WithSourceDocument(document).WithEditorSyntax()`. The language-server builder
enables both. Enabling checker capture alone can retain direct declarations,
but use occurrences remain unavailable without those exact written-owner views.

One captured document and reference-equal class AST define a canonical source
owner. Checker declaration IDs observed during preparation and body checking are
aliases of that owner. The first observed ID is its stable canonical ID; integer
equality alone does not establish source identity. A new checker generation
produces new member identities. Frozen copies remain independent of later checks.

Suppressed preparatory source checks do not publish use operations. Before a source document's
authoritative pass, use facts are cleared while its declaration groups and nominal aliases
remain registered. A body skipped by the final check therefore cannot leave a preparatory
member reference behind. Trusted declaration files retain their authoritative collection pass.

| Source or operation | Provenance contract |
| --- | --- |
| Identifier-named source fields, methods, auto-accessors and parameter-properties | Direct source declarations form canonical groups, with separate instance/static facets. User `.d.ts` classes and class expressions participate. |
| Legal overloads and getter/setter pairs | One canonical group retains every source declaration. Invalid mixed declarations do not merge merely because their names collide. |
| Inherited and generic members | The checker-selected declaring class supplies the origin. An override has its own origin; supported instantiated/inherited generic lookups retain the base origin. |
| Named reads, writes, calls, `this` and `super` | Actual successful checker-selected lookup branches record separate operation facts. A failed call can retain a proven member read without claiming call proof. |
| Repeated checker passes | A later check replaces the same AST owner's operation fact. Distinct owners or operations combine conservatively; earlier speculative failures cannot poison an authoritative recheck. |
| Union members | Every required constituent must be supported and agree on one canonical origin. Different origins remain explicit candidates and yield no authoritative definition/reference result. |
| Nullable optional access and unvisited intersection constituents | Unproven branches keep the aggregate unavailable. The index does not run additional checker lookups to infer an identity. |
| ECMAScript private reads/writes/calls | The checker-selected lexical owner and nominal receiver must prove the correct instance/static private facet. Source-owner aliases preserve that proof across checker passes. |
| `#field in candidate` | A successful check names the exact lexical brand, including a checker-valid `object` or `any` candidate. The probed object's nominal class is not the brand identity. |
| Updates | A checked operand such as `c.value++` can retain a proven read. Existing update checking does not establish a separate source-member write. |
| Compound/logical member assignments and literal-key class indexing | Existing paths such as `c.value += 1`, `c.value ||= 1` and `c['value']` can return `any` without selecting a named class member. Those paths remain unavailable. Exact key ranges are necessary, but do not themselves prove an origin. |
| Property-flow narrowings | The current early return supplies a narrowed type without a new member selection. Carrying the previous origin would require provenance to follow the same narrowing-context invalidation, scope and merge rules; no global name/path guess is used. |
| Existing `any` fallbacks | Generic self-typed method parameters and some forward class-typed `.ts` parameters currently become `any`. Capture on/off preserve that behavior and refuse a fabricated origin; supported local construction and declaration-order-safe types still participate. |
| Structural/interface/record/mapped/index-signature, dynamic/unknown/error, CLR and built-in domains | No source class identity is fabricated. Quoted/numeric/arbitrary computed declarations are outside this first source-member scope. |
| Generated syntax | An in-range token is insufficient. The exact occurrence owner needs an authoritative written member-name view, so generated parameter-property prologue writes cannot impersonate source uses. |

`MemberResolution` retains candidates and lookup completeness separately.
`IsResolved` requires one candidate and complete required evidence. Frozen
definition and known-reference queries require that resolved identity. Project
graph completeness is a separate analysis fact.

Full-mode go-to-definition consumes these proven origins after lexical binding selection.
It returns each canonical group's exact source declaration-name locations, deduplicated and
ordered by path and span. Targets use their own captured source document, including dirty
dependencies. Unrelated graph incompleteness does not erase a proven target; stale results
are discarded.

Full-mode references select lexical bindings first, then require one proven member identity.
They join separately checked projects using canonical declaration path/full name spans plus
member name, kind and instance/static/private facet. Checker IDs and AST references are local
to their own generation. Fresh canonical groups supply only their resolved frozen occurrences;
locations use each target document's own line index and deduplicate full spans.

The initialized workspace's configured discovery graph must be complete before any new member
reference locations are collected. Missing roots/configurations/project references return an
empty member result without leaking selected-document uses. Output stays within initialized
roots. Dirty overlays, closed reverse importers and project references share the same checked
workspace inputs and final validation.

A complete graph is distinct from complete semantic evidence for every possible use. A project
whose checker never registered the selected member contributes no invented occurrences; the
frozen index retains unavailable evidence independently. Structural compatibility, runtime
implementors and spelling do not establish a class-member reference.

Every new member identity currently denies rename. A parameter-property's
constructor-local lexical binding also denies rename because editing that facet
alone would miss property uses. Ordinary lexical bindings retain their existing
complete-graph rename behavior. Later private rename support must prove its
specific local domain; this index does not grant that permission.

`MemberIndexTests`, `SourceMemberDeclarationTests`, `SourceMemberOccurrenceTests`
and `MemberAnalysisTests` verify identities, operation evidence, source ownership,
refusals and snapshot lifetime. The member definition service, snapshot and handler tests
verify final locations, lexical precedence, capture reuse and cancellation/currentness.
`RenameServiceTests` independently protects the parameter-property gate.

The #1980 extension adds 42 service/handler cases and passed 3,475 affected tests. Real stdio
verification covers two configured projects, a project reference, a closed reverse importer,
exact UTF-16/CRLF ranges, dirty owner versions and close restoration, incomplete discovery,
member rename refusal and cancellation. Shared-analysis/navigation and signature-help stdio
regressions also passed. Imported generic/path-alias fixtures caught and verify a Windows
drive-letter casing fix in configured component selection, without weakening their assertions.

The optional [member-reference benchmark](../benchmarks/member-references/README.md) records
cold/warm service latency, allocations, result payload sizes, cache estimates and checker counts
for one-source and 25-source/four-configuration fixtures. Both retain zero warm whole-graph checks.

The optional [member provenance benchmark](../benchmarks/member-provenance/README.md)
compares ordinary and enabled checking against the preceding commit, including
allocation, retained publication size and semantic identity fingerprints.
