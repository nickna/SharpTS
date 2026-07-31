# Native AOT plan — two SKUs

_Consolidated 2026-07-29 from two assessments: `native-aot-variant.md` (SharpTS
feasibility, Windows ARM64 probes on SDK 10.0.400-preview / ILCompiler 10.0.9)
and PE-Packer's `AOT_SUPPORT_PLAN.md`. Every load-bearing claim in both was
re-verified against the SharpTS tree at `1835677f` and PE-Packer `master`
(`7da4b45`); corrections found during verification are folded in below and
listed in "Corrections to the source assessments"._

## Decision

**Two SKUs.**

- **Managed SKU** — self-contained single-file per RID. Full feature set:
  `-r foo.dll`, sharpts.json `references`, `@DotNetType`, `dotnet:`,
  `--verify`, `--gen-decl`, LSP, fork. This is the default and the answer to
  "no .NET installed" for anyone using interop.
- **Native SKU** — Native AOT, for straight TypeScript. ~18 ms startup (vs
  ~40–80 ms self-contained), no extraction cache, no IL on disk. Frozen type
  universe; losses listed below, each gated behind a clear
  "not available in the native build — use the managed build" error.

The compile backend was never the problem: `PersistedAssemblyBuilder` runs
fully under AOT (verified path: `Compilation/ILCompiler.cs:1469-1490` —
`GenerateMetadata(out, out)` + `ManagedPEBuilder.Serialize`). The real costs
are the interpreter's `Expression.Compile` tax (fixable, worth fixing on JIT
too), the 6-RID release burden, and the two-SKU support matrix.

## Interop model

".NET interop" is three different surfaces; AOT treats each differently.

| Surface | Managed SKU | Native SKU |
|---|---|---|
| BCL interop in interpreted code (`@DotNetType`, `dotnet:System.*`) | full | mostly keepable: `TrimMode=partial` + `IlcTrimMetadata=false` + per-assembly `<TrimmerRootDescriptor>` (+0.74 MB measured for `System.Uri`, zero source changes). Hard edges: value-type generic instantiations fail at `MethodInfo.Invoke` (no JIT); types not compiled into the image don't exist |
| Third-party DLL interop in interpreted code (`-r`, sharpts.json `references`) | full | **permanently lost** — no IL execution engine in a native process; `Assembly.LoadFrom/LoadFile/Load(byte[])` throw PNSE, and installing a runtime on the machine never fixes it (measured) |
| Interop in compiled output | full | full **at runtime** (output is a managed DLL under CoreCLR). At compile time: BCL refs work after Phase 2; third-party refs blocked — see below |

**The precise wall for `-r` in the compile backend:** metadata reading is not
the problem. `MetadataLoadContext` works under AOT, and SharpTS already has an
MLC-based loader (`Compilation/AssemblyReferenceLoader.cs:12,43`, used by the
LSP). The wall is that the emit path cannot consume MLC types —
`Compilation/ILCompiler.cs:426-431` documents that MLC types can't be passed to
`TypeBuilder.DefineType()` for interface implementation, so compilation always
uses `TypeProvider.Runtime`, which requires the assembly loaded in-process.
This limitation exists on plain JIT today; AOT merely makes it unrecoverable.
If it is ever fixed (file upstream / investigate against the managed
`System.Reflection.Emit` implementation), third-party refs in the **compile
backend** become recoverable in the native SKU. The interpreter case never is.

## Corrections to the source assessments (verified)

Plan against these numbers, not the originals:

1. **`MakeGenericType` is ~118 production sites, not ~21+13.** 58 go through
   the chokepoint `Compilation/TypeProvider.cs:485-488`; **60 raw
   `X.MakeGenericType(...)` sites bypass it** (concentrated in
   `RuntimeEmitter.PropertyDescriptorStore.cs`, `ExpressionEmitterBase.cs`,
   `ILEmitter.Calls.Constructors.cs`, `Runtime/DotNet/DotNetTypeRegistry.cs`).
   Mechanical, but M-effort: redirect the 60 through the helper, add the AOT
   fallback (`TypeBuilderInstantiation` reflection) once, in the helper.
2. **The pipeline is 10 phases (Phase 0–9, `ILCompiler.Compile` at :532), and
   the multi-module path `CompileModules` (:1062) is a separate 12-phase
   pipeline.** All probes died in phase 1; the "unexplored phases" unknown is
   larger than the original "phases 2–8" framing.
3. **"2,111 lookups in TypeProvider"** is actually ~2,112 `_types.Get*` call
   sites across the compiler **funneled through** ~30 lookup helpers inside
   `TypeProvider.cs` (+311 `GetMethodNoParams`). Good news: the trim-flag fix
   at the chokepoint covers all callers.
4. **`--target exe` flips from "broken" to "works with caveats."** The bundler
   now lives in the `NickNa.PEPacker` package (extracted in `52ee1543`).
   Verified: `SdkBundlerDetector` catches the `Assembly.LoadFrom` failure,
   `Auto` falls through to `ManualBundler`, which works under AOT. Real
   constraints: needs an installed SDK for the apphost template until
   PE-Packer Phase C embeds per-RID templates; **macOS is a hard stop**
   (`ManualBundler` does no Mach-O adjustment / ad-hoc signing; arm64 macOS
   kills unsigned binaries) — gate to Windows/Linux with an explicit error.
5. **`Examples/test-examples.ps1` invokes `dotnet run --`, not a binary.** The
   AOT smoke job needs a `-SharpTSExe <path>` parameter added first. Its
   `-Mode` is 4-way (`all|interpreted|dll|exe`).
6. **`Packaging/` does not reference Newtonsoft.Json** (transitive only, via
   NuGet.Packaging; OmniSharp pulls it into the LSP). The payoff of replacing
   `Packaging/` is removing NuGet.Packaging/NuGet.Protocol and their closure.
7. A `JsonSerializerContext` for `TsConfigJson` already exists as a template
   (`SharpTS.Sdk.Tasks/TsConfigSourceGenerationContext.cs`) — but its header
   documents that `[JsonExtensionData]` was deliberately excluded, and
   `Configuration/TsConfigJson.Cli.cs:36,114` has two such attributes. Expect
   that friction when building the CLI contexts.
8. Minor: `JSONBuiltIns.cs` escaping sites are at :287/:464;
   `Interpreter.Eval` at `Execution/Interpreter.cs:1273`; `GenerateMetadata`
   is the 2-out overload; TextCopy is not a direct dependency; `--gen-decl`'s
   fallback is `Type.GetType` over a hardcoded assembly list, not
   `Assembly.Load` (conclusion unchanged: hard-disable in the native SKU).

## Phased plan (both repos)

### Track 0 — now, in parallel (~1 week)

- **SharpTS ratchet CI job:** `dotnet build -p:IsAotCompatible=true
  -p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true
  -p:EnableSingleFileAnalyzer=true`; fail if warnings exceed the baseline.
  Without this, every fix below can be silently regressed by an ordinary PR.
  (Exclude `.codex/`/`.claude/` worktrees from any count.) **Done:**
  `aot-ratchet` job in `ci.yml`; the structured baseline lives in
  `.github/aot-warning-baseline.json`. The first cleanup tranche lowered the
  inventory from 2,585 to 2,154 distinct warnings. The compiler-metadata seam
  then routed required framework member lookups through `TypeProvider`,
  lowering it to 858 without broad member annotations (the win-arm64 native
  image changed by only 12,800 bytes, 0.0136%). A persisted type-definition
  seam lowered it again to 697; that seam added 3,072 bytes (0.0033%). Routing
  emitter-local and field-held types through the same required lookup API
  lowered it to 382 and added 3,584 bytes (0.0038%). Fixed BCL factory and
  reflection-method metadata lookups lowered the inventory to 290. Documenting
  the already-explicit empty-location handling at single-file boundaries
  lowered it to 281. Routing the remaining required BCL, async-builder, emitted
  member and P/Invoke metadata through the same seams lowered it to 221.
  AOT-safe stack diagnostics and explicit managed-only boundaries lowered it
  to 202. Routing optional compiler metadata lookups through `TypeProvider`
  and replacing obsolete property-descriptor reflection with direct typed
  access lowered it to 167. Guarding the managed-only IL-verification API and
  documenting the persisted generic-member metadata seam lowered it to 157.
  The guard also lets Native AOT prune the verifier path: the win-arm64 image
  dropped by roughly 1 MB at that checkpoint. Routing the remaining
  compiler-owned array and emitted-base-type shapes, explicitly guarding the
  managed in-memory compiler, and replacing attribute-default activation
  lowered the inventory to 151. Isolating reflection over known
  compiler-emitted managed shapes (`$Object`, `$TSFunction`/
  `$BoundTSFunction`, `$WritableStream`, and `$MessagePort`) behind a closed,
  Native-AOT-guarded boundary then lowered it to 139. That boundary added
  37,888 bytes (0.041%) to the win-arm64 image. Extending the boundary to the
  exact `$ArrayBuffer`, `$TSDate`, and `$PromiseRejectedException` shapes plus
  the assembly-local `$IHasFields` contract, and routing recognised callable
  shapes through `RuntimeCallableDispatcher`, lowered the inventory to 116.
  The resulting image is 93,498,880 bytes: 1,536 bytes smaller than the prior
  checkpoint. Removing the disabled-by-default `ILLabelValidator` diagnostic
  and the obsolete generator private-stack reader lowered the inventory to
  102. The metadata writer remains the mandatory label validator; generator
  suspension safety is now expressed by the existing `SpillBoxed` stack-neutral
  emission contract instead of private PersistedAssemblyBuilder fields. The
  resulting image is 93,400,064 bytes: 98,816 bytes smaller than the prior
  checkpoint and 1,050,112 bytes (1.11%) smaller than the original
  2,585-warning image. Routing reflection that belongs to the managed output
  runtime through `ManagedOutputRuntimeReflection` then lowered the inventory
  to 91. The boundary rejects Native AOT before reflection but deliberately
  remains open-ended under CoreCLR, preserving user-defined and third-party
  assembly interop in the Managed SKU. Against an exact unmodified-main
  win-arm64 publish, the native image increased by 2,560 bytes (0.0027%) to
  93,402,624 bytes; 2,048 bytes of that delta is the updated managed runtime
  payload embedded in the native host. CI pins total, per-code, per-area, and
  per-file/code counts, so both increases and category swaps fail until the
  same PR updates the explained baseline.
- **Ship the managed SKU:** `dotnet publish -r <rid> --self-contained
  -p:PublishSingleFile=true`. Prerequisite (~30 min): confirm embedded
  resources (stdlib modules, `lib.*.d.ts`) load under single-file extraction.
  Extend `.github/workflows/publish.yml` (today: one RID-less ubuntu job) with
  a RID matrix publishing GitHub Release assets. **Done**, with two flag
  corrections found by probing the actual win-x64 single-file binary:
  1. `-p:IncludeAllContentForSelfExtract=true`, **not**
     `IncludeNativeLibrariesForSelfExtract` — with only native-lib extraction,
     `Assembly.Location` is empty, which breaks `--verify` (reproduced:
     "Assembly or module not found: System.Runtime" at `ILVerifier.cs:154`)
     and turns soft-dep SharpTS.dll co-location
     (`Program.cs` `CopySharpTSRuntimeIfNeeded`) into a warning-and-skip.
     Full extraction restores both (reproduced passing). This is the
     "extraction cache" the native SKU's pitch already assumed the managed
     SKU has.
  2. `-p:PlatformTarget=AnyCPU` — a RID publish otherwise infers
     `PlatformTarget=x64`/`arm64` and arch-stamps SharpTS.dll (PE32+/AMD64),
     so a co-located copy fails to load when the compiled output runs under a
     different architecture's `dotnet` (reproduced: x64 SKU under emulation on
     win-arm64 → "SharpTS runtime not present"; AnyCPU rebuild → works, even
     cross-arch). Only the apphost needs the RID.
  Embedded-resource probe passed on win-x64 (interpret + `--compile` +
  `--verify` + eval soft-dep + outputs under JIT, stdlib `node:path` import;
  ~90 MB untrimmed — open question 8 closed). `binaries` job in `publish.yml`
  publishes all 6 RIDs as `sharpts-<version>-<rid>.zip|.tar.gz` release
  assets and re-runs the full smoke against the actual published binary on
  win-x64/linux-x64 before upload. Remaining known caveat:
  `child_process.fork` from a compiled soft-dep program still shells
  `dotnet exec SharpTS.dll`, so it needs .NET installed regardless of SKU
  (Phase 1 item 3 / Phase 3 item 8 territory).
- **PE-Packer Native AOT work: done and released in 1.0.6.** The package is
  AOT-analyzer-clean and has its own native smoke job;
  `BundleRequest`, `ReferencePolicy`, `IReferenceAssemblyIndex`, the
  `MetadataReader` implementation, and the 31.8 KB embedded net10 index are
  shipped. SharpTS now uses that index when `--sdk-path` is absent and passes
  an explicit compatibility policy. Six embedded Windows/Linux apphosts make
  executable creation SDK-free, and the `SdkBundler` feature switch removes
  its reflected SDK path from SharpTS's native image. SharpTS pins 1.0.6 and
  sets the switch only for Native AOT.

### Residual analyzer inventory (91)

The cleanup tranches removed 2,494 of 2,585 warnings (96.5%) without broad
`DynamicallyAccessedMembers` annotations. What remains is no longer one
mechanical problem:

| Analyzer | Count | Primary meaning now |
|---|---:|---|
| IL2075 | 20 | Reflection from a returned/derived `Type`: dynamic .NET interop and generic managed runtime fallbacks |
| IL2070 | 49 | Reflection on parameters: external .NET interop, declaration discovery, and dynamic runtime dispatch |
| IL2026 | 1 | The dynamic .NET type registry resolving a user-supplied type name |
| IL3050 | 14 | Runtime generic/array/delegate construction where Native AOT needs a precompiled shape |
| Other flow warnings | 7 | Two each IL2055/IL2057/IL2060 and one IL2072, concentrated in dynamic interop/type synthesis |

The remaining work is split at four ownership boundaries:

1. **Managed-only feature guards (done for the current inventory).**
   In-process compiled-assembly execution, third-party executable assembly
   loading and declaration discovery now reject Native AOT at their public
   boundaries. MetadataLoadContext inspection has a narrow metadata-only
   justification. The one remaining IL2026 belongs to dynamic interop policy,
   not this bucket.
2. **Structural CLR fallbacks.** Thirteen warnings preserve Managed-SKU
   compatibility for arbitrary objects that happen to expose `Invoke`,
   `Fields`, `GetProperty`, or `SetProperty`. Keep that open-world behavior in
   the Managed SKU, but route it through one managed-only compatibility seam
   and reject it predictably in Native AOT.
3. **Dynamic .NET interop policy.** `TypeInspector`, the external-property/call
   emitters and `Runtime/DotNet` deliberately inspect arbitrary types. Broad
   member annotations were measured and rejected because they increased the
   native image by 6.55%. Define the supported native BCL surface and root only
   that surface; guard third-party and unavailable generic shapes.
4. **Generated/runtime reflection (done for the current inventory).** Known
   compiler-emitted managed shapes now go through
   `ManagedEmittedShapeReflection`. Exact-name validation covers
   `$Object`, callable, stream, message-port, array-buffer, date, and
   promise-rejection shapes. Arbitrarily named emitted user classes are
   validated by the `$IHasFields` interface defined in the same output assembly;
   its public combined `Fields` view replaces private backing-field inspection.
   The boundary's exact suppressions are restricted to a closed shape enum and
   it rejects Native AOT before reflection. Recognised callbacks share
   `RuntimeCallableDispatcher`. Reflection used by the generated managed
   runtime is separately isolated in `ManagedOutputRuntimeReflection`: that
   seam rejects the Native host but intentionally accepts arbitrary managed
   output and third-party CLR types under CoreCLR. Fallbacks that intentionally
   inspect arbitrary CLR objects outside those output-runtime helpers remain
   unsuppressed and belong to item 2.
   The optional `ILLabelValidator` duplicate was removed rather than routing
   2,126 `GetILGenerator()` acquisitions across 250 files through a diagnostic
   wrapper; `PersistedAssemblyBuilder.GenerateMetadata` still rejects every
   unmarked branch target. The generator spill reader was also removed:
   multi-operand state-machine emission already spills operands before nested
   suspension, and the focused yield/yield-star plus IL-verification suites
   enforce that source-owned invariant.

Analyzer zero is not itself the goal. The target is zero unexplained warnings:
each residual warning should end at a tested metadata seam, a feature guard, or
an explicit native interop limitation. Blanket class/file suppressions remain
out of scope.

### Phase 1 — interpreter correctness and speed (~2–3 weeks; wins on JIT too)

1. **Source-generate the `NodeRegistry` dispatch table**
   (`Parsing/Visitors/NodeRegistry.cs:385-412` `Expression.Lambda().Compile()`;
   reflective auto-registration at :344/:360 and
   `AstNodeCatalog.cs:31-40`; consumers `TypeCheckerRegistry.cs:19`,
   `InterpreterRegistry.cs:27`). Measured: 16.8 ns / 0 B per dispatch vs
   31.6 ns on today's JIT and 173.2 ns / 248 B under AOT — a strict win
   regardless of AOT. Also removes the `TrimmerRootAssembly` need and the
   47→80 MB trim inflation.
2. **JSON:** four `JsonSerializerContext`s (`TsConfigJson`, `SharpTsManifest`,
   `PackageJson`, `ProjectBuildState`); hand-rolled ECMA-262 §25.5.2.2 string
   escaper for `Runtime/BuiltIns/JSONBuiltIns.cs:287,464`; `Utf8JsonWriter`
   for `FetchBuiltIns.cs:365` / `ResponseBuiltIns.cs:79` (both serialize
   `object` graphs — worst case under trimming).
3. **Fail-fast hygiene:** add `PlatformNotSupportedException` to the catch
   filter at `References/DotNetReferences.cs:111`; switch
   `ChildProcessModuleInterpreter.cs:847-867` to `Environment.ProcessPath`
   and drop the exact-name process sniff; gate `--gen-decl` / `--verify`
   behind explicit native-build errors.

**Exit criterion:** native exe runs the `Examples/` corpus at ≥ JIT speed.

### The gate — passed

`-SharpTSExe` was added to `Examples/test-examples.ps1` in Phase 1.

Results from the win-arm64 native probe, now preserved by the
`native-aot-compile-smoke` linux-x64 CI job:

1. **Both compiler pipelines pass.** A native SharpTS host compiles and runs
   single-file input plus a two-file module graph. The focused fixture covers
   accessors, generators and async generators, producing `2 2 33`.
2. **No deps.json is required** for root-flat additional assemblies. Measured
   and recorded in PE-Packer issue #18.
3. **PE-Packer passes inside SharpTS's partial-trim closure.** Native SharpTS
   rewrites with an empty `DOTNET_ROOT`, proving the embedded reference index
   is used, and its built-in bundler produces a working executable.

### Phase 2 — unblock `--compile` (implementation complete; hardening in progress)

4. **CA blobs: done.** `CustomAttributeEncoder` writes the positional
   ECMA-335 blob consumed by the supported `SetCustomAttribute(ConstructorInfo,
   byte[])` overloads; every `CustomAttributeBuilder` emit site is converted.
5. **Generic emit seams: done.** Every emit-path `MakeGenericType` and
   `MakeGenericMethod` routes through `EmitGenerics`. Native AOT falls back to
   the targeted, rooted `TypeBuilderInstantiation` and
   `MethodBuilderInstantiation` factories; the native smoke fixture exercises
   both on every CI run.
6. **Build config:** `-p:TrimMode=partial -p:IlcTrimMetadata=false
   -p:IlcGenerateCompleteTypeMetadata=true` (mandatory — TypeProvider's name
   lookups return null otherwise), plus `[DynamicDependency]` keep-alives for
   `AsyncTaskMethodBuilder<>` (`TypeProvider.cs:242-251`), `MethodInvoker`
   (:141), `ManualResetValueTaskSourceCore<>` (:265-274).
7. **Grind the remaining phases.** This is where the schedule slack goes.

**Exit criterion:** native exe compiles the `Examples/` corpus; every output
DLL loads and runs under JIT with correct stdout.

### Phase 3 — distribution (~1–2 weeks)

**Ordering constraint satisfied:** PE-Packer 1.0.5 ships `ReferenceAction` and
SharpTS passes an explicit compatibility policy. Item 8 can choose
`RetargetCoreLibOnly` when it begins deploying a genuine SharpTS reference.

8. **Managed runtime handoff: done.** A two-stage native build first produces
   the ordinary AnyCPU `SharpTS.dll`, then passes it as
   `SharpTSManagedRuntimePayloadPath`; the Native AOT binary embeds it as
   `SharpTS.ManagedRuntime.dll`. `CopySharpTSRuntimeIfNeeded` preserves the
   managed-SKU `Assembly.Location` copy and falls back to atomic resource
   extraction for native builds. The native CI gate compiles and runs an
   `eval()` soft-dependency program under CoreCLR, proving the extracted bytes
   are usable, not merely present. Missing reflection targets now throw a named
   deployment error instead of NRE. `child_process.fork` is rejected before
   output is written in the native SKU because it needs a second compiler
   process and full managed runtime closure.
9. **PE-Packer integration: done in 1.0.6.** The package supplies `BundleRequest`,
   `IReferenceAssemblyIndex`, `ReferenceAction`, the embedded CoreLib-surface
   index, the `MetadataReader` implementation, six Windows/Linux apphosts, and
   the feature-switched SDK bundler. SharpTS sets
   `PEPacker.EnableSdkBundler=false` as a trim-time application switch. The
   permanent and release smokes create executables with `DOTNET_ROOT` empty.
   The built-in bundler stays Windows/Linux-only until Mach-O adjustment +
   ad-hoc signing exist.
10. **Release matrix: wired.** Tagged releases build six Native AOT assets
    (win-x64/arm64, linux-x64/arm64, osx-x64/arm64) on matching-architecture
    GitHub runners. Every artifact runs interpret, managed compile, and embedded
    runtime extraction smokes before upload. Windows/Linux also create and run a
    PE-Packer executable with `DOTNET_ROOT` empty; macOS asserts the built-in
    bundler's named Mach-O/signing refusal. The first tagged run remains the
    cross-platform acceptance event. ILC peak RSS on the 7 GB macOS arm64
    runner is the main operational risk.
11. **SDK payload: keep the existing managed, RID-neutral compiler.** The
    original RID-native proposal is rejected for the default SDK: invoking
    `SharpTS.Sdk` already means running `dotnet build`, its targets pass the
    project's full `ReferencePath` through `-r`, and replacing that compiler
    with the native SKU would silently lose the interop surface the SDK is
    designed to compile. It would also turn one portable package into six
    large payloads without removing a .NET prerequisite. A separate opt-in
    native SDK package can be evaluated later if startup measurements justify
    it; the standalone `sharpts-native-*` release assets remain the native
    distribution.
12. *Optional:* replace `Packaging/` with ZIP + nuspec + HTTP PUT (~300
    lines); removes the NuGet.* closure.

## What the native SKU loses (final list)

| Lost | Why | Disposition |
|---|---|---|
| `-r foo.dll` / sharpts.json `references` in the interpreter | no IL engine in a native process | by design — route to managed SKU with a clear error |
| Third-party refs in `--compile` | MLC-types-into-`TypeBuilder` limitation (`ILCompiler.cs:426-431`), pre-existing on JIT | error now; recoverable iff the upstream limitation is fixed |
| Value-type generic instantiation in BCL interop | `MethodInfo.Invoke` needs JIT for new value-type instantiations | document; reference-type instantiations work |
| `--verify` | `ILVerifier.cs:154` needs `typeof(object).Assembly.Location`; no BCL on disk | hard-disable with named error |
| `--gen-decl` | `DiscoveryGenerator.cs:68` `Assembly.LoadFrom`; by-name fallback returns truncated metadata | hard-disable with named error |
| `child_process.fork` | shells `dotnet exec SharpTS.dll`; IPC failure not root-caused | drop from native SKU |
| `--target exe` on macOS | no Mach-O/signing in ManualBundler | explicit error until implemented |
| LSP | OmniSharp not AOT-viable | stays managed forever (already a separate tool) — zero work |
| file:line stack traces | AOT | `<StackTraceSupport>true</StackTraceSupport>` |

Everything else survives: REPL (`Repl/` verified reflection-free), eval/vm
(self-hosted, `Interpreter.cs:1273`), Proxy/Intl/Symbol/BigInt/generators/
workers, MSBuild SDK (subprocess-only by design, verified), JSX.

## Testing

- `SharpTS.Tests` and `Test262` load emitted bytes in-process
  (`TestHarness.cs:357,610,959,1023`; `Test262Runner.cs:538,543`) — **they can
  never run AOT; keep them managed forever.**
- The native SKU is gated by a subprocess smoke job: publish native, drive
  `Examples/test-examples.ps1 -SharpTSExe <native>` over interpret + compile
  modes, assert compiled DLLs run correctly under JIT.
- PE-Packer adds: an `IReferenceAssemblyIndex` fixture (no filesystem),
  runtimeconfig version/rollForward assertions, and its own native smoke job.
  Note its `MetadataRoundTripTests.cs:400` passes
  `RuntimeEnvironment.GetRuntimeDirectory()`, which under AOT silently returns
  the publish dir — the one usage pattern that is actively wrong.

## Remaining questions

| # | Unknown | Status / next step |
|---|---|---|
| 1 | Cross-platform native compiler | win-arm64 passes locally and linux-x64 passes CI, including managed-payload extraction. The six-RID release matrix is wired; its first tagged run is the remaining acceptance event. |
| 2 | BCL interop preservation | Targeted roots work for the emit internals; define and test the supported `@DotNetType` surface, including the known dynamic-event edge. |
| 3 | MLC-types-into-TypeBuilder latent JIT limitation | Conceded for the native SKU; file upstream separately. |
| 4 | Native-emitted output metadata parity | Executed output and PE-Packer rewriting pass. Add a `MetadataDiffer` fixture if byte/table-level parity becomes release-blocking. |
| 5 | SDK-free `--target exe` | Closed by PE-Packer 1.0.6. Both the permanent and release smokes require bundle creation with an empty `DOTNET_ROOT`. |
