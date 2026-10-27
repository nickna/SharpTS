# Experimental native guest projects

`--emit-native-project` exports an inspectable SDK project for NativeAOT-compiling
the generated TypeScript program itself. It is the first implementation stream of
[#1351](https://github.com/nickna/SharpTS/issues/1351). The full native guest output
epic remains open: the current emitted runtime still has unreviewed trim/AOT
diagnostics, and this exporter does not claim a supported warning-free native
program target.

The [native compiler distribution](native-aot.md) is separate. It NativeAOT-compiles
the SharpTS host; its ordinary DLL output is managed IL. The existing DLL and
bundled EXE targets retain their behavior.

## Decision record: experimental project export

Status: accepted for the first #1351 implementation stream. Export a project
through `--emit-native-project` and an explicit `--runtime`, using the supported
SDK publish pipeline. Keep the current `dll` and bundled `exe` targets unchanged;
reserve automatic native publishing and SharpTS.Sdk property integration for
later work after the generated-output warning contract is established.

Choose a zero-argument, integer-returning guest facade because the existing DLL
entry reads arguments from the process command line. This gives the SDK project
a direct callable entry without inventing an argument-injection hosting ABI.
Reject runtime bridges and third-party reference closures conservatively, and
treat all individual trim/AOT warnings as publish failures. Feature graduation
requires a warning-free corpus and execution evidence on each advertised RID.

The consequence is an inspectable, reproducible input to native publication,
with an explicit experimental boundary while emitted-runtime compatibility is
still being completed. Export success alone does not establish native support.

## Export and inspect

```sh
sharpts --compile app.ts --emit-native-project out/native-app --runtime win-x64 --verify --debug
dotnet publish out/native-app/NativeApp.csproj -c Release
```

Export requires SDK reference assemblies and selects `--ref-asm` automatically.
The RID must be explicit and match the compiler host's OS and process architecture.
The recognized RIDs are `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
`osx-x64` and `osx-arm64`. Current generated `process.platform` and `process.arch`
values reflect the compiler host, so this first profile refuses cross-RID export.
Native publish also requires an appropriate matching OS and native toolchain. See the
[supported NativeAOT publish flow](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
and [cross-compilation constraints](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile).

The directory must be new or empty. Export refuses an occupied directory, including
a previously exported project, and never recursively deletes it. Use a new
directory for regeneration; an existing exported project can be republished
directly. Exported files include:

```text
NativeApp.csproj
Program.cs
native-project.json
README.md
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
guest/Guest.dll
guest/Guest.pdb             # when --debug is selected
```

The local empty build-policy files prevent a parent repository's MSBuild or
central-package settings from changing the project. The versioned manifest records
the guest SHA-256, RID, profile, entry point and capability report. It is an artifact
record, not an incremental source-graph cache. A failed write/verification preserves
partial output and `native-project.pending` for inspection.

## First profile and diagnostics

Before writing a guest or project, export rejects emitted requirements for
`SharpTSRuntimeRequirements.RuntimeAssembly`, `FullDependencyClosure` or
`ManagedCompilerHost`. This excludes current SharpTS-runtime-dependent paths such
as eval, Proxy, Intl, vm, worker bridges and compiled fork. Diagnostics name the
recorded features and suggest ordinary managed output.

Third-party CLI `-r`, manifest and NuGet references are conservatively refused in
this first profile. Statically resolved BCL interop can use the ordinary compiler
path without external references. It must still pass NativeAOT analysis; a known
member at compile time does not make dynamic assembly loading or code generation
valid after native publication. The native compiler SKU retains its existing
closed interop universe.

| Code | Meaning |
|---|---|
| `SHARPTS401` | Emitted guest requires the SharpTS runtime/managed compiler bridge. |
| `SHARPTS402` | External reference closure is outside the first native profile. |
| `SHARPTS403` | Output directory is already occupied. |
| `SHARPTS404` | Direct export API target differs from the compiler host. |

`--msbuild-errors` exposes those stable codes. Invalid/missing RIDs and conflicting
CLI modes are usage errors. Project export requires ordinary DLL compilation and
cannot combine with `-o`, hosted output, packaging, an EXE/bundler target or a
no-output mode.
The first profile also requires the default `workstation` GC policy; non-default
`adaptive` and `throughput` policies are refused until their native configuration
is part of the export contract.

Refusal follows the emitted capability report. A constant eval that the compiler
has fully lowered to ordinary IL does not require the runtime eval bridge and
can export; an eval whose source comes from a runtime argument is refused.

Native guest runtimes omit the optional interpreter assembly-loading bridge.
Their indirect eval helper preserves non-string input identity and refuses string
input, even when the generated DLL is inspected under a managed host that has
SharpTS loaded. Dynamic aliases can still evade the early capability check;
the runtime refusal protects that boundary while source admission is completed
under [#3047](https://github.com/nickna/SharpTS/issues/3047).

## Entry, arguments and symbols

The opt-in guest assembly exposes the direct C# entry facade:

```csharp
int exitCode = SharpTS.Generated.EntryPoint.Run();
```

It calls the ordinary synthetic top-level DLL entry directly and returns
`Environment.ExitCode`. It performs no reflective lookup of `$Program`, and adds
no SharpTS runtime dependency. Program arguments come from the actual native
process command line; `process.argv.slice(2)` retains the existing Node-style
argument convention. This is a single program entry, rather than an arbitrary
argument-injection hosting ABI.

The facade follows DLL top-level behavior: defining a function named `main` does
not automatically call it. Invoke it from top-level code when using this first
stream. `process.exitCode` remains the program exit code.

NativeAOT produces platform native symbols separately by default. Guest portable
PDBs are retained when requested, but do not establish a TypeScript native
stepping/sequence-point support contract.

## Callable catalog foundation

Native-project emission adds a generated `$INativeCallable.Invoke(object[])`
contract to all 24 generated runtime carriers with the `object Invoke(object[])`
ABI: function and method wrappers, collection methods, Promise callbacks, text
decoder, metadata and stream callbacks, and crypto method wrappers. This includes
the callable Function prototype and the class-definition body's existing
call-without-`new` rejection. Private forwarding methods call their exact existing invocation
bodies. The original methods, receiver handling and function identities remain
the source of behavior. Interface dispatch also works for generic carrier
instances without enumerating open generic `Type` tokens. Ordinary managed
output does not emit this contract.

The method wrapper probes this contract and the exact `Func<object[], object>`
delegate ABI before its existing CLR reflection bridge. Covariant results use
the same CLR delegate contract; other delegate signatures keep the bridge. That
bridge is deliberately retained: a BCL-returned opaque CLR value
cannot yet be proved to belong to a finite invocation catalog. A subsequent
closed-world admission pass must either prove that provenance or reject the
specific CLR boundary before output. Guest `any`, computed and Symbol property
operations continue to use their existing semantics.

This is catalog infrastructure, not a warning-free native compatibility claim.
The strict `console.log(42)` publish measurement remains at 81 diagnostics with
the same inventory as before the callable contract. Remaining work includes the
opaque callable bridge and signatures, property/shape metadata, class/Symbol
initialization and runtime-eval reachability.

### CLR provenance boundary

External-reference absence does not prove a closed guest. Known BCL interop can
still produce an opaque CLR value. For example, this preparation-stage export
currently succeeds, and its managed facade prints `-1`:

```typescript
import { StringComparer } from 'dotnet:System.StringComparer';
const owner: any = StringComparer.ordinal;
const key: string = 'Compare';
console.log(owner[key]('a', 'b'));
```

The selected property getter is known during compilation, but `any` storage
erases the result's CLR origin before the computed member call. This is valid
guest code; rejecting all `any` or computed operations would be incorrect.
Removing its reflection fallback without an admission diagnostic would also
drop existing behavior. The current exporter therefore retains the fallback and
does not claim this preparation profile is warning-free.

A future closed profile needs producer-to-use provenance at these boundaries:

| Producer | Information available at emission | Required closure |
|---|---|---|
| CLR constructors and class values | Exact selected constructor or external type | Preserve the closed type through aliases and escapes. |
| CLR property and field reads | Selected member and declared result type | Classify primitive results directly; retain possible CLR owners for other values. |
| CLR method results, including arrays and ref/out tuples | Selected method and each result/element type | Track nested values as well as the outer guest container. |
| Managed `IMemberProvider.GetMember` bridges | Runtime bridge family | Admit only catalogued bridge results; do not infer safety from import spelling. |
| Reflection APIs and erased `Delegate` results | A declared CLR API result, possibly without the actual target type/signature | Diagnose unbounded dynamic provenance before output unless an exact target catalog is proved. |

Origins must survive assignments, parameters/returns, closures and container
storage, joining across branches. Typed direct BCL calls and primitive results
can keep their existing direct IL. A dynamic use of a proven CLR owner can
request an exact generated member/virtual-call thunk; an unbounded owner needs
a source-boundary diagnostic. Ordinary guest objects, `any`, computed names
and Symbol keys remain governed by guest semantics.

The callable interface itself does not prove this admission rule. Nor does its
single erased invocation entry replace the receiver-sensitive collection and
call/apply/bind paths. Those paths retain their existing dispatch.

### Exact source invocation plans

Known source callable producers can supply the invocation target separately
from the original `MethodInfo`. Native output adds assembly-internal constructor
and factory entries on the existing `$TSFunction`; managed output retains its
existing ABI. The original method, target object, instance/prototype cache keys,
name, length and JavaScript descriptor identity remain unchanged. Only the
invocation plan uses the supplied exact method token with `MethodInvoker.Create`.

For an erased generic function value, its declaration selects the canonical
`object` or closed constraint argument once. Calling one alias with a number
and then a string does not specialize its cache to the first call. Each producer
loads its actual declaring-owner token; there is no global lookup mapping an
open generic owner to a guessed invocation target. Methods on generic owners
require the caller's instantiated owner form.

Native factory method handles complete during runtime declaration. Per-source
generic target registrations remain owned by `FunctionGenericParameterRegistry`
through its existing body-emission completion phase. The emitted source target
and factory paths are checked with saved-IL verification, including captured
functions/arrows, defaults/rest, namespace/module aliases, generic owners,
private methods, computed methods and accessors. A Windows diagnostic native
publish executes that corpus and reports `RuntimeFeature.IsDynamicCodeSupported`
as `false`; all native warnings remain visible.

A separately published NativeAOT SharpTS compiler also exports the source-alias
corpus and callbacks created after `await`/`yield` in `Box<number>` and
`Box<string>` methods. Both exports pass saved-IL verification and managed
execution. The source-alias export additionally passes diagnostic native guest
execution. Generic-owner member mapping uses the existing
`EmitterTypeHelpers.ResolveMethod` seam.

The expanded suspension corpus has a remaining native guest blocker: its
canonical `Box<object>` owner is missing native code/metadata when
`$Runtime.RunClassDefinition(Type)` initializes the definition. The identical
failure reproduces with the pre-source-plan compiler at `83400f023`. This is a
class-definition rooting/catalog gap under #1351; successful native compiler
export does not establish strict native guest acceptance for that corpus.

This is an exact-method invocation plan, rather than a direct typed invocation
thunk. Opaque CLR and source-reflection construction still use their legacy
paths, including `$ErasedSourceInvokerTarget` and runtime `MakeGenericMethod`.
They are retained until their producer/use provenance is proved. The basic
strict console publish still fails on the unchanged 81-diagnostic inventory.

## Warning policy and remaining work

The exported project uses `PublishAot=true`, `SelfContained=true`, its explicit
RID, individual trim/AOT diagnostics and warnings as errors. It adds no broad
assembly roots or warning suppressions. Current emitted-runtime warnings therefore
fail strict native publish instead of being hidden by successful project export.

An initial Windows measurement with SDK 10.0.402, runtime/ILC 10.0.13 and the VS18
Insiders C++ toolchain found 101 diagnostics for `console.log(42)`: IL2026 (4),
IL2055 (7), IL2057 (1), IL2059 (6), IL2060 (1), IL2070 (19), IL2075 (59), IL3050 (4).
The same toolchain publishes and executes a minimal C# native control, so these
are guest-runtime compatibility findings. This inventory is a measured starting
point, not an accepted warning baseline.

Feature guards for unused class-owner generic closure reduced the basic guest
inventory to 89 diagnostics (IL2055: 0, IL3050: 1). Strict publish still fails.
Replacing reflection lookup of four known coercion helpers with direct calls
reduced the same measurement to 81 diagnostics, removing all four IL2026 and
four corresponding IL2075 diagnostics from that helper. The measured sequence
is 101, then 89, then 81; the remaining diagnostics still fail strict publish.
Separate diagnostic publishes retaining all warnings executed the ConsoleClear,
generic-symbol and core-language probes correctly; a statically resolved BCL
`RuntimeFeature.IsDynamicCodeSupported` probe returned `false` from the produced
native executable. Those measurements establish feasibility, not strict profile
acceptance.

Tracked remaining work in #1351 includes a generated-output warning ratchet and
AOT-safe dispatch, automatic native publishing with structured diagnostics,
static third-party dependency closure, caching/owned regeneration, SharpTS.Sdk
integration, native debugging validation, a warning-free feature corpus and
cross-platform release evidence. The profile can graduate only with that evidence.
