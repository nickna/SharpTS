# Plan: append-only string-local promotion to `StringBuilder` (#857 follow-on / new #856 child)

## STATUS: Phase 1 implemented (2026-06-20)

Implemented and validated. Files: new `Compilation/StringAccumulatorPromotionAnalyzer.cs`; `TypeSystem/TypeMap.cs`, `Compilation/CompilationContext.cs` (`TryGetPromotedStringAccumulator`), `ILCompiler.cs` (wired at both single- and multi-module sites), `ILEmitter.Statements.cs` (StringBuilder slot), `ILEmitter.Expressions.cs` (`=` append), `ILEmitter.Operators.cs` (`+=` append), `ILEmitter.Properties.cs` (`.length` + `charCodeAt` helper), `ILEmitter.Calls.MethodDispatch.cs` (`charCodeAt` hook); tests `SharpTS.Tests/SharedTests/StringAccumulatorPromotionTests.cs` (16 cases, both modes, green).

**Result:** bundled `strings.ts` @10k **17.3 ms → 0.265 ms (65×)**, O(n²)→O(n), now ~2.3× Node (was 149×). IL verifies. Full xUnit suite green except pre-existing flaky network + the 2 documented stale Test262 baselines (drift is `Array.isArray`/`proxy`, present in interpreter mode too → unrelated to this compile-only change).

**One critical deviation from the template below:** candidacy is keyed **per function scope**, NOT whole-program-per-lexeme like `ArrayLocalPromotionAnalyzer`. The array analyzer's whole-program lexeme guard silently fails for common names in bundles — a clean `s` in one function is poisoned by an unrelated escaping `s` in another module (e.g. `perf_hooks`'s `const s = findMark(...)`). The string analyzer enters a new scope per `Stmt.Function`/`Expr.ArrowFunction`; cross-scope references are captures, caught by the `IsVariableCaptured` guard. **Follow-up: apply the same per-scope keying to `ArrayLocalPromotionAnalyzer`** — it has the same latent bug, dodged only by uncommon array names.

---


## Why

Re-measured **warm, content-forced** (2026-06, `dotnet build -c Release`), compiled vs Node:

| strings @10k phase | Compiled | Node | Slowdown |
|---|---|---|---|
| concat-only (`s = s + "ab"` ×n, return `s.length`) | 18.0 ms | 0.05 ms | **337×** |
| scan-only (`charCodeAt` sweep) | 0.96 ms | 0.05 ms | 21× |

The concat is ~95% of the strings benchmark and is **O(n²)**: content-forced scaling 5k/10k/20k/40k = 6.5 / 17.8 / 74 / **351 ms** (≈4× per 2× n), vs Node O(n) (cons-strings) at 0.26 ms @40k → **1340× @40k and growing without bound**. The IL emits `String.Concat(string,string)` per iteration, copying the whole accumulator each time (`ILEmitter.Operators.cs`, the 2-operand both-string fast path from `fed6fc0a`).

`Compilation/StringConcatOptimizer.cs` only flattens **intra-expression** chains (`"a"+b+c`, `MinPartsForOptimization=3`). Loop-carried accumulation is unhandled. **No StringBuilder accumulator was ever committed** (checked git history; `fed6fc0a` = typed-string concat + `ArrayLocalPromotionAnalyzer`, nothing else). We build it fresh.

Goal: detect a provably **append-only** string local and back it with a `System.Text.StringBuilder`, turning O(n²) → O(n). Target: strings @10k from ~17 ms toward sub-millisecond.

## Template

This is the direct analog of the existing, shipped `ArrayLocalPromotionAnalyzer` (#857/#860). Follow it exactly:
- whole-program AST visitor, conservative non-escaping first cut;
- permitted-use overrides that consume the receiver variable **without** recursing into it;
- catch-all `VisitVariable` disqualifies any other bare occurrence (escape detection);
- results written to `TypeMap`; the IL emitter's fast paths key off the **slot's CLR type** via `LocalsManager`, so a captured/un-promoted name can never accidentally hit them.

Key correctness shortcut: **.NET `StringBuilder.Length` == JS `.length`** and **`sb[i]` (char) == `charCodeAt(i)`** — both are UTF-16 code units. So `length` and `charCodeAt` read the builder **directly, with no materialization**. `stringWork` (build + `.length` + `.charCodeAt`) therefore runs end-to-end on the builder with zero `ToString()`.

## Phase 1 — append + length + charCodeAt (covers the benchmark, fully correct, no materialization)

### 1. `Compilation/StringAccumulatorPromotionAnalyzer.cs` (new, mirror `ArrayLocalPromotionAnalyzer`)

A candidate `s` qualifies iff ALL hold:
1. declared `let`/`const` exactly once in the whole program (`DeclCount == 1`), statically `string`, with a **string-literal** initializer (`""` or any `"…"`);
2. not captured by any closure (`closures.IsVariableCaptured(name) == false`);
3. every use is one of the permitted shapes below — anything else disqualifies (the `VisitVariable` catch-all).

Permitted shapes (each consumes the receiver/target `s` without visiting it as a bare variable):
- **append**: `s = s + E` (`VisitAssign` where `Value` is `Binary(+, Variable(s), E)`) **or** `s += E` (`VisitCompoundAssign`, `+=`), where `E` is **statically `string`** and does **not** reference `s`. Visit `E` only.
  - Restricting `E` to statically-`string` sidesteps the `StringifyCoerce` string-hint-vs-default-hint divergence noted at `ILEmitter.Operators.cs:52`. Non-string appends are a Phase-2 concern (route through the exact `+` coercion).
- **`s.length`** (`VisitGet`, name `length`, non-optional).
- **`s.charCodeAt(i)`** (`VisitCall`, callee `Get(Variable(s), "charCodeAt")`, non-optional). Visit the index arg only.

Reuse verbatim from the array analyzer: `VisitAssign`/`VisitCompoundAssign` override structure, the single-decl `DeclCount` guard, the not-captured guard, the `VisitVariable` catch-all, the "permitted overrides don't recurse into the receiver" discipline.

### 2. `TypeSystem/TypeMap.cs` (mirror `_promotableArrayLocals`)

```csharp
private readonly HashSet<Token> _promotableStringAccumulators = new(ReferenceEqualityComparer.Instance);
public void MarkPromotableStringAccumulator(Token nameToken) => _promotableStringAccumulators.Add(nameToken);
public bool IsPromotableStringAccumulator(Token nameToken) => _promotableStringAccumulators.Contains(nameToken);
```

### 3. Wire into the pipeline — `Compilation/ILCompiler.cs`

Add `StringAccumulatorPromotionAnalyzer.Analyze(statements, _typeMap, _closures.Analyzer);` next to the existing array/object analyzer calls at **both** sites: `ILCompiler.cs:427/429` and `ILCompiler.cs:957/959`.

### 4. Declare the slot — `Compilation/ILEmitter.Statements.cs` `EmitVarDeclaration`

New branch alongside the array branch (~line 233), **before** the generic `CanUseUnboxedLocal` fall-through:

```csharp
if (_ctx.TypeMap != null && _ctx.TypeMap.IsPromotableStringAccumulator(v.Name))
{
    var sbType = _ctx.Types.StringBuilder; // add to TypeProvider if absent
    var sbLocal = _ctx.Locals.DeclareLocal(v.Name.Lexeme, sbType);
    // initializer is a string literal (analyzer guarantee): seed the builder
    EmitExpression(v.Initializer!);                       // pushes the seed string
    IL.Emit(OpCodes.Newobj, ctor(StringBuilder, string)); // new StringBuilder(seed)
    IL.Emit(OpCodes.Stloc, sbLocal);
    return;
}
```

### 5. Append lowering — `Compilation/ILEmitter.Operators.cs`

At the assignment / `+=` emission for a target whose slot is the promoted `StringBuilder` (check `LocalsManager` slot CLR type, same as the array fast paths), emit `sb.Append(E)` instead of `String.Concat` + store:
- `s = s + E`: load `sbLocal`, `EmitExpression(E)` (statically string ⇒ already a `string` on stack), `callvirt StringBuilder StringBuilder::Append(string)`, `pop` the returned builder.
- `s += E`: identical.

This replaces the per-iteration `Convert::ToString(object)` + `String.Concat` with one amortized-O(1) `Append`.

### 6. `length` / `charCodeAt` lowering on a promoted slot

- **`s.length`** — `Compilation/ILEmitter.Properties.cs` (where the array `.length` fast path lives): `ldloc sbLocal; callvirt get_Length; conv.r8`.
- **`s.charCodeAt(i)`** — `Compilation/ILEmitter.Calls.MethodDispatch.cs` (where the array `push` fast path lives): bounds-check `i` against `sb.Length`; in range ⇒ `ldloc sbLocal; ldloc i; conv.i4; callvirt char get_Chars(int32); conv.r8`; out of range ⇒ `ldc.r8 NaN` (JS `charCodeAt` OOB ⇒ `NaN`).

`TypeProvider.cs`: add `StringBuilder` `Type` + cached `MethodInfo`s (`Append(string)`, `get_Length`, `get_Chars`) and a `(string)` ctor, mirroring how `List<T>` members are cached. **Standalone-DLL note:** `StringBuilder` is pure BCL (`System.Text`), so this is NOT a SharpTS soft-dependency — do **not** call `RequireSharpTSRuntime` (CLAUDE.md).

## Phase 2 — materialize-on-escape (broadens to real-world `return s` / pass / index / other methods)

A string you only measure (length/charCodeAt) but never use as a string is rare in real code. Phase 2 permits escapes by materializing:
- Add a companion **cached `string?` slot** per promoted accumulator.
- Append: `sb.Append(...)`; set `cached = null`.
- Any escaping use (`return s`, pass as arg, `s[i]`, `s.<otherMethod>`, template literal, comparison, non-string append operand): `if (cached == null) cached = sb.ToString(); use cached;`.
- Total cost stays O(n) when escapes don't interleave with appends (the common build-then-use shape); if they do, it degrades gracefully toward the current behavior.

Keep Phase 2 a separate PR — Phase 1 ships the headline win and is the minimal correct slice.

## Correctness & gates (non-negotiable, per epic #856)

- **Evaluation order**: `s = s + f()` ⇒ load builder, eval `f()`, `Append` — RHS side effects preserved. Disqualify appends whose operand references `s` (`s = s + s`) in Phase 1.
- **`undefined` sentinel**: sidestepped — string-literal init + string-only appends + single decl + no reassignment means `$Undefined` can never enter the slot (this is *why* the generic string-slot typing is blocked at `ILEmitter.Statements.cs:~1324`, and why this narrow promotion is safe where that isn't).
- **Capture/escape**: not-captured guard + `VisitVariable` catch-all (verbatim from the array analyzer).
- **Tests**: new `StringAccumulatorPromotionTests.cs` mirroring `ArrayLocalPromotionTests.cs` — promotion fires on the safe shapes; does NOT fire on captured / reassigned / escaping / non-string-append / shadowed / multi-decl cases; semantic equivalence (interpreter vs compiled) on each.
- **Suites**: green on `dotnet test`, `SharpTS.Test262` (interp + compiled), `SharpTS.TypeScriptConformance` — no baseline regression. Run `--compile … --verify` on the promoted output (IL-verify the new fast paths).
- **Measurement protocol**: content-forced only (return/print a guard) — a build-only loop gets DCE'd and reports a fake sub-ms O(1) number (this is exactly how the prior "concat is O(n), StringBuilder useless" conclusion was wrong). Confirm O(n) via the 5k/10k/20k/40k scaling sweep and compare to Node.

## Out of scope (tracked separately)

- **array-methods @100k (2.79× warm)**: `arr: number[]` is NOT promoted because it's consumed by `.map()` — the array analyzer disqualifies any receiver used by a non-`push` method, so `arr` stays `$Array`-backed `object` (isinst ladder on `push`, boxed-double `List<object>` map/filter/reduce intermediates). Extending promotion to typed-list HOF receivers is a separate #860/#861 follow-on.
- **factorial-class tight loops**: per-iteration `$Runtime::CheckCancellation()` is a small constant tax on every numeric loop (~4× warm but in µs). Separate investigation.
