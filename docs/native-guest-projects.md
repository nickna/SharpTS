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
At the callable-contract checkpoint, strict `console.log(42)` publish retained
the same 81 diagnostics as before the contract. Remaining work includes the
opaque callable bridge and signatures, property/shape metadata, class/Symbol
initialization and source admission.

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
`Box<string>` methods. Both exports pass saved-IL verification, managed
execution and diagnostic native guest execution. Generic-owner member mapping uses the existing
`EmitterTypeHelpers.ResolveMethod` seam.

The expanded suspension corpus originally failed when
`$Runtime.RunClassDefinition(Type)` dynamically closed its canonical
`Box<object>` owner. That failure also reproduced before source invocation plans
at `83400f023`. Exact definition initialization, described below, supplies the
literal canonical owner and resolves that missing native code/metadata failure.

This is an exact-method invocation plan, rather than a direct typed invocation
thunk. Opaque CLR and source-reflection construction still use their legacy
paths, including `$ErasedSourceInvokerTarget` and runtime `MakeGenericMethod`.
They are retained until their producer/use provenance is proved. Omitting the
unavailable interpreter eval bridge reduced the basic strict console inventory
to 80 diagnostics; the source invocation plan itself removed none. The optional
numeric-rest proof below reduces it further.

### Exact class-definition initialization

Native output calls a nongeneric, assembly-internal initializer stub at each
existing class-definition evaluation site. Its literal closed type token flows
directly to `RuntimeHelpers.RunClassConstructor`. Declarations use the canonical
argument snapshot already owned by `ClassGenericParameterRegistry`; evaluated
class expressions retain their existing closed factory template. The stub
preserves the existing `TypeInitializationException` inner-exception unwrapping.

Defining a stub does not run it. Heritage evaluation, the source declaration
position and subsequent deferred computed-key registration retain their order.
The separate stub also keeps its exception region outside any surrounding
expression stack. Managed output retains its existing three-instruction
`RunClassDefinition(Type)` call and emits no native initializer stubs. Unknown
or open native owners retain that legacy path instead of guessing type arguments.

The suspended generic-owner corpus now runs under a diagnostic native publish,
including when exported by the native SharpTS compiler. Its strict diagnostic
inventory loses four `RunClassDefinition` entries: IL2055 twice, IL2059 and
IL3050, with no additions. This class-specific change did not reduce the basic
80-diagnostic console inventory at that checkpoint, which did not reach `RunClassDefinition`.
Dynamic prototype/Symbol owner initialization and other reflection paths remain
under #1351; strict native acceptance is still incomplete.

### Bounded source own-property operations

Native projects add own-property operations for a bounded set of source class
declarations. Queries observe completed field definitions and actual property
creation order, including an initializer that observes a later field or leaks its
receiver before throwing. Successful definitions, assignments, redefinitions and
deletions update the instance state; failed mutations leave it unchanged.
Generic field access uses the actual closed owner, while the captured default
prototype uses the existing semantic class owner.

The current subset excludes inheritance, per-evaluation class templates, public
computed instance fields, instance auto-accessors, parameter properties and colliding CLR
field spellings tracked in [#3048](https://github.com/nickna/SharpTS/issues/3048).
An exact receiver check prevents an unsupported subclass from inheriting
admission. Other owners retain their existing dispatch. This step removes no
reflection fallback and makes no strict native warning-reduction claim.
Normal output and class-free native output omit the optional family.
The general compiled-output fixes remain tracked in #3043 and #3044.

### Optional numeric-rest binding

Before declaring the native runtime, the compiler scans its prepared source,
including every module, for potential rest-function producers. Any rest
function keeps NumericRest4 binding enabled, even when it is ineligible
for the optimization. Source-execution names conservatively keep it as well.
The scan is independent of runtime-feature overrides, and marker attachment
checks the producer invariant.

When this scan proves no producer exists, native TSFunction constructors omit
only the optional reflective lookup of the NumericRest4 entry. The owned marker,
field and ordinary invocation fallback remain unchanged. Managed output and
standalone runtime-emitter calls retain the existing binding by default.
The fresh strict console inventory falls from 80 to 78 diagnostics: precisely
the two constructor IL2075 warnings for that lookup disappear, with no additions.
When binding is enabled, native output uses a finite compiler-owned helper. The
sole marker producer records its original method and exact entry together, after
checking their module, owner and signatures. Before finalizing the program type,
the helper emits literal method-token comparisons and direct delegate construction.
It preserves the existing unused-object-plus-four-double entry ABI with a non-null
bound target. Unknown methods return null, and captured functions skip binding;
both retain the ordinary invocation fallback. Imported producers, aliases, bind
and alias mutation controls preserve their behavior.

A separate Windows rest-producer/capability probe reduces its strict inventory
from 43 to 40 diagnostics, removing three constructor IL2075 lookup diagnostics
and adding none. A diagnostic-only publish retains all warnings and its native
executable prints `10 true false` (sum, source-function identity, and dynamic-code
availability). The basic no-producer inventory remains 39. This finite optional
optimization does not remove other reflective invocation paths or establish strict
native acceptance.

### Finite canonical source owners

Native runtime helpers can use a compiler-owned late catalog to obtain literal
canonical source-class types. Registration occurs at the actual interface
implementation declarations. Completion follows source type creation and checks
the created interface metadata and `ClassGenericParameterRegistry` snapshots.
Saved-PE tests account for every generic implementer with either a literal
mapping or an explicit legacy blocker, and also check the runtime carriers.

Static-member lookup still requires this output's owned field interface before
using the catalog. Deferred static storage retains its additional canonical
storage marker check and normalization of closed generic owners. Constructor
closing with supplied type arguments keeps its separate existing behavior.
The Symbol helper adds an owned-interface guard for this fast path; foreign
open CLR owners retain their old closure. The catalog also retains shared legacy
closure for unknown or unproved owners. For example, persisted reflection cannot
enumerate interfaces through some constructed generic base types; those owners
record a metadata blocker and keep legacy closure. No owner names substitute
for interface or canonical-argument proof.

The catalog does not run class initializers. Existing callers retain evaluation
timing, receiver and constructor identities. Ordinary managed output and default
runtime-emitter APIs retain their existing IL and emit no catalog.

On the static/Symbol class corpus, strict publish at combined checkpoint
`17ae47a8a` reports 108 diagnostics; the catalog candidate reports 65. The exact
multiset removes 58 rows and adds 15: three shared catalog legacy-closure
diagnostics, eleven member-metadata diagnostics now attributed to the resolver's
return, and one additional analysis occurrence in the retained foreign Symbol
closure. No diagnostic is suppressed and no broad metadata roots are added.
This class-specific measurement makes no basic-console reduction claim.

The native SharpTS compiler exports and IL-verifies the class corpus. A separate
diagnostic native control passes typed static reads/writes, inherited Symbol
getters/methods and constructor-alias identity. The broader corpus still loses
dynamic generic constructor static/descriptor metadata under native execution,
identically at `17ae47a8a` and with the catalog; the same exported DLL succeeds
under CoreCLR. This concrete remaining preparation issue is tracked in
[#3050](https://github.com/nickna/SharpTS/issues/3050). Strict native acceptance
remains incomplete.

## Warning policy and remaining work

The exported project uses `PublishAot=true`, `SelfContained=true`, its explicit
RID, individual trim/AOT diagnostics and warnings as errors. It adds no broad
assembly roots or warning suppressions. Current emitted-runtime warnings therefore
fail strict native publish instead of being hidden by successful project export.

With the current changes combined, fresh strict `console.log(42)` publication
reports 39 diagnostics: IL2059 (6), IL2060 (1), IL2070 (19), IL2075 (12), IL3050 (1).
The two optional numeric-rest lookups and 39 Proxy bridge diagnostics are absent
from the earlier 80-diagnostic checkpoint, with no additions. Strict publication
still fails. Separate diagnostic publishes retain all warnings and pass focused
native execution for own-property presence/order, generic storage/prototypes,
suspended callbacks and rest-function binding; a managed-compiler export of the
dynamic-code capability probe reports `false` as a native executable.

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
is 101, then 89, then 81, then 80 after the unavailable eval bridge is omitted,
then 78 after the optional numeric-rest no-producer proof; the remaining
diagnostics still fail strict publish.

Native output also omits the interpreter-backed Proxy trap bridges. It retains
their type checks and throws a guest TypeError before reading or invoking a trap
if a host supplies a Proxy at runtime. This covers callable wrappers, JSON
materialization/revivers and array classification; ordinary managed output and
general CLR reflection keep their existing behavior. On the same basic guest and
toolchain, this removes 39 diagnostics from the 80-diagnostic checkpoint with
none added, leaving 41: IL2059 (6),
IL2060 (1), IL2070 (19), IL2075 (14), IL3050 (1). Strict publish still fails.
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
