# Sparse + hole-aware arrays in compile mode — implementation plan

**Issue:** #73 Stage E.2
**Status:** **COMPLETE** (2026-04-23). All six milestones M1–M6 landed. 10,275/10,275 unit tests pass. Compiled Test262 baseline: 2,181 Pass (interpreter: 850 Pass). Array folder re-enabled in compile mode, sparse/hole-aware dispatch end-to-end.
**Prerequisite:** Stages A–D merged; Stage E.1 (interpreter uint32 widening) merged.

## Goal

The compiled emitter must produce arrays with the same observable semantics as the interpreter:

1. **Sparse storage.** `a[2**31] = 1` writes in O(1), does not OOM, does not throw.
2. **Hole sentinel.** `let a = []; a[5] = "x";` creates real holes at positions 0–4. `forEach` skips them, `map` preserves them, `Object.keys(a)` returns `["5"]`, `JSON.stringify(a)` renders the holes as `null`, etc.
3. **ECMA-262 uint32 range.** `a.length` can hold values up to `2^32 - 1`. `a[4294967294] = 1` succeeds and sets `a.length === 4294967295`.
4. **Pure IL, no reflection.** No calls through `Type.GetType("..., SharpTS")` or `Activator.CreateInstance(typeOfSharpTSArray, ...)`. The emitted DLL must still run standalone without `SharpTS.dll` present.
5. **No test regressions.** All 10,275 `SharpTS.Tests` stay green. Compiled Test262 baseline gains Array-folder coverage matching the interpreter's (currently 337 passes + the 4 passed by Stage E.1 = 341).

## Non-goals

- Restructuring how arrays are typed at the AST / type-checker layer (`TypeInfo.Array` is independent of runtime representation).
- Replacing the typed-array fast paths (`List<int>`, `List<double>`, etc. — those are opt-in for monomorphic arrays and stay as-is).
- Perf parity with V8. We accept O(Length) materialization for shift-class operations on sparse arrays, matching the interpreter's behavior.

## Current compile-mode state (the starting point)

Two array representations coexist:

| Path | Backing type | Source |
|---|---|---|
| Array literals `[1, 2, 3]` | `List<object?>` | `ILEmitter.Properties.Literals.cs:42` calls `$Runtime.CreateArray` |
| `new Array(N)` | `List<object?>` | `$Runtime.ArrayConstructor` in `RuntimeEmitter.ArrayFrom.cs` |
| `Array.from(iter)` / `Array.of(...)` | `List<object?>` | `$Runtime.ArrayFrom` / `.ArrayOf` |
| DNS/crypto/GroupBy result arrays | `$Array` | `runtime.TSArrayCtor` (~11 emitter call sites) |

The emitted `$Array` class (`RuntimeEmitter.TSArray.cs`) already exists but is a thin wrapper:

```
$Array {
  List<object?> _elements
  bool          _isFrozen
  bool          _isSealed

  $Array(List<object?>)
  List<object?> Elements { get; }
  bool          IsFrozen { get; }
  bool          IsSealed { get; }
  void          Freeze()
  void          Seal()
  object?       Get(int)
  void          Set(int, object?)
  void          SetStrict(int, object?, bool)
  string        ToString()
  // Plus IList<object?> implementation for Array.isArray() dispatch.
}
```

Runtime dispatch (`$Runtime.GetLength`, `.GetElement`, `.SetIndex`, etc.) checks `$Array` first via `Isinst`, then unwraps via the `Elements` getter. Most built-in array methods operate on the unwrapped `List<object?>`.

Surface area impact (files that would be touched):

- `Compilation/RuntimeEmitter.TSArray.cs` — the `$Array` class itself (300 lines → grows ~3×).
- `Compilation/RuntimeEmitter.Arrays.cs` — core helpers: `CreateArray`, `GetLength`, `GetElement`, `SetIndex` (~1100 lines).
- `Compilation/RuntimeEmitter.Arrays.Mutators.cs` — push/pop/shift/unshift/slice/splice/reverse/sort/fill/copyWithin/with/etc. (~2200 lines).
- `Compilation/RuntimeEmitter.Arrays.Iterators.cs` — forEach/map/filter/reduce/every/some/find*/flat/flatMap (~900 lines).
- `Compilation/RuntimeEmitter.Arrays.Search.cs` — indexOf/lastIndexOf/includes (~230 lines).
- `Compilation/RuntimeEmitter.ArrayFrom.cs` — Array.from/Array.of/ArrayConstructor (~290 lines).
- `Compilation/Emitters/ArrayEmitter.cs` — fast-path method dispatch (length getter, at, with, fill, copyWithin).
- `Compilation/ILEmitter.Properties.Literals.cs` — array literal emission (two spots: non-spread, spread/concat).
- `Compilation/RuntimeEmitter.GroupBy.cs`, `.CryptoHelpers.Hashing.cs`, `.Dns.cs`, `.Functions.cs`, `.UtilHelpers.cs`, etc. — all places that build arrays via `TSArrayCtor`.
- `Compilation/RuntimeTypes.Arrays.cs`, `.Objects.cs` — host-side helpers (these are in `SharpTS.Compilation` namespace but called from interpreter/tests; they need to understand `$Array` too for interop).

~20 files, ~5,000 lines of IL emission currently, somewhere between 2,000 and 3,000 new lines added/modified.

## Design

### Step 1 — Promote `$Array` to the primary array type

`$Array` becomes the canonical runtime representation for a JS array in compile mode. All paths that previously produced `List<object?>` produce `$Array` instead. The bare `List<object?>` type is still used internally by `$Array` (as its dense backing) and by typed-array fast paths, but never crosses the array API boundary.

Rationale: centralizes all array semantics in one class with owned methods, rather than scattering dispatch logic across every built-in emitter. Every hole/sparse check lives in `$Array`'s methods; callers just invoke them.

### Step 2 — Grow `$Array`'s fields

```
$Array {
  List<object?>           _dense          // dense prefix [0, _dense.Count)
  Dictionary<uint,object> _sparse         // sparse tail, null until first sparse write
  long                    _length         // full JS array length, independent of _dense.Count
  bool                    _isFrozen
  bool                    _isSealed
  // _isExtensible deferred — current $Array doesn't expose it either
}
```

### Step 3 — Emit `$ArrayHole` singleton

Mirror the interpreter's `ArrayHole` class:

```
$ArrayHole {
  static $ArrayHole Instance
  private ctor()
  override ToString() => "undefined"
}
```

Added to `EmittedRuntime` alongside `$Undefined`. Referenced by `Ldsfld` in the emitted runtime, never allocated outside the singleton.

### Step 4 — New methods on `$Array` (full list)

Mirror the interpreter's `SharpTSArray` public API. Each is a chunk of IL to emit.

**Length / identity:**
- `int Length { get; }` — clamped at `int.MaxValue` (for source-compat with `IReadOnlyCollection.Count`).
- `long LongLength { get; }` — the true length; used by the JS `length` property accessor.
- `int Count { get; }` — IList/IReadOnlyList compatibility, delegates to `Length`.

**Indexed access:**
- `object? GetRaw(long)` — returns `$ArrayHole.Instance` for holes; `$Undefined.Instance` for OOB.
- `object? Get(long)` — user-facing, converts holes to undefined.
- `bool HasIndex(long)` — HasProperty for numeric indices.
- `void Set(long, object?)` — JS-semantic write; extends length, transitions to sparse past `SparseThreshold` slots.
- `void SetStrict(long, object?, bool)` — strict-mode variant.
- `void DeleteAt(long)` — creates a hole.
- `void SetLength(long)` — truncate-or-extend for `a.length = N`.
- `this[long] { get; set; }` — indexer.

**Enumeration (hole-aware):**
- `IEnumerator<object?> GetEnumerator()` — yields undefined for holes (user-facing semantics, matches spread / for-of).
- Internal-use methods on the built-ins (see step 5) iterate with `HasIndex`.

**Mutation helpers (delegating but length-aware):**
- `void Add(object?)`, `void AddRange(IEnumerable<object?>)`, `void AddFirst(object?)`
- `void Insert(int, object?)`, `void InsertRange(int, IEnumerable<object?>)`
- `object? RemoveLast()`, `object? RemoveFirst()`, `void RemoveAt(int)`, `void RemoveRange(int, int)`
- `void Clear()`, `void ReverseInPlace()`
- `List<object?> GetRange(int, int)` — preserves holes by including `$ArrayHole.Instance` entries.

**Frozen/sealed predicates** (existing — keep):
- `bool IsFrozen { get; }`, `bool IsSealed { get; }`, `void Freeze()`, `void Seal()`.

**Private helpers:**
- `MaterializeDense()` — flattens sparse tail into `_dense`. Shift-class ops call this first.
- `TryCollapseSparse()` — drops empty sparse dict after length-reducing ops.

### Step 5 — Hole-aware built-in emitters

Every Array.prototype method emitter in `RuntimeEmitter.Arrays.*` gets rewritten to respect hole semantics. Per the interpreter's Stage C audit:

| Method | Behavior |
|---|---|
| forEach, filter, reduce, reduceRight, every, some, indexOf, flat, flatMap, map(callback) | Skip holes (HasIndex check before callback invoke / comparison) |
| map (output), concat, slice, reverse (in-place), splice's deleted portion | Preserve holes |
| find, findIndex, findLast, findLastIndex, includes, join, toReversed, with, toSpliced | Don't skip; fill with undefined on read |
| fill, copyWithin | Fill holes; copyWithin deletes target when source is hole |
| keys, values, entries | Iterate 0..length, yield undefined for holes |
| at | Return undefined for OOB |

Each emitter gains a `$Runtime.ArrayHasIndex(arr, i)` call in its inner loop. Some gain an `IL.Emit(Ldsfld, ArrayHoleInstance)` + `Stelem_Ref` for hole propagation.

### Step 6 — Runtime helper dispatch

`$Runtime.GetLength / GetElement / SetIndex / HasArrayIndex / DeleteArrayIndex` become `$Array`-first and strip the old `List<object?>` branches (except where typed-array fast paths still return raw typed lists). `List<object?>` stays legal input for compat but no longer a target for JS array operations.

### Step 7 — IL emitter updates

`ILEmitter.Properties.Literals.cs::EmitArrayLiteral`:
- Non-spread: emit `Ldc_I4 count; Newarr object; <fill>; Call CreateArray` (returns `$Array`). Unchanged signature of the IL call; `CreateArray` changes its return type.
- Spread: `ConcatArrays` returns `$Array`.

`$Runtime.CreateArray`:
```il
ldarg.0                  // object[] args
newobj List<object?>::.ctor(IEnumerable<object>)
newobj $Array::.ctor(List<object?>)
ret
```

`$Runtime.ArrayConstructor(args)`:
- `Array()` / `Array()` with no numeric arg → `new $Array(new List<object?>())`.
- `Array(N)` with numeric N → `new $Array(empty list)`; call `SetLength(N)`; returns $Array with N holes (sparse if N > SparseThreshold). No eager allocation — removes the 1M guard.
- `Array(x)` non-numeric → single-element `$Array([x])`.
- `Array(a, b, c, …)` → `$Array` from args list.

`$Runtime.SetIndex` for `$Array`:
- Handle length assignment (`a.length = N`) → `$Array.SetLength(N)`.
- Numeric index → `$Array.Set(long index, value)`.
- Other keys → fall through to named-property path (already exists, unchanged).

### Step 8 — `for-in` / `in` operator on arrays

Both currently dispatch via `$Runtime.GetKeys` / `$Runtime.HasProperty`. Update these to call `$Array.HasIndex` for numeric keys and iterate 0..length skipping holes.

### Step 9 — `JSON.stringify`, `Object.keys/values/entries` on arrays

Already handled correctly by the interpreter (stage C). Port the same logic into the compile-mode emitters in `RuntimeEmitter.Json.*` and `RuntimeEmitter.Objects.Properties.cs`.

### Step 10 — Remove compile-mode guards

- `$Runtime.ArrayConstructor`'s 1M RangeError guard (added in Stage D) comes out: sparse storage handles large N natively.
- `SharpTS.Test262/config/subset.json::compiledExcludeFolders` clears `test/built-ins/Array`.

## Milestones / merge cadence

The work lands in small, independently green PRs. Tests must pass after each.

**M1: Infrastructure, zero behavior change**

- Emit `$ArrayHole` class + static Instance field. Reference from `EmittedRuntime`.
- Grow `$Array` fields (`_dense` renames from `_elements`, add `_sparse: Dictionary<uint,object?>?`, add `_length: long`).
- Emit new `$Array` methods: `LongLength`, `HasIndex(long)`, `GetRaw(long)`, `Get(long)`, `Set(long, object?)`, `SetLength(long)`, `DeleteAt(long)`, `MaterializeDense`, `TryCollapseSparse`. Update `Get(int)`/`Set(int, object?)` to widen.
- Old methods (`Elements` getter, `IsFrozen`, etc.) keep working; `_elements` renamed to `_dense` but exposed under the same property.
- **Acceptance:** full test suite green; no callers see any new behavior.

**M2: Unify array creation**

- Change `CreateArray`, `ArrayConstructor`, `ArrayFrom`, `ArrayOf`, `ConcatArrays` return types to the emitted `$Array` type. Every call site that was expecting `List<object?>` needs an `Elements` getter call or (preferred) update to use `$Array` methods directly.
- Remove the 1M guard from `ArrayConstructor` — route through `$Array.SetLength`.
- Array literal emission in `ILEmitter.Properties.Literals.cs` produces `$Array`.
- **Acceptance:** full test suite green; manual spot-check: `let a = new Array(10_000_000); console.log(a.length);` no longer OOMs under compile.

**M3: Index ops + length setter**

- `$Runtime.GetElement` dispatches on `$Array` using `Get(long)`.
- `$Runtime.SetIndex` dispatches on `$Array` using `Set(long)`; special-cases `length` key → `SetLength`.
- `$Runtime.GetLength` returns `$Array.Length`.
- `$Runtime.HasArrayIndex` = `$Array.HasIndex`.
- `$Runtime.DeleteArrayIndex` = `$Array.DeleteAt`.
- **Acceptance:** `a[2147483648] = 1; a.length === 2147483649` works in compile. `a.length = 5` truncates.

**M4: Hole-aware iteration primitives**

- Per-method rewrites in `RuntimeEmitter.Arrays.Iterators.cs` (forEach, map, filter, reduce, reduceRight, every, some, find*, flat, flatMap). Skip or preserve holes per the Stage C audit table.
- `RuntimeEmitter.Arrays.Search.cs`: indexOf/lastIndexOf skip; includes doesn't.
- **Acceptance:** Compiled Test262 Array baseline opens — pass count grows toward interpreter's 341.

**M5: Mutators + ancillary dispatch**

- `RuntimeEmitter.Arrays.Mutators.cs`: reverse / splice / concat / slice / copyWithin / fill / sort update per the Stage C audit.
- `for-in`, `in` operator, `JSON.stringify`, `Object.keys/values/entries` on arrays.
- **Acceptance:** Compiled Test262 baseline pass count matches interpreter's within noise; no hard regressions.

**M6: Cleanup**

- Remove `compiledExcludeFolders` from `subset.json`.
- Regenerate compiled baseline.
- Update `project_sparse_array_73.md` memory note, mark Stage E complete.
- Delete the Stage D-era 1M guard in `RuntimeEmitter.ArrayFrom.cs`.
- Delete any now-dead `List<object?>` compatibility branches.

## Risks

1. **Emitted `$Array` IL is verifiable.** This is a class-emission codebase we already have — the risk is coverage of edge cases (generic method bindings, value-type unboxing on hole sentinel, stack-type tracker). Mitigation: M1 landing as a no-op behavior change means any peverify failure surfaces without a spec regression to chase.

2. **Interop with typed-array fast paths.** `List<int>`/`List<double>` are produced for monomorphic arrays (`[1, 2, 3]` with static type `number[]`). These intentionally bypass `$Array` for perf. Decision: keep them as-is; only boxed `List<object?>` arrays get promoted to `$Array`. Result: monomorphic typed arrays don't get hole semantics (they can't have holes by construction anyway; the type implies no undefined slots).

3. **Perf regression on small dense arrays.** Each indexed access now goes through a method call instead of a direct `List<object?>` indexer. Mitigation: the `Get(long)` implementation checks `_sparse == null && index < _dense.Count` as its first branch and falls through to a direct `List<object?>` index — two CPU instructions of overhead vs. the current direct access. JIT should inline cleanly. Benchmark in M3.

4. **`List<object?>` escape through interop.** Some emitter paths (DNS results, crypto hashing, GroupBy output) currently build raw `List<object?>` and wrap in `$Array` via `TSArrayCtor`. Those keep working because `$Array`'s ctor still accepts `List<object?>`. No change needed for M1/M2.

5. **Reflection-based LINQ iteration.** Some emitters iterate the legacy `Elements` getter (returns `List<object?>`). With `_dense` now the internal backing, `Elements` should return... what? Options:
    - Return `_dense` (only the contiguous prefix; caller sees nothing past it). Breaks sparse-aware iteration.
    - Return a materialized snapshot. Allocation per call.
    - Mark `Elements` `[Obsolete]` and migrate all callers to a new `Enumerate()` method.
   Decision: snapshot for M1–M2 (safe, ~free for small arrays), migrate callers in M5.

## Effort estimate

- M1: ~4–6 hours (class restructure + new methods, no behavior change, test green).
- M2: ~3–5 hours (creation path unification, spot-verification).
- M3: ~3–4 hours (runtime helper dispatch + length setter).
- M4: ~6–8 hours (per-method hole semantics in IL).
- M5: ~4–6 hours (mutators + ancillary).
- M6: ~1–2 hours (cleanup, baseline regen).

Total: ~3 working days of focused work. Each milestone is a standalone mergeable PR.

## Files touched (estimated)

| Kind | Count | Lines |
|---|---|---|
| Growing IL emission code | ~6 | +1,500 |
| Rewriting array built-in emitters | ~4 | ±2,000 |
| Small dispatch updates | ~10 | ±300 |
| Tests (runtime-type sync allowlist, standalone DLL test) | 2 | +50 |
| Baselines (Test262 compiled) | 1 | +3,100 |
