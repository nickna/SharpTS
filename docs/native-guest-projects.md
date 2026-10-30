# Experimental native guest output

`--target native-aot` publishes the generated TypeScript program through the
supported SDK/ILC pipeline. `--emit-native-project` retains the inspectable project
mode. Both are experimental streams of
[#1351](https://github.com/nickna/SharpTS/issues/1351).

The combined basic-console checkpoint `001ade061` publishes strictly with zero
trim/AOT diagnostics, passes saved-IL verification and executes `42` as a native
PE with no CLR header and no `dotnet` on PATH. Automatic CLI publication also
verifies native arguments and exit status, plus repeated unsigned-cache reuse.
The full epic remains open: a warning-free feature corpus, third-party interop,
broader native debugging and the complete platform/release matrix require separate evidence.
A [Windows x64 Debug native debugger check](debugging-typescript.md#nativeaot-guest-debugging-windows-x64)
now verifies original TypeScript breakpoints, caller frames and source-line Step Over; native
runtime frames remain visible during Step In.

The [native compiler distribution](native-aot.md) is separate. It NativeAOT-compiles
the SharpTS host; its ordinary DLL output is managed IL. The existing DLL and
bundled EXE targets retain their behavior.

## Decision record: experimental native publication and project export

Status: implemented for experimental CLI publication. Use `--target native-aot`
with an explicit matching `--runtime` and `-o` executable path. Keep inspectable
`--emit-native-project` export alongside it, using the same supported SDK publish
pipeline and feature boundary. The current `dll` and bundled `exe` targets retain
their behavior. SDK integration and the complete release matrix remain separate
acceptance streams.

The [SDK NativeAOT guide](msbuild-sdk.md#nativeaot-guest-publish) describes the
separate MSBuild publish graph and its output ownership.

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

Automatic publication uses the same experimental feature profile and entry facade:

```sh
sharpts --compile app.ts --target native-aot --runtime win-x64 -o out/app.exe --verify --debug
```

The RID and final executable path are explicit.
The executable stem cannot be `Guest` or a framework assembly name, because the
wrapper must not shadow its own dependencies.
The driver selects the SDK under
the source directory's `global.json` policy, pins it in an owned temporary project,
and invokes supported `dotnet restore` and `dotnet publish`. It forwards original
SDK/ILC diagnostic codes and text. Individual trim/AOT warnings remain errors;
automatic publication never disables the exported project's warning policy.
`SHARPTS407` identifies toolchain discovery/start failures, `SHARPTS408` identifies
restore/publish failures, and `SHARPTS409` identifies invalid artifacts or output
promotion failures. CTRL+C cancels the owned SDK process tree and returns 130.

Only a completed publish replaces the requested executable. Platform native
symbols (`.pdb`, `.dbg` or `.dSYM`) accompany it when produced by the SDK. An owned
sidecar tracks those companions so regeneration removes only its own stale
symbols. Output is staged beside the destination and prior artifacts are restored
if promotion fails. Windows native output supports the existing `--sign` options;
signing happens on a fresh staged copy on every run, including cache hits. Signing
credentials never enter the native artifact cache. Other native platforms refuse
Authenticode signing precisely.

An unsigned artifact cache records the original coherent guest PE/PDB and native
executable/symbol pairs. Its guest revision ignores only the serialized COFF
timestamp and exact metadata module-MVID slot; IL, signatures, feature metadata,
PDB bytes, project policy, RID, compiler revision, SDK/resolved package closure and
identified native tool/library inputs all participate. Every cached file's hash
is checked before reuse. The Windows toolchain inventory covers target-architecture
VC and SDK search directories; an unidentified/incomplete native toolchain still
publishes strictly but bypasses final-artifact reuse. This cache is not a source
graph cache. Temporary attempt directories are owned and cleaned separately from
user-exported projects. Guest portable PDBs do not promise TypeScript native stepping.

CLI cache identity preparation and lock acquisition have a bounded deadline. If
they cannot finish, publication continues through the same strict SDK pipeline
without reading or writing a cached artifact. Caller cancellation still stops
publication. A partial or late fingerprint cannot authorize cache reuse.

`--emit-native-project` remains a separate inspect mode and never starts publication:

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

The serialized guest's assembly references are checked before the project directory
is claimed. Framework names, versions, cultures and public-key tokens must match
the framework index or the emitted runtime's literal CoreLib/Uri implementation
identities; full public keys are compared through their canonical CLR tokens.
Implicit runtime-library dependencies are subject to the same boundary as explicit
references. For example, the current Zlib emission references `ZstdSharp`, so this
profile refuses it with `SHARPTS402` until its static dependency closure is supplied
and verified (#3083). The check runs with or without `--verify` and also protects
direct compiler serialization. Ordinary managed output retains its dependencies.

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
PDBs are retained when requested. The [Windows x64 Debug acceptance](debugging-typescript.md#nativeaot-guest-debugging-windows-x64)
verifies original entry/imported TypeScript breakpoints, caller frames and source-line
Step Over using the native executable and matching native PDB. Step In can enter
NativeAOT runtime helpers; runtime/reflection/wrapper frames remain visible. This
does not certify managed Just My Code, hoisted-local projection, Release optimization
or other platform debuggers. Retaining a portable PDB alone establishes none of
those behaviors.

## Callable catalog foundation

Native crypto hashing and random-byte generation are covered by the native guest
corpus. `X509Certificate.verify` accepts the program's own public `KeyObject`
values, such as results of `crypto.createPublicKey` or a certificate's `publicKey`.
Other CLR key carriers receive a TypeError before their export members are
inspected or called. Use managed DLL or bundled EXE output for those carriers.
The native verification controls include valid and wrong RSA/ECDSA public keys.

The implementation sections below retain historical diagnostic checkpoints.
Counts such as 81, 30 or 8 describe their individual integration stages; they are
not the current basic-console inventory, which is zero at the checkpoint above.
Feature-present probes can still expose findings that the basic program does not reach.

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

The method wrapper probes this actual contract and the exact `Func<object[], object>`
delegate ABI before any remaining CLR reflection bridge. Covariant results use
the same CLR delegate contract. This is a checked invocation capability, not proof
that an arbitrary value belongs to this compiler. Ordinary managed output and
uncertified runtime-emitter APIs keep the legacy bridge for other signatures.
Prepared native source output has the explicit operation boundary below; typed
BCL and other CLR metadata paths remain separate.

This is catalog infrastructure, not a warning-free native compatibility claim.
At the callable-contract checkpoint, strict `console.log(42)` publish retained
the same 81 diagnostics as before the contract. Remaining work includes the
opaque callable bridge and signatures, property/shape metadata, class/Symbol
initialization and source admission.

### Residual reflective callable boundary

Prepared native source output declares one assembly-internal rejection helper on
the already-declared runtime type and fills it after error metadata is available.
Scoped consumers receive its exact nullable MethodInfo and validate the module,
static object-returning signature and zero parameters. Default runtime-emitter
calls have no selected boundary, even when emitting a native callable catalog;
ordinary managed output also retains the original bodies and declaration order.

`$MethodCallable.Invoke` keeps the existing native-capability and exact
`Func<object[], object>` arms first, with their existing numeric normalization and
exception behavior. The old GetType dereference remains, including the null-wrapper
NullReferenceException. Remaining reflective Invoke/Call lookup instead throws a
guest TypeError naming that unsupported operation. No general Delegate.DynamicInvoke
bridge is added. Other delegate signatures can still use the ordinary/default
legacy wrapper; this selected native profile diagnoses their residual operation.

`InvokeMethodValue` keeps all existing receiver-aware function, wrapper, MethodBase,
collection, Proxy, exact delegate and Type handling. Only its final reflective
Call bridge is refused. It gains no general delegate or interface dispatch: a
value with only Invoke was not callable through that route, and promoting it would
change behavior. The two legacy bridges also differ in argument packing, numeric
normalization and exception unwrapping; ordinary/default controls retain those
precise differences.

A verified all-feature runtime inventory has 24 actual erased-ABI callable carriers,
all with the exact capability, and no declared Call method. The five additional
Invoke bodies are zero-argument worker/finally thunks: they enter typed delegates or
an explicitly tokenized TSFunction wrapper. The sole generated MethodCallable
construction site is GetFieldsProperty's GetMember compatibility path; its actual
emitted bound DH/ECDH carriers are already catalogued. These facts preserve known
runtime behavior, but are not a whole-program ownership certificate. In particular,
an unrelated source class with a public Call member can reach the legacy bridge;
that ordinary baseline defect remains tracked separately in
[#3052](https://github.com/nickna/SharpTS/issues/3052). The native boundary therefore
names the reflective operation rather than claiming every rejected value is foreign.

Saved-IL tests cover actual carriers, foreign Invoke/Call priority, argument identity,
null/bare values, delegate behavior, and exception wrapping in ordinary, default and
selected native output. A diagnostic native golden executes function aliases,
array and Promise callbacks and the source-class refusal. Ordinary console and
callback/class programs retain their PE sizes and every method-body hash. Strict
console publication drops from 36 to 33 diagnostics, removing exactly the three
residual IL2075 entries with none added; it still fails the production warning gate.

### CLR provenance boundary

External-reference absence does not prove a closed guest. Known BCL interop can
still produce an opaque CLR value. For example, ordinary managed output
prints `-1`, while native project preparation now refuses it with `SHARPTS405`:

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
drop existing behavior. The exporter retains the fallback and
does not claim this preparation profile is warning-free.

The first native admission slice captures original source expressions and checked
binding identities before lowering. Selected CLR constructors, type values,
properties, fields, indexers, operators and methods record their exact metadata
and emitted conversion. It permits direct typed CLR receivers/arguments and
simple immutable checked local bindings whose reads remain in that domain.
Explicitly discarded results do not escape. Primitive results and arrays that
are actually materialized entirely into safe guest values retain guest semantics.

`SHARPTS405` names unsupported CLR-origin flows before native output is written:
source returns/throws, guest heap/container writes, dynamic or `any` use, mutable
or unresolved aliases, captures and unclassified implicit consumers. A declared
`System.Object` result is refused without scalar proof; boxing an arbitrary CLR
struct does not make it a guest primitive. Value-type construction is also
refused pending the existing guest-storage boxing defect (#3051); boxed results
from proven getters can still feed typed consumers. Missing selected-source
context is negative evidence, rather than a guest-safety default.

Typed annotations and observed class-shaped values are not positive CLR-origin
proof. Every selected receiver/argument edge must have its actual emitted
conversion recorded. Reference pass-through requires a selected CLR producer or
an immutable checked alias of one; scalar-normalizing conversions and independent
copies of scalar arrays are admitted. Guest arrays cast to `IList`, including
`ArrayList.Adapter` wrappers, cannot acquire trusted origin merely by reaching a
typed consumer. Callback reference and array returns are refused because their
adapter casts or passes through the guest value rather than making an independent
copy; scalar and void callbacks remain supported. Indexer assignments that return
the original RHS keep that RHS provenance, even when the stored value was converted.

Reference stores through CLR setter/indexer syntax are currently unimplemented.
This does not prove that CLR state retains no references: admitted CLR method
arguments can be retained internally. Every later result, callback or exception
ingress still requires classification. A separate exception-effect fixed point
follows known checked source function bindings and immutable callable aliases,
including guest `any` aliases. Catch bindings receiving CLR effects are refused;
unresolved dynamic calls or implicit user-code operations are conservative
exception effects when CLR code is present. An otherwise unrelated dynamic operation in a read catch boundary can therefore
be refused when the compilation also contains CLR effects. Known pure source
calls and their checked aliases retain ordinary guest catch behavior. Callback signatures are checked using
their actual boxing/materialization convention; opaque reference, unsupported
boxed-struct and by-ref callback slots are refused. Known selected CLR effects
also flow through checked source callees in a separate fixed point. Effectful
async/generator bodies and class/accessor/initializer owners are unimplemented
in this first profile. An effectful synchronous source callable can be used only
as a known direct source callee or through immutable checked aliases whose uses
remain direct; callback arguments, returns/exports, containers and dynamic
call/apply/bind wrappers refuse independently of typed CLR argument admission.
This covers Promise executors/rejection handlers and EventEmitter capture-rejection
listeners, whose exceptional payloads do not enter through source catch syntax.
Guest-only and unrelated pure async callbacks/rejections remain guest values.
Module capture also preserves original roots and identities for deferred CommonJS
and dynamic-import-only modules. Selected or transitively known CLR effects at
those module roots refuse before output, rather than escaping when require/import
runs inside a later guest callback. The eager entry keeps its typed CLR domain;
pure deferred modules and cached CommonJS cycles remain supported. This is a
bounded initialization rule, not a blanket module/`any` restriction. Native
projects use non-hosted initialization; explicit top-level await retains its
existing compiler-context refusal, so this slice adds no redundant await gate.
Uncatalogued member-provider operations on a proven CLR origin remain unsupported. Ordinary guest objects,
callbacks, catches, `any`, computed names and Symbol keys retain guest semantics.

Static direct/indirect eval lowering reparses source outside that original AST
inventory. Every successful static lowering records an unsupported proof boundary;
when the final compilation contains any CLR origin/effect facts, `SHARPTS405`
refuses it even if eval appeared before the CLR operation. Guest-only static eval
and its known literal callable aliases retain their existing behavior. The
compiled `Function` constructor does not reparse source: its existing runtime
grammar accepts only no arguments or `return this`, and other source text keeps
its existing refusal. This slice adds no redundant Function-constructor gate.

The proof owner stays open during source emission, then freezes before native
serialization. Failed admission also guards `Save`, `SaveToBytes` and
`SaveArtifacts`; normal compilation has no admission owner. No runtime reflection
fallback is removed, and this bounded slice is not a complete closure or a
warning-free native compatibility claim. In particular, consumer conversion proof does
not certify reflection or metadata capabilities: a selected API may address guest
state by assembly/type/member identity without receiving a guest object argument. Selected `FieldInfo.SetValue`, `PropertyInfo.SetValue`,
`MethodBase`/`ConstructorInfo.Invoke`, `Type.InvokeMember` and
`MethodInfo.CreateDelegate`/`Delegate.CreateDelegate` operations therefore
refuse with `SHARPTS405` until their addressed target ownership is proved. The
rule follows actual metadata declaring-type ancestry and all overloads, rather
than guest variable names or type annotations. Pure metadata reads and ordinary
typed CLR calls remain supported. This bounded refusal does not certify other
reflection or metadata execution capabilities.

The optional property-lookup catalog replaces only the `GetFieldsProperty`
property reflection operation. It admits exact registered same-output role types,
completed source-owner declarations and explicit framework guest carriers. Rows
come from their complete public instance property inventory; receiver subclasses
do not inherit admission. Unique rows use a declaring-type-qualified getter token
and the existing reflective invocation/numeric normalization. Duplicate case-insensitive
candidate groups retain a literal, positively owned `Type.GetProperty` operation.
Owners with nonpublic accessors anywhere in their base chain, or unsupported
builder enumeration, retain that same literal-owner operation for all names.
This avoids treating builder visibility/hiding as proof of saved runtime absence;
the actual runtime binder and its exceptions remain authoritative.
A complete owner miss continues to the existing method/prototype lookup; an
unproved owner or a setter-only property produces an explicit native TypeError at
this operation. Default managed output retains its original lookup body.
Native method queries use a separate late catalog of the same exact owners and
the source catalog's proved canonical generic targets. SafeGetMethod retains
its original runtime ambiguity handling: prefer a zero-argument overload, then
the first case-insensitive name match in the loaded runtime's method order.
Each admitted query uses a literal owner and one fixed flag profile. Output-owned
types support public instance (with or without IgnoreCase), public static, and
public declared-only static lookup. Framework guest carriers support public
instance lookup only; native intrinsic constructor routes already provide their
JavaScript static members. Unsupported native CLR static queries, other flags,
foreign owners and foreign subclasses raise a guest TypeError at this operation.
This avoids retaining unrelated framework static capabilities such as dynamic
array creation. Raw object arrays and the exact runtime function carrier admit only three literal instance probes
Call, GetMember and SetMember used by the manual callable/member bridges. These
use the actual named binder query; other CLR-array method queries are refused.
Ordinary JavaScript array and function properties, descriptors and prototypes
keep their dedicated dispatch. Other CLR helper queries on the function carrier
are explicitly refused; this prevents dormant constructor helpers from becoming
reachable through unrelated metadata enumeration.
A source-owner catalog with unproved generic inheritance keeps
the entire legacy method query operation, including its visible diagnostics.
No canonical owner is invented for that case. Default managed output retains
the original query bodies; other general reflection fallbacks remain separate.
The strict Windows console inventory falls from 29 to 27 diagnostics, removing
exactly the two SafeGetMethod IL2070 findings with none added. Separate native
controls pass loaded ambiguity/overload selection, guest error identity, raw
rest-array reads, and function descriptors/prototypes/aliases. Their saved IL
verifies, and ordinary console, rest-producer and class output retain every
method body and PE size. Strict publication still fails on the other findings.
Zlib's implicit ZstdSharp dependency remains a separate exporter limitation
tracked in #3083; its managed member bridge is tested without claiming native
project acceptance.

A broader closed profile still needs producer-to-use provenance at these boundaries:

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
thunk. Uncertified output retains the legacy source-reflection paths. Prepared
native output uses the completed finite generic-target catalog and explicit
foreign-marker boundary described below. Omitting the
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
availability). At that checkpoint the basic no-producer inventory was 39. This finite optional
optimization does not remove other reflective invocation paths or establish strict
native acceptance.

### Completed method-generic certificate

Native output records both owned method-generic definition sites: ordinary source
functions and their `new.target` body clones. A compiler-owned late helper checks
the exact method and parameter snapshots against the completed
`FunctionGenericParameterRegistry` before the program type is finalized or any
output is serialized. Saved-PE tests compare every generic method token with that
inventory, including imported definitions and feature overrides.

The helper retains the original method identity for nongeneric methods, closed
generic methods and foreign methods without all three source markers. A generic
definition outside the completed owned inventory carrying this output's
`FunctionName`, `FunctionLength` and `PadUndefined` markers instead receives a
guest TypeError naming the unsupported foreign-marker boundary. These public
attributes alone do not prove that a method belongs to the compiler: a verified
foreign assembly can carry them. The refusal therefore makes that native profile
boundary explicit rather than silently dropping its previous generic closure.

For each ordinary owned definition, the completed catalog proves every legacy
eligibility condition: all three source markers recorded at their actual emission
hooks, closed owner/signature, no special generic-parameter attributes, and zero
constraints or exactly one closed constraint per parameter. Its declaration-owned
MethodSpec must use precisely `object` for an unconstrained parameter or that
single constraint. The helper compares a literal real MethodInfo to the input and
returns the corresponding literal closed MethodSpec. Caller-defined MethodInfo
equality cannot select a target. An unproved owned definition records a blocker
and retains the full legacy operation; it does not admit foreign marked methods.

Constructed calls use a separate exact entry/core catalog, with the original
source MethodInfo, cache identity, receiver/default/rest shaping and final
`__newTarget` slot preserved. Only the proved entry definition or its exact
canonical closed entry selects the corresponding literal closed core. Other
closed entries and foreign generic cores receive a guest TypeError at the former
dynamic generic-closing operation. Construct-only cores keep their unmarked
ordinary-call behavior. The finite native canonical path also avoids the open-core
failure tracked separately for ordinary managed output in [#3080](https://github.com/nickna/SharpTS/issues/3080).

Ordinary managed output and runtime-emitter calls without the optional certificate
retain their original method bodies. Saved-PE tests cover constraints, completed
metadata changes, exact definition/closed inputs, hostile equality, imports and
generic `new`, bound construction and `Reflect.construct` with defaults/rest.
Diagnostic native executions pass both the zero-producer capability control
(`42 false`) and the generic alias control (`7 text`).

The same basic strict console measurement falls from 39 to 37 diagnostics,
removing exactly the normalizer's IL2060 and IL3050 findings with none added.
This does not establish general CLR provenance or strict native acceptance.
An isolated generic constructor control against immutable `c440c0ee5` falls from
34 to 30 strict diagnostics, removing exactly IL2060/IL3050 from the late generic
normalizer and `InvokeConstructed`, with none added. Strict publication still
fails. A separate publish retaining all warnings executes the native constructor
as `7 true`; ordinary console, generic alias, constrained alias and `new.target`
output retain identical PE sizes and every method-body hash.

### Completed union-conversion inventory

Native output can also certify its completed union declaration inventory. The exact
`UnionTypeGenerator`, mapper, module and emitted `$IUnionType` interface must agree;
a type name is not ownership evidence. Both single-file and module finalization
complete the generator before selecting the late converter body or serializing output.
An incomplete, failed or foreign owner cannot certify the output. Saved-PE tests
compare actual interface implementers with the completed snapshot, including imported
producers and feature overrides.

Any compiler-owned union declaration retains the full legacy argument converter,
even if a mapped collection or return slot subsequently widens to object. Ordinary
managed output and uncertified runtime-emitter APIs retain it too. With a completed
empty inventory, null arguments, already-exact boxed values, non-value types and
value types outside the old `Union_` prefix predicate keep their old behavior.
A non-null mismatched argument for a `Union_` value-type parameter instead receives
a guest TypeError immediately before the former reflective `op_Implicit` lookup.
This is an explicit experimental native boundary: verified foreign structs can match
that name and have working implicit operators without belonging to this guest.
The compiler does not silently discard their conversion or use their names as proof.
Cached conversion flags, parameter metadata and original MethodInfo identity remain.

The strict basic-console inventory falls from 37 to 36, removing exactly the
converter's IL2075 diagnostic with no additions. Diagnostic publishes retain all
warnings; the zero-producer console executes `42` and a real union-producing array
program executes `2`. Strict native acceptance remains incomplete. Ordinary output
for both programs retains its PE size and every method-body hash against the
preserved managed compiler baseline. The producer-present strict control remains
at 37 diagnostics; its retained IL2075 warning moves from the original converter
to the late helper, with no other multiset change.

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

### Literal runtime class initialization

The native runtime's class-prototype and static Symbol lookups use a late
initializer dispatcher completed from this output's source-owner catalog.
For a fully resolved nongeneric source inventory, it compares exact Type
identities and calls `RunClassConstructor` with the matching literal type token.
The inventory includes source owners, their base chains and the emitted
intrinsic constructor identities, including closed BCL representations such as
`Task<object>` and `IList<object>`. These entries run the actual initializer at
the original lookup position; no framework type is assumed to have no effects.

An unlisted owner receives an explicit guest TypeError at the former dynamic
initializer operation. Generic or unresolved source owners retain the complete
legacy operation until their independent static-state domain is proved.
The dispatcher is absent from ordinary managed and default runtime-emitter
output. Literal registration and completion do not run initializers. Tests cover
initialization order, repeated lookup, intrinsic missing-Symbol reads, inherited
methods, module declarations and the explicit unknown-owner boundary.

The strict console inventory falls from 36 to 30, removing exactly six IL2059
diagnostics with no additions. The IL-verified diagnostic native executable
prints `42`. Ordinary console, rest-producer and generic-class outputs retain
their PE sizes and every method-body hash against the preserved managed baseline.

### Exact static-member presence

Native constructor own-property queries can use a completed exact-owner presence
table. Created output builders contribute their declared public static field,
property and method names; literal known framework owners retain their actual
field/property queries with the original binding flags. The compiler never treats
a name as owner identity. Generic, ambiguous or unresolved output metadata keeps
the complete legacy operation. Unknown owners receive an explicit guest TypeError.

Descriptor/deletion checks, declared-only inheritance and computed-method lookup
retain their ordering, including the latter's initializer effects. Literal field
handles retain the exact metadata needed by companion descriptor and deletion
operations. The compiler's
builder enumeration seam reads live metadata solely for serialization; it adds no
guest suppressions or metadata-root configuration. Normal output keeps its PE size
and every method body. Strict console diagnostics fall from 30 to 27, removing
exactly the three HasOwnPropertyHelper IL2070 records with no additions.

### Receiver-field binding

Prepared native output supplies a scoped receiver binder to `$TSFunction`.
Compiler-created lexical receiver fields use a direct setter on their actual
display-class owner, including generic owners. No function constructor, cache
key, receiver convention or function identity changes. Dictionary acquisition
keeps its existing `_expectsThis` guard and lexical-arrow exclusion.

Before returning or writing any saved image, a final PE certificate accounts for
every public instance field named `this`, its exact setter and interface
implementation, and every generated base chain. Unregistered, readonly or
non-object receiver fields and unproved foreign/dependent generic ancestry
refuse with SHARPTS406. The certificate trusts actual output assembly identity,
not a type name or interface supplied by another assembly.

Generated receivers without such a field query their fixed framework root using
literal `GetField("this", Public|Instance)` and preserve any real inherited field.
Exact framework receiver types use the same literal lookup: object, object array,
object List, string/object and object/object Dictionaries, object HashSet,
`Func<object[],object>`, string, bool, double, BigInteger, RuntimeType, Exception,
SocketException, object/object ConditionalWeakTable and object WeakReference.
Other CLR receivers, including field-bearing subclasses of these types, throw a
guest TypeError at the former reflective lookup point. This boundary does not
silently treat an unknown target as having no field. Ordinary/default emitter
APIs retain the public reflective behavior.

Literal framework queries need no SDK certificate, runtime-pack pin, publish
hook, broad roots or warning suppression. The private RuntimeType token retains
its exact CoreLib reference; public signatures still use normal facade
retargeting. Actual native-compiler export and native execution cover generic
owners, async/generator suspension, captured functions, defaults/rest arguments,
aliases and inherited Object helpers. An independent native public-constructor
probe accepts all sixteen exact framework types and rejects a foreign Exception
subclass without changing its public field. Ordinary console and larger function
corpus outputs retain identical sizes and every method body. Against immutable
`5073a305d`, strict console publication changes 36 diagnostics to 35, removing
only `$TSFunction.BindThis` IL2075 and adding none. Strict acceptance remains
incomplete.

## Warning policy and remaining work

The exported project uses `PublishAot=true`, `SelfContained=true`, its explicit
RID, individual trim/AOT diagnostics and warnings as errors. It adds no broad
assembly roots or warning suppressions. Any emitted-runtime warnings therefore
fail strict native publish instead of being hidden by successful project export.

### Historical warning inventory

A historical combined checkpoint before the residual-callable integration reported
27 strict `console.log(42)` diagnostics: IL2070 (16), IL2075 (11).
The two optional numeric-rest lookups, 39 Proxy bridge diagnostics, two
generic-normalizer diagnostics, the union-converter lookup, six dynamic
initializer diagnostics and three static-presence lookups are absent from the
earlier 80-diagnostic checkpoint. The residual-callable slice separately removes
three IL2075 records with no additions. Strict publication still failed at that
stage. Separate diagnostic publishes retained all warnings and passed focused
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

Tracked acceptance work in #1351 includes the generated-output warning ratchet
and broader AOT-safe feature dispatch, static third-party dependency closure,
SharpTS.Sdk platform acceptance, broader native debugging validation, a warning-free feature
corpus and cross-platform release evidence. Automatic CLI publication, structured
tool diagnostics and unsigned caching/owned output promotion are implemented
above. The profile can graduate only with the remaining evidence.

### Exact static field selection

Native static field queries use a late helper completed from the existing
class-initializer owner inventory. Exact owner identities select the actual
loaded `Type.GetField` operation with one of the three emitted public-static
flag combinations: ordinary, declared-only, or flattened inheritance. This
preserves field hiding, missing fields, null-name exceptions and initialization
timing. Unknown owners or flags receive a guest TypeError. An inventory with
unproved generic owners retains the complete legacy operation and its warnings.
Ordinary managed output retains its original lookup instructions.

After combining the callable, receiver, property and static-presence changes,
the strict Windows console inventory contains 22 diagnostics. This field helper
reduces it to 9, removing exactly 13 IL2070/IL2075 findings with none added.
Saved IL verification and native execution pass for console output and a class
control covering inherited fields, own shadowing, descriptors, deletion and
redefinition. Three managed output controls retain their PE sizes and every
method-body hash. Strict publication still fails on the remaining diagnostics.

Native constructor-key enumeration separately selects exact completed source
owners and executes the existing declared public-static `GetMembers` query on
their literal type tokens. It preserves loaded member order and attribute
identity without enumerating framework types. Generic or unresolved owner
inventories keep the entire legacy query. This removes one further IL2070
finding from the console control, reducing 9 diagnostics to 8 with none added.
Native own-key order, enumerability, deletion and redefinition controls pass.
The accompanying repeated-signature fix (#3082) prevents an older bodyless
accessor declaration from becoming reachable through metadata enumeration.

### Bounded deletion metadata

Native deletion uses a late lookup bundle for the two remaining reflective
operations in the strict and ordinary deletion paths. Static property queries
select exact completed initializer owners and retain the actual public-static
binder query. Dynamic field deletion selects exact source/runtime role owners
and queries only the existing private `$EnsureFields` method with its original
instance, nonpublic, declared-only flags. Invocation, descriptor checks,
initializer timing and dictionary removal remain at their existing positions.

Unknown owners, dynamic-store names or flags receive a guest TypeError at the
lookup boundary. Unproved generic owner inventories retain the complete legacy
operations and their diagnostics. Default managed output retains its original
lookup instructions. Against the eight-diagnostic console checkpoint before
the separate method/presence catalogs, this removes exactly four deletion
IL2070/IL2075 findings with none added. Native console and class deletion controls
pass saved-IL verification and execute with the remaining diagnostics retained.

### Exact in-operator member presence

The native `in` operator keeps its ordinary descriptor and prototype walk for
source instances, constructor values, functions and arrays. Only its CLR
fallback selects a completed, exact owner inventory and runs the loaded public
instance property query before the loaded method query. Direct method-binder
ambiguity remains observable; this path does not use `SafeGetMethod`'s overload
fallback. Unknown CLR owners throw a guest TypeError. Unproved generic metadata
retains the whole original reflective operation and its diagnostics.

The exact object-array carrier has a finite framework method-name inventory.
Each ignore-case name group must have one canonical spelling before emission.
A case-insensitive name comparison then selects that literal method name with
public-instance flags, preserving binder overload selection without rooting
unrelated static Array factories. Saved-IL and actual-native controls cover
ambiguity, foreign-owner refusal, initialization timing, typed/inherited source
members, computed symbols, static symbols and prototype replacement without
invoking getters. Three managed controls retain every method body and PE size.
The combined console checkpoint changes from six strict diagnostics to four,
removing exactly the two `HasIn` IL2075 findings and adding none. The remaining
four deletion diagnostics still fail strict publication.

### Constructor entry and new-target body selection

Native function construction records each actual source entry/body marker pair,
including nested functions and the AbortController value constructor. Completion
checks the exact output module, declaring owner, static shape, generic arity,
parameter/return ABI, completed source inventory and emitted body lifecycle.
Selection compares a trusted literal MethodInfo against the original entry or
its already-proved canonical method instantiation. It returns the literal body
definition and leaves generic resolution, argument shaping, defaults, rest,
new.target propagation and the original function-cache key unchanged.

No type/member-name scan establishes ownership. A foreign marker or hostile
MethodInfo cannot select an owned body through its Equals implementation.
Unproved owned metadata retains the whole original declaring-type/name/flags
lookup and exposes blockers and diagnostics. Ordinary managed emission keeps
its existing instruction sequence.

Saved-IL and native execution cover ordinary and generic constructors, bound
construction, Reflect.construct, defaults/rest, nested captures, module aliases,
AbortController and foreign-marker refusal before a body executes. The strict
constructor control removes exactly the InvokeConstructed IL2075 finding with
none added. Other new-target and symbol-owner reflection findings remain
visible in broader feature controls under #1351; this slice does not establish
warning-free acceptance for that full corpus.

### Exact source constructor factories

Native construction records the actual private static factory immediately after
the source class's factory marker is emitted. Completion validates its output
module, completed source owner, emitted body and (object[], object) -> object
ABI. Nongeneric inventories return the literal factory MethodInfo for the
recorded owner/name/declared-private-static flag protocol. Delegate creation,
caller arguments, defaults/rest and new.target ordering keep the existing path.

Unproved owned generic families retain the whole original binder operation.
Their ownership check uses real runtime Type identity and the actual recorded
generic definition; a foreign marker or hostile TypeDelegator cannot authorize
lookup. This slice does not manufacture closed factory tokens from raw generic
arguments. Ordinary managed output retains its original instructions.

The strict Map/Set/RegExp control changes from two findings to one, removing
only ConstructWithNewTarget's factory GetMethod IL2070 and adding none.
Activator's separate IL2067 remains visible under #1351. The independent
native Array.from(arguments) limitation is tracked in #3094.

### Constructor Type admission and Activator binding

The native constructor-value inventory records actual token emissions in source
class bindings, imported CLR constructor values, global constructor getters and
runtime constructor descriptors. The token helper emits the value and records
its body offset in one operation. Completion follows runtime and source type
creation in both compiler pipelines and publishes an immutable snapshot for
independent consumers. A property owner or a checked type annotation alone does
not admit a constructor Type.

The dynamic construction Type arm checks admission before reading custom
attributes. Exact values compare against trusted literal Type objects. Source
generic families retain the original operation after an exact runtime-Type kind
check and a recorded generic-definition identity check. Foreign Types, including
TypeDelegator instances with hostile metadata methods, receive a guest TypeError
before those methods run.

For completed literal owners, public constructor metadata and emitted constructor
bodies must be proved. Already-emitted closed BCL tokens remain exact values;
source generic constructor normalization remains an unproved family. Existing
intrinsic equality/return arms certify their own handling. The remaining exact
owners use the same `Activator.CreateInstance(Type, object[])` overload with a
literal Type and the original argument array. The binder, overload failures and
initialization order remain observable. This does not synthesize generic closures
or replace overload binding with a direct constructor call.

The collection/RegExp and Promise checkpoints each remove the Activator IL2067
diagnostic with no findings added. Their separate factory GetMethod diagnostic
requires the constructor-factory slice; generic legacy paths retain their actual
diagnostics rather than suppressing them. Default managed emission retains its
original constructor instructions.

### Late evaluated constructor metadata

Native frontend construction consumes the activation catalog's immutable completed
constructor-value snapshot after runtime and source producer bodies finish in both
compiler pipelines. Admission comes from actual token/value emission, with closed
owner and base contexts validated independently for the late operation. Checked
type annotations remain inputs to the existing legacy argument selection.

For proved exact closed values, CloseEvaluated returns the original evaluated Type
object. FirstPublicConstructor executes the full literal GetConstructors()[0]
operation, preserving loaded constructor order and the zero-constructor exception.
Callee evaluation, argument evaluation, parameter padding/conversion and Invoke
remain at their existing caller locations. No raw generic arguments manufacture new
closed tokens.

Unproved owned source generic families keep the complete previous closure and
constructor-selection operations behind exact real RuntimeType and owned generic
definition guards. The generic definition is the only portable family token;
an unbound self TypeSpec is never transplanted into a nongeneric helper. Foreign
and custom Type objects receive the named guest TypeError before their virtual
metadata can authorize a query. Blockers and legacy diagnostics remain visible.

The ordinary held-function construction control changes from five strict
diagnostics to zero, with exactly five removed and none added. Its saved initializer
previously contained one MakeGenericType and one GetConstructors instruction;
the finite replacement contains neither raw operation in that caller. Actual
native execution and the independent initializer/hostile-Type query probe pass.
Ordinary managed output retains identical method bodies and PE sizes. This
checkpoint does not establish strict support for unproved source generic families.

### Runtime receiver-initializer lookup

Native runtime-parent validation and initialization use a late finite lookup for
the existing public $InitializeReceiver convention. Its owner domain comes from
completed actual constructor-value producers and separately recorded class-template
token/Create pairs. A standalone token cannot complete template evidence. Actual
adapter declarations must match the compiler's initializer registry and finish with
the exact public static object(object, object[], object, object) ABI and body.

Each admitted closed owner executes the complete literal GetMethod(string) query.
Inherited matches, ambiguous names and missing methods retain the loaded runtime's
behavior. Promise handling, receiver/argument order, MethodInfo.Invoke and the
TargetInvocationException.InnerException path stay in their original positions.
Only real owned source generic families can enter the whole legacy query; their
blockers remain visible. Foreign and custom Type metadata cannot authorize a query.

The ordinary source-class control changes from twelve strict findings to eight,
removing exactly four and adding none. The two runtime sites contain no raw
GetMethod after this change. Actual native controls preserve class-template parents,
default/rest/bound/Reflect construction and constructor exception unwrapping.
A separate strict native probe preserves ambiguity, inheritance, named misses and
hostile-Type refusal. Source-owner/Symbol closure and prototype $EnsureFields
findings remain visible; this checkpoint is not strict source-class acceptance.

### Native source and symbol owner closure

Native static-member lookup, deferred static storage and symbol owner queries use
a separate late helper after source metadata and CLR ingress admission finish.
Its evidence comes from actual emitted constructor Type values, source interface
implementations and canonical mappings, constructor-base and Promise constructor
normalization, symbol owner token/registration pairs, and static-storage attribute
emissions. Constructor invocation ABI and unrelated property or initializer roles
do not establish this closure proof. Existing source-catalog finalization stays in
its original phase.

With complete evidence, the helper fences custom Type implementations before
metadata, retains closed symbol/ordinary static owners, and reuses the exact
canonical source tokens already emitted by the compiler. A native call outside
that proved open-owner domain throws a named TypeError. Missing or unproved
producer, CLR ingress, base or normalization evidence retains the whole original
operation, including its foreign Type callbacks and analyzer findings. In
particular, a non-generic source declaration does not prove that its generic base
can be normalized safely. The general SourceOwner.Resolve and SymbolClosedOwner
helpers retain their existing foreign/open/closed behavior.

This checkpoint removes exactly seven findings from the ordinary source-class
control: eight become one after the receiver lookup above, with no new findings.
The remaining prototype $EnsureFields query is a separate acceptance item. Strict
warnings remain enabled. Seven ordinary managed fixtures retain all 8,306 raw
method bodies (8,474 method definitions) and PE sizes; PE bytes differ only in
module MVID and COFF timestamp.
