# Plan: De-virtualize non-escaping local arrows to a direct call (#858)

**Status:** implemented — closures benchmark reached ~1.02× vs Node via this
work (STATUS.md §18).

## Context

In **compiled** mode, an arrow bound to a local and then called directly is
invoked by constructing a `$TSFunction` wrapper **via reflection on every call**,
rather than calling the arrow's method directly. The canonical case is the
`closures` benchmark (`benchmarks/scripts/lib/algorithms.ts::closureWork`):

```ts
function closureWork(n: number): number {
    let sum = 0;
    for (let i = 0; i < n; i++) {
        const add = (a: number): number => a + i;   // never escapes
        sum = sum + add(i);                          // only ever called by name
    }
    return sum;
}
```

The arrow `add` never escapes — it is only called by name in the same scope —
so the wrapper + reflection + arg-packing is entirely avoidable.

### Confirmed: emitted IL (decompiled, per loop iteration)

```csharp
object obj = new $TSFunction(new <>c__DisplayClass9 { i = num2 },
    (MethodInfo)MethodBase.GetMethodFromHandle(/*Invoke token*/, typeof(<>c__DisplayClass9).TypeHandle));
object[] array = new object[1] { (object)num2 };                                    // box arg + array alloc
num = num + $Runtime.ConvertToNumber($Runtime.InvokeMethodValue(null, obj, array)); // reflective dispatch + unbox
```

Per iteration this pays: `GetMethodFromHandle` (reflection) + `$TSFunction` alloc
+ box arg + `object[]` alloc + `InvokeMethodValue` (MethodInvoker dispatch) +
`ConvertToNumber` (unbox). **Only the `<>c__DisplayClass` alloc is semantically
required** (per-iteration `let i` capture).

### Measured upside (3M iterations, .NET 10 Release, warm)

| variant | time | note |
|---|---|---|
| **current compiled** (`add(i)` reflective) | **~3050 ms** | ships today |
| C# model: reflective dispatch | ~142 ms | idealized; real path is ~20× heavier |
| C# model: **direct call** (display alloc + `dc.Invoke(i)`, no box/reflection/array) | **~9 ms** | the realistic target |
| hand-inlined floor (`i + i`, no call) | ~7.6 ms | unreachable theoretical floor |

The direct-call model is **~15× faster than even an idealized reflective path**,
and the real SharpTS reflective path carries far more overhead than the model
(3050 ms vs 142 ms), so the practical win on `closureWork` is **one-to-two orders
of magnitude**. Per project memory (`perf_dotnet10_warm_vs_cold`), reflective
arrow dispatch — not boxing or concat — is the **dominant remaining warm
overhead** in the suite, and `closureWork` is the benchmark that exercises it.

## Root causes

1. **The slow dispatch site** is `Compilation/ExpressionEmitterBase.CallHelpers.cs`
   (~1173–1179): for a callee that is a plain value (not an `Expr.Get` method
   call), the callee is boxed into a local and dispatched via
   `IL.Emit(OpCodes.Call, Ctx.Runtime!.InvokeMethodValue)` with a boxed
   `object[]` arg array. This is correct but maximally generic.

2. **No escape analysis exists.** `Compilation/ClosureAnalyzer.cs` has thorough
   *capture* analysis (which vars a closure reads from an enclosing scope) but
   **nothing tracks whether an arrow value escapes** its defining scope. So the
   compiler cannot prove `add` is only ever called by name and is free to
   de-virtualize. This is the gating prerequisite.

3. **The reflection-free binding machinery already exists but is narrowly wired.**
   `Compilation/ILEmitter.Calls.Closures.cs::TryEmitArrowAsDelegate` (line 23)
   already binds an arrow to a typed delegate without the `$TSFunction` wrapper
   (`ldftn` + delegate ctor, reusing `EmitCapturingArrowDisplayInstance` at line
   180). It is currently called **only** from the iterator-helper fast path in
   `ArrayEmitter.cs` (`.map`/`.every`/etc. callbacks). It already bails on async,
   generator, and self-referential (`af.Name != null`) arrows.

### What's already in our favor

- **Arrow methods are already typed.** In `Compilation/ILCompiler.ArrowFunctions.cs`
  the static method / display-class `Invoke` is defined with `returnType` from the
  type checker (line 186–218, recorded in `_closures.ArrowReturnTypes`) and
  `paramTypes` from `ParameterTypeResolver` (line 221–235). For
  `(a: number): number => a + i` the method is literally `Invoke(double): double`.
  A direct `callvirt Invoke` therefore passes/returns unboxed **for free** — the
  current boxing exists only to satisfy `InvokeMethodValue`'s `object[]` calling
  convention, not the method signature. This removes the "do we need a separate
  typed-dispatch path" risk I'd flagged.
- **Targets are already mapped:** `CompilationContext.Closures.cs` /
  `ILCompiler.State.cs` expose `ArrowMethods` (arrow → `MethodBuilder`),
  `DisplayClasses` (arrow → `TypeBuilder`), `ArrowReturnTypes`, and
  `ConstArrowBindings` (binding name → `ArrowFunction`, populated in
  `ILCompiler.ArrowFunctions.cs:96`).

## Changes

### 1. Escape analysis for local arrow bindings (`ClosureAnalyzer.cs`)

Add a conservative, single-pass analysis that marks a local arrow binding as
**non-escaping** iff *every* reference to the binding identifier is in **callee
position** (`f(...)`). Treat as **escaping** (and therefore leave on the existing
wrapper path) any binding that is:

- passed as a call/`new` argument,
- returned from the enclosing function,
- assigned/stored to a field, property, array element, or another variable,
- captured by a nested closure,
- used in any other expression position (template, object literal, `typeof`,
  comparison, spread, etc.),
- reassigned after its initial `const`/`let` binding, or shadowed.

Default to escaping when unsure. Scope v1 to the cleanest, highest-value shape:
a `const`-bound arrow **literal** in the same function/block scope (the
`ConstArrowBindings` shape already collected). Expose e.g.
`bool IsNonEscapingDirectCallArrow(string name, <scope key>)` plus a way to get
the bound `ArrowFunction` so the call site can resolve its method/display class.

### 2. Bind the local to the display instance, not the `$TSFunction` wrapper

When the binding is non-escaping (change 1), the `const add = <arrow>` site
should store the **display-class instance** (capturing) or nothing/static-marker
(non-capturing) into the local, instead of constructing the `$TSFunction`
wrapper. Reuse `EmitCapturingArrowDisplayInstance`
(`ILEmitter.Calls.Closures.cs:180`). The local's IL type becomes the display
`TypeBuilder` (capturing) rather than `object`.

- Non-capturing non-escaping arrows need **no** per-binding allocation at all —
  the call becomes a direct static call.
- If a binding is later found to escape, fall back to the wrapper exactly as
  today (change 1 must run before emission and gate this).

### 3. De-virtualize the call site (`ExpressionEmitterBase.CallHelpers.cs`)

In the function-value call path (~1080–1190), before the generic
`InvokeMethodValue` tail, add a fast path: when the callee is an identifier that
change 1 marked non-escaping and bound to a known arrow:

- **capturing:** `ldloc <displayLocal>; <emit each arg as its typed param>;
  callvirt <displayClass>::Invoke` → result already typed (no `ConvertToNumber`).
- **non-capturing:** `<emit args typed>; call <static arrow method>`.

Emit each argument directly into its resolved parameter type (using the same
`ParameterTypeResolver` types the method was defined with) — no boxing, no
`object[]`. Consume the typed return directly. Handle arity/optional/rest exactly
as the wrapper path does today; if the call shape doesn't match cleanly (rest
args, spread, wrong arity, `HasOwnThis`), **bail to the existing path** rather
than emitting a partial fast path.

### Explicitly out of scope (follow-ups)

- Hoisting the per-iteration `<>c__DisplayClass` allocation. It is semantically
  required for per-iteration `let i` capture and is the ~9 ms residual in the
  model; eliminating it needs separate "capture is not mutated across
  iterations" analysis. File as a follow-up, not part of #858.
- The "cache the arrow `MethodInfo` in a static field" micro-optimization floated
  in the issue. The model shows `GetMethodFromHandle` is **not** the dominant
  cost (reflective-with-it was 142 ms; the alloc + box + `object[]` +
  `InvokeMethodValue` dominate), so this partial win is marginal and not worth
  doing as a separate step — go straight for the direct call.

## Tests

- **Behavior parity** — add dual-mode (`[Theory]` + `ExecutionModes.All`) tests
  co-located with the closure/compiled-closure suites covering:
  - capturing direct-call arrow (`closureWork` shape) — value correct, both modes
    identical;
  - non-capturing direct-call arrow (`const id = (x) => x; id(5)`);
  - **escape guards must keep the wrapper path and stay correct**: arrow passed as
    an argument, returned, stored to a field/array/object, captured by a nested
    closure, reassigned, shadowed — each must still produce correct results
    (these are the regression-risk cases);
  - arrow with default/optional params and with a rest param (must bail safely);
  - recursion via a self-named arrow (`af.Name != null` → wrapper path);
  - async / generator arrows (→ wrapper path).
- **IL acceptance** — assert the `closureWork` capturing site emits **no**
  `GetMethodFromHandle` / `$TSFunction::.ctor` / `newarr Object` /
  `InvokeMethodValue` per call (e.g. decompile with `ilspycmd` or assert via the
  existing IL-inspection test utilities), and that `--compile … --verify` passes.

## Verification

1. `dotnet build` then targeted tests: `dotnet test --filter
   "FullyQualifiedName~Closure"` (interpreter + compiled closure suites — the
   high-risk area).
2. Full suite `dotnet test` for regressions.
3. Conformance: run `SharpTS.Test262` (interpreter + compiled) and
   `SharpTS.TypeScriptConformance`. **Per project memory
   (`test262_baseline_stale_flaky`), the Test262 harness is flaky and its
   committed baseline drifts — do NOT trust a single-run diff; re-run and verify
   the closure/capture cases directly.**
4. Benchmark delta: `benchmarks/scripts/closures.ts` via the compiler, comparing
   against `main`. Expect `closures@100k` to drop by ~1–2 orders of magnitude.
   Force content output (the bench harness's guard accumulation already does
   this) so the loop is not DCE'd into a fake sub-ms number.

## Risks

- **Correctness over a hot closure path (medium-high).** A wrongly-classified
  "non-escaping" arrow that actually escapes would call a freed/incorrect display
  instance or skip wrapper semantics. Mitigation: escape analysis is
  conservative (default to escaping); the call site bails to the wrapper on any
  shape mismatch; the escape-guard test matrix above is mandatory.
- **Interaction with existing display-class plumbing** (entry-point / function /
  arrow-scope DC reference fields, async propagation). v1 deliberately targets
  only the simple `const`-bound literal shape and bails otherwise.

## Critical files

- `Compilation/ClosureAnalyzer.cs` — new non-escaping-arrow analysis.
- `Compilation/ExpressionEmitterBase.CallHelpers.cs` (~1080–1190) — call-site
  fast path; current `InvokeMethodValue` tail at 1173–1179.
- `Compilation/ILEmitter.Calls.Closures.cs` — `TryEmitArrowAsDelegate` (23) and
  `EmitCapturingArrowDisplayInstance` (180), reused for instance binding.
- `Compilation/ILCompiler.ArrowFunctions.cs` — arrow method definition / typed
  `returnType` + `paramTypes` (186–235); `ConstArrowBindings` population (96).
- `Compilation/CompilationContext.Closures.cs` / `ILCompiler.State.cs` —
  `ArrowMethods`, `DisplayClasses`, `ArrowReturnTypes`, `ConstArrowBindings`.
- `benchmarks/scripts/lib/algorithms.ts::closureWork` — the driving benchmark.
- Closure / compiled-closure test suites — dual-mode parity + escape guards.
