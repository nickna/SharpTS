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
  `aot-ratchet` job in `ci.yml`; baseline lives in
  `.github/aot-warning-baseline.txt` (2,725 distinct warnings measured at this
  tree — the assessed 2,730 minus drift) and the job fails on *any* deviation:
  above means a regression, below means "lower the baseline in this PR", so
  fixed warnings can't become slack.
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
- **PE-Packer Phase A** (independent value; defect 1 is a live JIT bug):
  runtimeconfig version from the caller + `rollForward: latestMinor` instead
  of `Environment.Version` patch-pinning (`ManualBundler.cs:216-223`,
  `SdkBundler.cs:418-425`); AOT-aware `--bundler sdk` diagnostic; wrap the
  rewriter-constructor MLC failure in a `PEPackerException`; `TryGetValue` +
  named error at `AssemblyReferenceRewriter.Types.cs:36`; feature-switch the
  SDK bundler; suppress the two MLC false-positive warnings with
  justification; then `<IsAotCompatible>true</IsAotCompatible>` ratcheted in
  CI. **First reconcile the version skew** — the local checkout is 1.0.0 while
  SharpTS pins 1.0.3.

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

### The gate — before committing to Phase 2 (~4–5 days, throwaway branch)

Prerequisite: add `-SharpTSExe` to `Examples/test-examples.ps1`.

Answer three questions with one probe:

1. **Do `--compile` phases 2–9 (and the 12-phase module path) hide another
   month?** Apply the CA-blob encoder, the `MakeGenericType` fallback, and the
   trim flags; publish native; run `--compile` over `Examples/` **including a
   multi-module program**; run every output under JIT and diff stdout.
2. **Does a multi-assembly bundle work with a zeroed deps.json?**
   (`ManualBundler.cs:126-153` embeds exactly one assembly today.) Decides
   whether `--target exe` can ever ship SharpTS.dll alongside compiled
   output — gates the 14 soft-dep features for exe targets. ~Half a day.
3. **Does PE-Packer stay green inside SharpTS's trim closure**
   (`TrimMode=partial -p:IlcTrimMetadata=false`)? Its own probe was clean at
   defaults only.

### Phase 2 — unblock `--compile` (~2–3 weeks, only if the gate passes)

4. **CA blobs:** one `EncodeCA(ConstructorInfo, object?[])` over
   `BlobEncoder.CustomAttributeSignature`; replace the 21 verified
   `new CustomAttributeBuilder(` sites in 11 files (list in the assessment;
   `ILCompiler.Functions.cs` third site is :1364; don't miss the target-typed
   `new` at `DebuggerMetadata.cs:59`).
5. **`MakeGenericType`:** redirect the 60 bypass sites through
   `TypeProvider.MakeGenericType`, add the
   `catch (PlatformNotSupportedException)` →
   `TypeBuilderInstantiation.MakeGenericType` fallback there, plus a
   `TrimmerRootDescriptor` for `TypeBuilderInstantiation` as insurance.
6. **Build config:** `-p:TrimMode=partial -p:IlcTrimMetadata=false
   -p:IlcGenerateCompleteTypeMetadata=true` (mandatory — TypeProvider's name
   lookups return null otherwise), plus `[DynamicDependency]` keep-alives for
   `AsyncTaskMethodBuilder<>` (`TypeProvider.cs:242-251`), `MethodInvoker`
   (:141), `ManualResetValueTaskSourceCore<>` (:265-274).
7. **Grind the remaining phases.** This is where the schedule slack goes.

**Exit criterion:** native exe compiles the `Examples/` corpus; every output
DLL loads and runs under JIT with correct stdout.

### Phase 3 — distribution (~1–2 weeks)

**Ordering constraint:** PE-Packer Phase B's `ReferenceAction` policy must land
**before** item 8 below. `AssemblyReferenceRewriter.Assembly.cs:121` hardcodes
dropping the `SharpTS` AssemblyRef, and a program that genuinely references
SharpTS types currently dies with a bare `KeyNotFoundException` — embedding
SharpTS.dll makes that latent bug live.

8. **Embed SharpTS.dll** as a resource; rewrite `Program.cs:838`
   (`CopySharpTSRuntimeIfNeeded`) to extract to `AppContext.BaseDirectory`.
   Coherent: compiled output already requires a runtime, so the target machine
   has .NET. Restores the 14 soft-dep features. Pass non-null `onMissing` to
   `EmitReflectionCall` (`RuntimeEmitter.ReflectionHelpers.cs:79`) so failures
   are messages, not NREs. Keep `CopySharpTSRuntimeIfNeeded`. Drop
   `child_process.fork` from the native SKU.
9. **PE-Packer Phases B/C** (its repo): `BundleRequest`,
   `IReferenceAssemblyIndex` (directory + embedded CoreLib-surface index),
   `ReferenceAction` policy, embedded per-RID apphost templates (~700 KB for
   six RIDs, opt-out property), drop the `System.Reflection.MetadataLoadContext`
   9.0.0 dependency (version-mismatched on net10.0; replaceable with ~40–60
   lines of `MetadataReader`). Built-in bundler stays Windows/Linux-only until
   Mach-O + ad-hoc signing exist.
10. **Release matrix:** 6 RIDs (win-x64/arm64, linux-x64/arm64, osx-x64/arm64);
    budget ~268 billable min/tag on a private repo (macOS ×10). Toolchain
    gotchas: `vswhere.exe` must be on PATH for ILC's link step on Windows
    (hit 3/3 probes locally; GitHub images are fine); ILC peak RSS is an OOM
    risk on 7 GB runners.
11. **SDK payload:** `SharpTS.Sdk/Sdk/Sdk.targets:92` drops the `dotnet`
    prefix; `Sdk.props:28-29` RID-selects the compiler exe; package goes
    RID-specific.
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

## Open questions (merged, current)

| # | Unknown | Risk | Experiment |
|---|---|---|---|
| 1 | `--compile` phases 2–9 + 12-phase module path unexplored | **highest** | the gate probe |
| 2 | Multi-assembly bundle with zeroed deps.json | **highest for exe-target parity** | gate probe item 2 (~half day) |
| 3 | Only win-arm64 probed; linux/macOS untested | medium | re-run probes `-r linux-x64`, `-r osx-arm64` (~1 h each) |
| 4 | Binary size once the NodeRegistry generator re-enables default trimming | low | measure after Phase 1 item 1 |
| 5 | `preserve="all"` TrimmerRootDescriptor on CoreLib fails to link (`RhIsGCBridgeActive`, ILC 10.0.9) | medium — caps `@DotNetType` salvage | retry on GA toolchain; else per-member descriptors |
| 6 | MLC-types-into-TypeBuilder (latent JIT bug) | low for AOT (conceded) | file upstream issue |
| 7 | Native-emitted output byte-identical to JIT-emitted? | low | PE-Packer `MetadataDiffer` over a native fixture (~2 h) |
| 8 | Embedded resources under single-file (managed SKU) | medium | 30-min test before Track 0 ship |
| 9 | Full suites never ran against a native binary | medium | smoke job; manual `Examples/` run first |
| 10 | True per-callsite warning counts inside rollups | low — PE-Packer's is now measured: 13, reducible to 0 | `-p:TrimmerSingleWarn=false` build once |
