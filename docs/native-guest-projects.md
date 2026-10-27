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
contract to the ordinary function, bound-function, call/apply/bind and method
wrapper carriers. Private forwarding methods call their exact existing invocation
bodies. The original methods, receiver handling and function identities remain
the source of behavior. Interface dispatch also works for generic carrier
instances without enumerating open generic `Type` tokens. Ordinary managed
output does not emit this contract.

The method wrapper probes this contract before its existing CLR reflection
bridge. That bridge is deliberately retained: a BCL-returned opaque CLR value
cannot yet be proved to belong to a finite invocation catalog. A subsequent
closed-world admission pass must either prove that provenance or reject the
specific CLR boundary before output. Guest `any`, computed and Symbol property
operations continue to use their existing semantics.

This is catalog infrastructure, not a warning-free native compatibility claim.
The strict `console.log(42)` publish measurement remains at 81 diagnostics with
the same inventory as before the callable contract. Remaining work includes the
opaque callable bridge and signatures, property/shape metadata, class/Symbol
initialization and runtime-eval reachability.

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
