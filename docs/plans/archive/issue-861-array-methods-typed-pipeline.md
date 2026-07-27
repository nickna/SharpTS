# Plan: typed `List<double>` HOF pipeline for monomorphic number arrays (#861 follow-on / #856 child)

## STATUS: IMPLEMENTED (2026-06-20)

Done in 4 commits on `wrk/array-methods-typed-pipeline` (stacked on the per-scope analyzer fix):
1. per-function-scope candidacy in `ArrayLocalPromotionAnalyzer` (prerequisite).
2. typed `reduce` (`ArrayReduceDouble`) — proves the direct `Func<double,…>` binding + analyzer/emitter agreement.
3. typed `map` (`ArrayMapDouble`) + result-local typing (decided at emit time from the source slot; typed result only into a non-escaping promoted local).
4. typed `filter` (`ArrayFilterDouble`) — completes the pipeline.

**Result: array-methods @100k 11.2ms → 1.95ms — now BEATS Node (3.03ms, 0.64×)**, was 2.79× slower. The whole pipeline runs on `List<double>` with a direct `Func<double,…>` per stage and zero boxing/isinst (build→`ArrayPushDouble`, map→`ArrayMapDouble`, filter→`ArrayFilterDouble`, reduce→`ArrayReduceDouble`). IL-verified; green on `dotnet test` (38 `ArrayLocalPromotionTests`) except the pre-existing stale/flaky Test262 baselines.

**Deviations from the design below:** (a) no analyzer fixpoint — result-local slot type is decided at EMIT time from the source's already-declared slot (declarations emit in source order), keeping everything slot-type-keyed; (b) typed map/filter results are emitted INLINE from `EmitVarDeclaration` (only when the result lands in a promoted non-escaping local) rather than via a general dispatch hook, so a `List<double>` result never escapes into `$Array` context; (c) the typed arrow binds DIRECTLY to `Func<double,…>` (no boxed adapter); (d) only non-capturing inline arrows are typed (capturing → boxed fallback). With #856's other workloads already at/above Node, **every benchmark now meets or exceeds Node** — the epic goal.

---

## Why (original plan)

After strings (#857), **array-methods is the last benchmark workload meaningfully off Node** (everything else is at parity or we win):

| array-methods @100k (warm) | Compiled | Node | Slowdown |
|---|---|---|---|
| full pipeline | 11.2 | 4.0 | 2.79× |

Stage decomposition (mean ms @100k, content-forced) localizes the gap:

| stage | Compiled | Node | Slowdown |
|---|---|---|---|
| build (`push` loop) | 6.0 | 1.5 | 3.9× |
| `map` | 4.9 | 1.1 | 4.4× |
| `filter` | 9.4 | 1.2 | **7.9×** |
| `reduce` | 1.1 | 1.0 | ~parity ✅ |

**Root cause (confirmed from IL of `filterOnly`):** the whole pipeline is **boxed `double` in `List<object>`**. `arr` is `$Array`-backed (not promoted to `List<double>` — it's consumed by `.map`, which the `ArrayLocalPromotionAnalyzer` escape rule disqualifies). `filter` calls `ArrayFilterDirectBool(List<object>, Func<object,bool>)`: every element is unboxed inside the adapter and the kept ones re-boxed into a fresh `List<object>`. `map` is analogous. #861 already removed the callback-dispatch overhead (bool-returning adapter, no per-stage `List↔$Array` round-trip) — the **residual gap is purely per-element boxing**, so there is no surgical fix; the representation must become typed.

`reduce` is already at parity because it folds to a scalar (no result array to box).

## Goal

When a number array flows through `map`/`filter`/`reduce` with monomorphic `number`→`number` / `number`→`bool` callbacks, keep it as a concrete `List<double>` end-to-end: no per-element box/unbox, no `$Array` indirection.

## Design

Three coordinated pieces (analyzer → emitter → runtime helpers):

### 1. Promotion analyzer — permit terminal HOF consumers

Extend the typed-array-local promotion (currently `push`/`[i]`/`.length` only) so a `number[]`/`boolean[]` local may also be the **receiver of `map`/`filter`/`reduce`** and still stay `List<double>`/`List<bool>` — provided the array identity itself doesn't escape (the HOF consumes it and returns a *new* array/scalar; the original isn't returned/passed/aliased). `forEach` can join later.

- Reuse the same conservative escape discipline as `ArrayLocalPromotionAnalyzer` (permitted-use overrides that consume the receiver without recursing into it).
- **Prerequisite:** port the **per-function-scope candidacy** fix from `StringAccumulatorPromotionAnalyzer` to `ArrayLocalPromotionAnalyzer` first (see its own note) — without it, common array names (`arr`, `data`, `items`) are poisoned across bundled modules and never promote anyway.

### 2. Typed HOF result locals

`const doubled = arr.map(...)` / `const evens = doubled.filter(...)` must themselves be `List<double>` slots, not `object`. Mark the HOF-result local as promoted when (a) the receiver is a typed `List<double>`/`List<bool>` and (b) the callback's return type is statically `number`/`boolean`. This chains: `arr`(List<double>) → `doubled`(List<double>) → `evens`(List<double>) → `reduce`→`double`.

### 3. Typed runtime HOF helpers + emitter dispatch

Add typed helpers alongside the existing `ArrayFilterDirectBool` etc. (in `RuntimeEmitter.Arrays.*` / the `$Runtime` HOF surface):
- `ArrayMapDouble(List<double>, Func<double,double>) -> List<double>`
- `ArrayFilterDouble(List<double>, Func<double,bool>) -> List<double>`
- `ArrayReduceDouble(List<double>, Func<double,double,double>, double) -> double`
- (`bool` variants as needed)

Emitter (`ILEmitter.Calls.MethodDispatch.cs`, the #861 HOF path): when the receiver slot is `List<double>` and the callback is a monomorphic `double`-typed arrow, bind a `Func<double,double>`/`Func<double,bool>` (the #858/#861 de-virtualized arrow already gives a direct delegate) and call the typed helper. The callback bodies are already numeric (the #861 typed-adapter work) — here the adapter signature becomes `double`-based instead of `object`-based, eliminating the box at the call boundary and the unbox inside.

Keep the existing `object`/`$Array` helpers as the fallback for polymorphic / non-promoted arrays.

## Expected impact

Eliminating per-element boxing across build+map+filter should pull each from 4–8× toward `reduce`'s ~parity, i.e. array-methods @100k from ~11 ms toward ~5 ms (≈Node). Bounded by the inherent intermediate-array allocations (which Node also pays).

## Correctness & gates

- `$Array` semantics (holes, sparseness, identity) must never be observed for a promoted `List<double>` — same non-escaping guard as the existing array promotion. OOB and `undefined`-sentinel handling per the #857/#860 landmines (bounds-checked reads; a `double` slot can't carry `$Undefined`).
- Callback return-type monomorphism must be statically proven; a callback returning `number | undefined` etc. disqualifies (falls back to the boxed path).
- Green on `dotnet test`, `SharpTS.Test262` (interp + compiled), `SharpTS.TypeScriptConformance`; `--verify` the emitted HOF chains.
- Measure content-forced, with the stage decomposition above as before/after.

## Scope / sequencing

High value (last benchmark gap), **medium-high risk** (touches the HOF hot path + the promotion analyzer). Larger than #857. Suggested order:
1. Port per-scope candidacy to `ArrayLocalPromotionAnalyzer` (small, low-risk, independently shippable; also hardens existing bundled-program promotion).
2. Typed `map`→`reduce` (no `filter`) as the first vertical slice (proves the typed-delegate + helper plumbing on the simplest chain).
3. Add `filter` (the worst stage) and result-local chaining.
