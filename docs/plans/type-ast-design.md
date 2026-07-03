# Type-AST design: replacing the string-based type pipeline

**Status:** ✅ complete — slices 1–6 shipped; the string scanner is deleted (#1111)
**Driver:** three independent conformance blockers and a recurring bug class, all rooted in
types passing through lossy string round-trips between the parser and the checker.
(The "problem today" section below describes the pre-migration state, kept for context.)

## The problem today

The parser tokenizes and *structurally* parses every type annotation — then throws the
structure away, re-rendering it to a `string` (`Parser.Types.cs`, ~1,100 lines). The checker
re-parses that string with a second, hand-rolled character-scanning parser
(`TypeChecker.TypeParsing.cs`, ~1,900 lines) to produce `TypeInfo` (the semantic type model).

Costs, all observed in practice during the 2026-06-10/11 conformance push:

1. **A recurring bug class.** The checker-side string scanner re-implements bracket matching,
   union splitting, member splitting, etc. We fixed four instances of the same
   `>`-of-`=>` mis-splitting family, a curried mis-parse of two-call-signature object types,
   and an alias-whitespace bug — each a new leak in the same dam.
2. **No declaration identity.** Two `class Base { foo: string }` declarations in different
   modules are different types to tsc but identical strings to us. This caps
   `assignmentCompatWithObjectMembers{4,Optionality2,StringNumericNames}` (cross-module
   diagnostic-code fidelity) and forced the `TypeInfo.CacheKey()` structural-fingerprint
   workaround for interfaces.
3. **No substitution origin.** tsc's `isInstantiatedGenericParameter` — "did this parameter
   type come from substituting a type parameter?" — gates the always-on callback comparison
   rule. The information cannot survive a string round-trip. Blocks `covariantCallbacks`
   (#202) and alias-instantiation variance.
4. **No source spans.** Diagnostics inside type annotations attach to the statement line, not
   the offending type position.
5. **Double parsing** of every annotation, plus `ToString()`-keyed caches that make rendering
   a correctness concern (three cache-collision incidents).

## The design

Mirror tsc's Node/Type split. Two layers, with an explicit boundary:

```
source ──Lexer──> tokens ──Parser──> TypeNode  (SYNTAX: what was written, where)
                                        │
                              TypeResolver (checker)
                                        │
                                        v
                                    TypeInfo   (SEMANTICS: what it means)
```

- **`TypeNode`** (new, `Parsing/TypeNodes.cs`): immutable records describing type *syntax* —
  `NamedTypeNode("Map", [k, v])`, `UnionTypeNode([...])`, `ArrayTypeNode(elem)`,
  `LiteralTypeNode(...)`, etc. Each carries its source line. Built by the parser in the same
  pass that today builds the string — the structure already exists; we stop discarding it.
- **`TypeInfo`** (existing, unchanged role): the checker's semantic model. Resolution
  (`ToTypeInfo(TypeNode)`) happens in the checker where scopes/generics live, exactly like
  today's `ToTypeInfo(string)` — minus the string re-parse.
- **Identity and origin live on the semantic layer, fed by the syntactic one.** Once
  resolution starts from nodes, a `NamedTypeNode` resolves through the environment to a
  *declaration*, so `TypeInfo` for classes/interfaces can carry a declaration handle
  (fixing problem 2), and `Substitute` can mark results with their origin (fixing problem 3).
  These are follow-ups enabled by the node layer, not part of the first slices.

### Why two layers (and not one)?

Considered: making `TypeInfo` itself carry syntax (one layer). Rejected — `TypeInfo` is
compared, substituted, and cached by *meaning*; syntax (spans, written form) would poison
structural equality and the caches. The two-layer split is also what tsc itself does
(`TypeNode` vs `Type`), so conformance reasoning maps 1:1.

### Migration strategy: dual-path, node-first with string fallback

The same playbook as the RuntimeValue/V2 migration (incremental, finished cleanly):

1. Parser's existing type-parsing functions return `(string, TypeNode?)` — the string exactly
   as today (zero behavior change), the node when every sub-component produced one. An
   unsupported construct anywhere inside yields a null node; the consumer falls back to the
   string path. **The string is authoritative until the end of the migration.**
2. AST statement/expression records grow optional `…Node` fields next to their existing
   string annotation fields, populated when available.
3. The checker prefers `ToTypeInfo(TypeNode)` when a node is present, falling back to
   `ToTypeInfo(string)` otherwise. Node conversion reuses the existing resolution helpers
   (`ResolveGenericType`, environment lookups), so semantics are shared, not duplicated.
4. Expand node coverage construct-by-construct (each step measured against both suites),
   then site-by-site flip consumers off the string fields, then delete the string scanner.

**Safety net:** the conformance suite (63 pinned Passes, (line, TSnnnn)-exact) and the
10,650+ unit suite. Every increment must keep both identical — the slice in this PR includes
an equivalence test that converts both ways and asserts identical `TypeInfo` renderings.

### Slice shipped with this document

- `TypeNode` records: named (with type arguments), primitives/keywords, string/number/boolean
  literals, array suffix, union, parenthesized.
- Parser: `ParsePrimaryType` / union / annotation entry points build nodes opportunistically.
- `Stmt.Var.TypeAnnotationNode` populated; `VisitVar` resolves node-first.
- Instrumentation: `TypeNodeStats` counters (node-hit vs string-fallback), to size the
  remaining migration. **Measured over the 79-test conformance corpus: 37% of variable
  annotations already resolve through the node path with just this slice** (198 node / 337
  fallback); function and object types dominate the fallback set, confirming slices 1–2 as
  the high-value next steps.

### Order of subsequent slices (each independently shippable)

1. ✅ **Shipped.** Function/constructor type nodes (`(a: T) => R`, `new (...) => R`), plus
   `const` annotations wired into the node path. Coverage over the conformance corpus:
   37% → 46.4% (254 node / 293 fallback). Generic function/constructor types
   (`<T>(…) => R`) and `this`-parameter constructor types still fall back (slice 3).
   Shipping this slice immediately caught the **fifth** `>`-of-`=>` instance: the node
   path parsed `(x: (a: B) => D, y: (a2: B) => D) => R` correctly while the string path's
   `SplitFunctionParams` fused both parameters into one — previously masked because both
   sides of the comparison were equally mangled. The string path got the same arrow guard
   `SplitObjectMembers` already had.
2. ✅ **Shipped (object + tuple).** `ObjectTypeNode` (properties with optional/method
   markers, computed names in their `@@` spelling, index signatures, call/construct
   signatures) and `TupleTypeNode` (named/optional/spread elements, trailing-rest rule,
   TS1257 ordering). `ParseMethodSignature` became a node producer alongside
   `ParseFunctionTypeBody`. Coverage: 46.4% → **65.3%** (357 node / 190 fallback).
   Conditional and mapped types remain string-path — they tie into generic/alias
   resolution, so they ride with slice 3.
3. Type aliases store nodes (kills alias string substitution; enables alias-instantiation
   identity for #202's variance cases).
   - ✅ **3a shipped: generic references.** `NamedTypeNode.TypeArguments` is built by the
     parser; the checker resolves argument nodes and reuses `ResolveGenericType`'s
     existing `TypeInfo`-args overload (built-in generics, utility types, generic
     classes/interfaces/functions, same TS2314 arity errors). Generic **aliases**
     deliberately still fall back — their expansion is textual substitution of the
     ORIGINAL argument spellings, so node-resolved arguments could shift
     recursion/instantiation keys. Coverage: 65.3% → **68.7%**.
   - ✅ **3b shipped: alias definitions as nodes.** `Stmt.TypeAlias.TypeDefinitionNode` →
     `TypeEnvironment` stores it beside the string; expansion binds the type parameters to
     the resolved arguments in a child scope (`DefineTypeParameter` — consulted first by
     name lookup) and resolves the definition node directly. No argument-string
     substitution, no definition re-parse. Guards mirrored exactly: TS2314 arity,
     open-type-variable deferral, TS2589 depth, recursion placeholder with the same key
     derivation. Built-in generic names shadow aliases on both paths
     (`IsBuiltInGenericName`). Aliases with mapped/conditional bodies naturally fall back
     (those constructs have no nodes yet). Corpus coverage flat at 68.7% — the corpus's
     aliases are mapped/conditional-heavy; the win is the mechanism (structural
     instantiation), which is what slice 5's substitution-origin marking builds on.
4. Declaration handles on `TypeInfo.Interface/Class` resolved via nodes (kills the
   `CacheKey()` workaround; fixes the cross-module diagnostic tests).
   - ✅ **Shipped (first installment): Pass 63 → 66.** Three mechanisms:
     (a) classes carry a per-declaration id (`MutableClass.DeclarationId` →
     `ClassMetadataCore`), mixed into `Class`/`Instance.CacheKey` — same-name classes in
     different scopes stop sharing compat-cache verdicts (`assignmentCompatWithObjectMembers4`);
     (b) the TS2741 first-failure set keys on INSTANCE identity, mirroring tsc's per-type-id
     relation cache (`…Optionality2`, `…StringNumericNames`);
     (c) found-by-(a): namespace type pre-registration leaked members into the enclosing
     scope, so a later same-named interface bound to the earlier declaration — pre-registration
     now happens inside the namespace scope (`assignmentCompatWithObjectMembers`, where the
     leak and a name-keyed stale-true cache hit had been cancelling each other out).
     Interface declaration ids (replacing the `CacheKey()` structural fingerprint) remain —
     they need instantiation-aware keys, which ties into slice 5.
5. ✅ **Shipped: substitution-origin marking — `covariantCallbacks` passes (Pass 66 → 67), closing #202.**
   `TypeInfo.Function.InstantiatedTypeParamPositions` records which parameter positions were
   NAKED type parameters before instantiation (tsc's `isInstantiatedGenericParameter`),
   set by `Substitute` (rewrite path) and by node-path alias expansion (where substitution
   is a scope binding — the mark detects a bare type-parameter reference bound to a
   concrete type). The callback comparison rule is now ALWAYS ON (was measurement-scoped),
   gated off at marked positions: `set(value: T)` instantiated with a function type stays
   bivariant (tsc #51620), while declared callback positions (`forEach(cb: (item: A) => void)`)
   relate covariantly. Marks participate in `CacheKey()` (origin changes assignability).
   Wiring fixed en route: ambient `declare let/const` annotations and pre-registered
   (forward-referenced) alias definitions now carry their nodes; arrow-hoisting resolves
   annotations node-first so both resolutions of one annotation agree.
5a. ✅ **Shipped: operator + conditional coverage (toward slice 6).** Nodes for the remaining
   composite operators and conditionals, so they stop falling back to the string scanner:
   `IntersectionTypeNode` (resolved through the same `SimplifyIntersection`), `KeyofTypeNode`,
   `IndexedAccessTypeNode` (chained `T[K][J]` nests structurally), `ConditionalTypeNode` +
   `InferTypeNode` (the deferred `ConditionalType` is built node-first; distribution and `infer`
   inference still run path-independently in `EvaluateConditionalType`), and `TypeQueryNode`
   (`typeof`, delegating to the same `EvaluateTypeOf`). Conditional alias definitions now carry
   nodes, so generic conditional aliases expand through `TryExpandGenericAliasFromNode`. Corpus
   coverage: 68.7% → **75.0%** (410 node / 137 fallback). No baseline movement — the conditional
   Fails are `EvaluateConditionalType`/`infer`-inference semantic gaps, not string round-trip
   damage (verified: node and string paths produce identical verdicts).
5b. ✅ **Shipped: mapped types.** `MappedTypeNode` (parameter, constraint, value, optional
   `as`-clause, and the `+/-readonly` / `+/-?` modifier flags carried as bools so the syntax layer
   needs no dependency on `MappedTypeModifiers`). `ParseMappedType` is now a node producer;
   resolution (`TryResolveMappedType`) mirrors `ParseMappedTypeInfo` exactly — constraint first,
   then the mapped parameter registered in `_openTypeVariablesInScope` (the SAME shared set) while
   the as-clause and value type resolve, so their bodies build the identical deferred
   IndexedAccess/TypeParameter forms `ExpandMappedType` substitutes per key. Generic mapped alias
   definitions now expand through the node path (`TryExpandGenericAliasFromNode`'s post-pass calls
   `ExpandMappedType`). A mapped type whose `as`-clause needs a template-literal (no node yet)
   falls back whole. Corpus annotation-site coverage flat at 75.0% (the corpus's mapped types live
   inside alias definitions, which the coarse annotation counter doesn't measure) — like slice 3b,
   the win is the mechanism. No baseline movement; full unit suite green.

5c. ✅ **Shipped: generic signatures + template-literal types.** `GenericFunctionTypeNode`
   (carries the `TypeParam` list and the body `FunctionTypeNode`; `TryResolveGenericFunctionType`
   mirrors `TryParseGenericFunctionTypeInfo`'s two-pass scope so constraints/defaults and the body's
   `T`s resolve identically) and `TemplateLiteralTypeNode` (N+1 static segments around N
   interpolations; resolves through the same `NormalizeTemplateLiteralType` — concrete parts expand
   to a string-literal union, a `string` part stays a pattern type). **Fixed a pre-existing latent
   bug en route:** template-literal types in type position were entirely broken — the lexer emits a
   `TemplateStringValue` (cooked+raw) but the type parser still cast `Token.Literal` to `string`,
   so every `` `a${T}` `` annotation threw a parse error on the *string* path too. Now reads
   `.Cooked` (both paths). Corpus coverage: 75.0% → **87.6%** (479 node / 68 fallback). No baseline
   movement. Generic *constructor* types (`new <T>(…) => R`) deliberately still fall back.

5d. ✅ **Shipped: long-tail constructs.** `ReadonlyTypeNode` (`readonly T[]` / `readonly [A,B]`
   → marks the resolved array/tuple readonly), qualified names (the dotted name carries on
   `NamedTypeNode`, handed to the same single-name / `ResolveGenericType` path), `TypePredicateNode`
   + `AssertsNonNullTypeNode` (`x is T`, `asserts x is T`, `asserts x` — completes function types
   with predicate returns), and constrained `infer U extends C` (the whole `U extends C` becomes the
   inferred parameter's name, matching the string path's `InferredTypeParameter` quirk exactly).
   Corpus annotation-site coverage flat at 87.6% (these forms live in alias defs / function returns,
   not the three counted sites) — mechanism, not metric. No baseline movement; equivalence verified
   (e.g. qualified namespace type-alias exports resolve permissively to `any` on BOTH paths).

5e. ✅ **Shipped: generic signatures — 100% corpus annotation coverage.** An audit of the
   remaining fallbacks showed they were almost entirely generic constructor types and generic
   call/construct signatures. `GenericConstructorTypeNode` (`new <T>(…) => R` → object type with a
   generic construct signature) and object-type generic call/construct signatures
   (`{ <T>(x): T; <U>(a): U }`, `{ new <T>(x): T[] }`) — `ParseMethodSignature` now keeps its
   type parameters and emits a `GenericFunctionTypeNode`; the call/construct signature member nodes
   widened from `FunctionTypeNode` to `TypeNode`. All share one `TryResolveGenericSignature` helper
   (the two-pass type-parameter scope, mirroring the string path's `ResolveSignature`). Corpus
   coverage: 87.6% → **100.0% (547 node / 0 fallback)** — every annotation site in the 79-file
   corpus now resolves through the node path; the string scanner is no longer reached for any of
   them. No baseline movement; full unit suite green.

   The string path (`ToTypeInfo(string)` / `TypeChecker.TypeParsing.cs`) is still reached for a few
   long-tail constructs absent from the corpus — bigint literal types (`1n`), `this`-parameter
   generic/constructor types, `unique symbol` — and remains the implementation for the REPL/embedding
   API.

   **The construct layer is done; the consumer layer is not.** Coverage above is measured at the
   `ResolveAnnotation` sites only (originally var/const/function-param-hoist + alias defs + generic
   args). The *other* annotation consumers — class fields, function/method params, return types,
   `this` types, interface members, accessors, index signatures, type assertions — still call
   `ToTypeInfo(string)` directly and must each be flipped to the node path before the scanner can go.
   There are also internal **rendered-string round-trips** (`ResolveGenericType` arg-string overload,
   string alias expansion, `SubstituteTypeParamInString`) whose strings are NOT valid source, so
   they can't be reparsed — they need structural reimplementation, independent of the consumer flips.

5f. ✅ **Shipped (first consumer flip): class field annotations.** `Stmt.Field.TypeAnnotationNode`
   populated by the parser (captured immediately after the field's `ParseTypeAnnotation`, before the
   initializer expression); both checker passes (signature collection + body check) resolve via
   `ResolveAnnotation`. Constructor **parameter properties** (`constructor(public x: T)`) are a
   separate field-creation path still on the string path. No baseline movement; full unit suite green.

### Slice 6 — deleting the scanner ✅ **Shipped (#1111)**
1. ✅ **Construct gaps closed.** The last three node-less constructs: bigint literal types (`1n`,
   `LiteralTypeNode` carrying the `BigInteger`; resolves to `Any` for parity with the scanner's
   unknown-name tail — real `BigIntLiteral` semantics are a follow-up), `unique symbol`
   (`UniqueSymbolTypeNode`; resolution throws the same TS1331, the const-`Symbol()` special case
   still short-circuits before resolution), and `this`-parameter constructor types
   (`ConstructorTypeNode.ThisType`; resolved then dropped from the construct signature, exactly
   like `ConvertConstructSignatures` did).
2. ✅ **All remaining consumers flipped** to node-first `ResolveAnnotation`: `TypeParam`
   constraints/defaults (node twins on the record), function/method/arrow signatures
   (params/returns/`this`, both hoisting and body-check passes), interface call/construct
   signatures, auto-accessors, catch bindings, `using` bindings, ambient module vars,
   `Expr.Call`/`Expr.New` type arguments, class heritage and interface-extends type arguments.
   Two divergences surfaced by the flips were ported, not papered over: the
   `IsDeferredKeyDomain` mapped-expansion guard (#337 — without it the conditionalTypes1 f7/f8
   verdicts flip) and eager `SimplifyConcreteIndexedAccess` on `IndexedAccessTypeNode` (#365).
3. ✅ **Rendered-string round-trips eliminated.** Non-generic aliases store their definition NODE
   in `TypeEnvironment` beside the string (the string stays: it keys the expansion cache and
   discriminates same-named aliases across scopes); expansion is node-first inside
   `ResolveTypeName` with the identical cache/recursion/TS2456 guards. Generic alias expansion is
   node-only (`TryExpandGenericAliasFromNode`); `SubstituteTypeParamInString` and the
   argument-string substitution are gone. `GetReferenceParamConstraints` returns `TypeInfo`
   directly instead of rendering and re-parsing.
4. ✅ **Scanner deleted** (~1,500 lines out of `TypeChecker.TypeParsing.cs`, plus
   `ParseGenericTypeReference`/`SplitTypeArguments`). What survives in that file:
   `ResolveTypeName` — the shared single-name resolver both paths use (type-param scope,
   node-first alias expansion, primitives/lib globals/typed arrays/Error names, open type
   variables, environment tail; the TS2456 depth backstop rides on it, which every unbounded
   resolution cycle traverses) — plus `SimplifyIntersection`, the template-literal normalization
   family, and the `EvaluateTypeOf` family, which the node path resolves through.
   `ToTypeInfo(string)` survives only as parse-to-node + convert (`Parser.TryParseTypeFragment`:
   the real lexer + type grammar over the fragment, EOF-checked; unparseable text → `any`,
   matching the old unknown tail) serving `ResolveAnnotation`'s defensive fallback and the
   embedding surface. `TypeNodeEndStateTests` pins zero string fallbacks across a
   construct-corpus program.

### Non-goals

- No change to `TypeInfo`'s public shape in this phase (compat caches, IL compiler, and the
  declaration generator all consume it).
- No parser grammar changes — nodes capture exactly what the string parser already accepts.
- The interpreter/IL compiler are untouched until slice 6 (they never see type strings today;
  types are erased before execution).
