# Editor syntax provenance

`Parser.WithEditorSyntax()` captures an immutable `EditorSyntaxIndex` owned by the
`SourceDocument`. Ordinary parsing keeps its existing statement/debugger spans
and does not allocate an editor index. The editor index adds views over existing
AST objects; it does not add position fields to records or change their equality.

| Consumer syntax | Captured source views and contexts | Limits |
| --- | --- | --- |
| Lexical names | Written variable references, assignment/update targets and declaration/parameter/type names | Generated temporary names have no written-name location. |
| Members | Receiver, operator, name token and whole access; optional/private flags; literal string/number index keys | Dynamic computed indexes retain their expression range, without inventing a static member identity. |
| Calls and construction | Whole expression, callee, opening/closing parentheses and only top-level argument commas; optional/new flags | `new C` has no fabricated argument-list delimiter. Tagged templates keep expression provenance without posing as parenthesized calls. |
| Expression nesting | Intermediate chain nodes, binary/logical/conditional/comma/unary/wrapper expressions, literals, arrays/objects, templates and updates | Destructuring and parser replacements use source-equivalent provenance where an existing source construct survives lowering. Later compiler transforms are outside this index. |
| Functions and classes | Declaration/function-expression/arrow whole and header views; body, parameter list, parameter names and class member names | Method header views begin at the parsed name and exclude preceding modifiers. A computed method header begins at `(`; its computed key has a separate expression range. Overload/ambient signatures have no invented body. Generated runtime wrapper functions are not user declarations. |
| Blocks | Written brace/body extent and nested statement spans | A parser-created statement sequence is not an extra written block. |
| Written types | Existing type-node roots and nested forms, annotations, qualified names, type parameters and grouping views | Parenthesized types can share one semantic type object and have separate grouping views. Implicit `any` has no written type range. |
| TSX | Original element/attribute/type-argument ranges and embedded source expressions, preserved through JSX lowering | Factory/runtime imports and generated props/children scaffolding do not acquire written declaration or call-delimiter identities. |
| Unfinished expressions | Private cursor-local artifact for `receiver.`, `receiver?.`, `super.`, `f(`, `f(a,`, `f(a, ,`, `new C(` and bounded nested variants | Missing names/arguments/closers are explicit recovery, not authoritative rename or definition locations. Arbitrary-error recovery remains unavailable. |

All ranges are half-open UTF-16 offsets into exactly one document. Syntax lookup
uses binary search over flat interval arrays, then selects the narrowest supported
view. Ties use start position, kind, role and original recording order. Invocation
and member caret contexts also accept the boundary immediately before a close or
after an unfinished token; that caret convention does not widen written ranges.
Only `Written`, `Grouping` and `SourceEquivalent` views can supply authoritative
source matches. `Recovered`, `Synthetic` and `Implicit` views cannot.

Publication filters every view against objects reachable from the final parser
AST, including auxiliary parameter/property/type objects. It also prunes opt-in
span entries for abandoned speculative objects. Split `>>`/`>>>` generic closers
track the exact consumed character, and speculative checkpoints restore both
the cursor and the original token. Copy callbacks are detached at completion or
cancellation, and the mutable collector does not survive publication.

For parser branches whose existing semantic AST deliberately keeps only a type
spelling, editor-only owner attachments retain the already-parsed type syntax.
These edges follow source-equivalent owner replacements and disappear if their
owner is abandoned. They preserve editor annotation views without changing the
checker-visible AST or implicit-type behavior.

`Parser.ParseForEditor` uses unchanged source text with a fresh document/token
stream and explicit query/policy inputs. A repaired gap must actually contain
the caret, between consumed source and the next real token; an unrelated broken
statement before or after the caret is not repaired. Repairs have count/nesting
bounds and are disabled inside comments and literals. Missing-argument comma
lookahead stops beyond the remaining repair budget and observes cancellation.
`super.` keeps its written keyword as receiver proof while its missing name and
whole recovered access remain non-authoritative. Bare `this.#` still fails the
ordinary lexer, and arbitrary broken enclosing braces remain unsupported.

The language server keeps two distinct uses of this parser. Its existing syntax
artifact cache remains parse-only: it never checks a cached artifact or adds
bindings to it. Semantic cursor analysis instead creates a fresh checked graph.
An opt-in `ModuleResolver.EditorParseTarget` replaces only the exact canonical
target module's parse with a fresh `ParseForEditor` result. The resolver's
original lexer still supplies reference directives and JSX pragmas, and normal
configuration, imports, path aliases, declaration preference and program/library
processing continue. Other source modules use fresh ordinary parses; embedded
read-only library declarations keep their existing shared path. No published
base AST or cached syntax artifact is checked again.

A seed must belong to the same analysis service, match the exact originating
request stamp and target path, and remain current. Its captured reads, probes
and directory inventories are replayed before reconstructing configuration and
resolution, with extra reads captured as needed. The build owns a fresh metadata
view and validates both seed and combined inputs before publication. A missing,
foreign or mismatched seed uses a fresh configured build; a configured cursor
build refuses fallback if configuration or membership fails, or checking cannot
produce a model.

Checked cursor graphs share the ordinary admission slots, LRU and default
64 MiB estimated payload budget, with at most four completed cursor entries.
They acquire no ordinary document aliases. Cache keys include caret, query,
policy and an opaque weak-table base identity that does not retain the base AST.
Only the in-flight build retains a seed lease; a completed child does not own its
seed. The separate
parse-only cache stays bounded within the same combined byte budget.

`EditorExpressionTests`, `EditorDeclarationSyntaxTests`, `EditorSyntaxIndexTests`
and `EditorExpressionRecoveryTests` pin these contracts with exact-offset and
query fixtures. `CursorRecoveryBoundaryTests` and `EditorParseTargetResolverTests`
cover caret-gap limits and fresh target parsing through the normal module graph.
Expression catalog classification and a separate concrete
`TypeNode` catalog guard require an explicit provenance decision when those
families grow; auxiliary owners have focused reachability tests because they
are outside the expression/statement catalog. Existing source-span, type-node,
debugger and navigation suites protect ordinary behavior. Replay parse time and
allocation measurements with [the optional benchmark](../benchmarks/editor-syntax/README.md).
