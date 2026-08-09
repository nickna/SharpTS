# Avalonia desktop status and platform roadmap

Date: 2026-08-09

This document is the canonical engineering record for the Avalonia desktop effort. It replaces
the Phase 0, Phase 1A, Phase 1B, Phase 2, Phase 3A, and Windows-preview findings reports. Git
history remains the source for the full phase-by-phase narratives.

## Phase 0 scope resolution

Phase 0 is complete for the active Windows product scope. Windows x64 feasibility passed in both
interpreted and compiled modes with a real window, dispatcher-owned input/rendering, timers, and
asynchronous continuations. The original cross-platform exit criterion is not complete: no macOS
gate has run or passed. Phase 4 Track E now supplies macOS payloads, cross-publish evidence, and
native/release workflows, but those workflows have not run, so the original criterion remains open. Native
Windows ARM64 execution is likewise release work rather than evidence already obtained. This
distinction is deliberate and no cross-platform compatibility is implied.

## Phase 1A stabilization: shutdown, diagnostics, and interpreted top-level await

Phase 1A.1 and 1A.2 are complete at `b2c0f555`; Phase 1A.3 is complete at `b3c993e6`.
All three chunks were verified on Windows x64 on 2026-08-08. Hosted ABI 1, GUI API 2,
application manifests, and the supported application surface are unchanged.

Every desktop termination path now converges on one idempotent host coordinator. A native window
close is cancelled long enough to run hosted shutdown, then the close interception is detached so
reverse cleanup can dispose the renderer and native window before Avalonia receives the final exit
request. Automated completion, initialization failure, uncaught guest errors, watchdog failures,
and hosted lifetime requests use the same coordinator with their original shutdown reason and exit
code. Repeated requests select the first reason, and bridge callbacks arriving after selection are
ignored.

Detailed tracing is now opt-in. An ordinary launch records no detailed events and writes no trace;
bare `--trace` writes a uniquely named file under `%LOCALAPPDATA%\SharpTS.Gui\Traces`, while
`--trace <path>` preserves the explicit path. `--auto-close` enables conformance tracing and uses
the default trace directory unless a path is supplied. Trace-write failures produce a nonfatal
warning and never print a misleading location. Host-managed traces retain the newest 20 files;
explicit paths are not pruned. Fatal reports are separate under
`%LOCALAPPDATA%\SharpTS.Gui\Errors`, where the newest 10 host-managed logs are retained. An
explicit `SHARPTS_GUI_ERROR_LOG` is overwritten for each failure.

Hosted interpreted ESM now evaluates top-level awaits inside compound expressions,
conditionals, and loop bodies through the asynchronous evaluator. Dynamic imports evaluate an
awaited specifier, prepare a newly discovered ESM dependency graph once, execute it in dependency
order, and run a complete guest microtask checkpoint at each module-job boundary. Already-active
cycles observe registered live bindings; a dynamic import that tries to await its own evaluating
module rejects instead of deadlocking. Missing modules and rejected module jobs reject in guest
code, while an uncaught initialization error identifies the failing module path. Shutdown cancels
a suspended hosted await and rejects late timer or callback work. These hosted paths do not call
the console runtime's `WaitForPromise` pump, replace Avalonia's synchronization context, poll, or
start a nested dispatcher.

### Phase 1A.1/1A.2 verification

| Gate | Result |
| --- | --- |
| Focused hosted/JSX/SDK | `dotnet test SharpTS.Tests/SharpTS.Tests.csproj -c Release --filter "FullyQualifiedName~SharpTS.Tests.Hosting\|FullyQualifiedName~JsxTypeCheckerTests\|FullyQualifiedName~GuiSdkTaskTests"`: 48 passed, zero skipped, zero failed |
| GUI conformance | `dotnet test SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj -c Release --no-restore`: 41 passed, zero skipped, zero failed |
| Release solution build | `dotnet build SharpTS.sln -c Release --no-restore`: zero warnings, zero errors |
| Canonical core suite | CI/release exclusions for `LiveNetwork`, `LoadSensitive`, and `npm`: 16,521 passed, two documented HTTP lifecycle skips, zero failed |
| Packaged Windows x64 consumer | `SharpTS.Gui.Sdk.Consumer/Run-PackagedConsumer.ps1 -RuntimeIdentifier win-x64 -RealWindow`: package audit, path-with-spaces rebuild, IL verification, interpreted/compiled Headless and real-window directory runs, asset closure, and compiled single-file Headless/real-window runs passed |

The real-host lifecycle fixture covered ordinary close, cancelled-then-successful close, repeated
close, initialization-time close, and close with queued timer/off-thread work in interpreted mode.
It asserts one effect cleanup, one unmount, one host exit request, no late timer callback, and the
ordered sequence `beforeExit`, its microtask checkpoint, cleanup/unmount, `exit`, host exit, and
runtime disposal. Automated interpreted and compiled runs assert the same lifecycle subsequence
and retain their pre-shutdown renderer-disposal conformance check. Parser/host tests cover disabled,
bare, automatic, and explicit trace modes, an ordinary no-trace execution, a deliberately
unwritable explicit trace target, separated fatal/trace directories, explicit-log overwrite, and
10/20-file retention. The unwritable trace run still started and shut down successfully and
reported only the expected diagnostic warning.

### Phase 1A.3 verification

| Gate | Result |
| --- | --- |
| Hosted runtime unit suite | `dotnet test SharpTS.Tests/SharpTS.Tests.csproj --filter FullyQualifiedName~HostedInterpreterRuntimeTests`: 25 passed, zero skipped, zero failed |
| Focused hosted/JSX/SDK | `dotnet test SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~SharpTS.Tests.Hosting\|FullyQualifiedName~JsxTypeCheckerTests\|FullyQualifiedName~GuiSdkTaskTests"`: 54 passed, zero skipped, zero failed |
| GUI conformance | `dotnet test SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj -c Release --no-restore`: 42 passed, zero skipped, zero failed |
| Release solution build | `dotnet build SharpTS.sln -c Release --no-restore`: zero warnings, zero errors |
| Canonical core suite | CI/release exclusions for `LiveNetwork`, `LoadSensitive`, and `npm`: 16,527 passed, two documented HTTP lifecycle skips, zero failed |
| Packaged Windows x64 consumer | `SharpTS.Gui.Sdk.Consumer/Run-PackagedConsumer.ps1 -RuntimeIdentifier win-x64 -RealWindow`: package audit, path-with-spaces rebuild, IL verification, interpreted/compiled Headless and real-window directory runs, asset closure, and compiled single-file Headless/real-window runs passed |

The deterministic hosted suite covers successful compound, conditional, and loop awaits;
successful dynamic-import dependency evaluation; missing, rejected, and cyclic dynamic imports;
module-path error attribution; and shutdown during a suspended timer await with no late callback.
The Avalonia Headless trace covers successful and caught-rejection cases for compound,
conditional, loop, and dynamic-import awaits, dependency and module microtask checkpoints, window
mount, and the exact `beforeExit` through runtime-disposal shutdown sequence.

## Phase 1B stabilization: compiled module-job parity and ABI rules

Phase 1B is complete in the Phase 1B implementation commit. Compiled hosted ESM now uses the same
async state-machine lowering as ordinary async functions instead of splitting only direct await
statements. It supports awaits nested in compound expressions, conditionals, loops, and
try/catch; preserves export visibility before dependent jobs run; discovers literal dynamic-import
graphs during preparation; initializes those graphs lazily; rejects missing, failed, and active
self-imports in guest code; attributes uncaught initialization failure to the module path; and
cancels suspended initialization without accepting late timer work. Each compiled module job runs
a complete microtask checkpoint before the next dependency begins.

Hosted ABI remains version 1. These changes refine scheduler and module semantics behind the
existing validated runtime factory and do not change the hosted assembly entry-point shape.
Compatibility is enforced as follows:

| Contract | Compatibility rule |
| --- | --- |
| Hosted ABI | Persisted compiled guests declare ABI 1. A different or missing ABI marker is rejected before runtime construction and guest initialization. |
| GUI API | Application manifests declare GUI API 2. Other versions fail before payload loading; no API 1 compatibility mode is active. |
| SDK/runtime package | Applications must rebuild with the SDK/runtime package that supplies their manifest and generated TypeScript surface. Package assets are an atomic contract rather than independently versioned files. |
| Application manifest | API 2 requires the compiled/interpreted payload fields plus descriptor schema metadata. Missing required metadata is a rebuild-with-current-SDK error. |
| Descriptor schema | Schema version 1 and its semantic SHA-256 must match the host exactly. Diagnostics include host and application values, and validation occurs before guest initialization. |

The cross-mode Avalonia Headless fixture asserts identical ordered stages for caught compound,
conditional, and loop rejections; rejected dynamic import; lazy dependency evaluation; dependency
and module microtask checkpoints; resumed exports; window mount; and `beforeExit`/`exit` cleanup.
The hosted unit suite additionally covers default and function exports, missing/failed/self dynamic
imports, module-path attribution, ABI construction, and cancellation of a suspended top-level
await.

### Phase 1B verification

| Gate | Result |
| --- | --- |
| Hosted runtime unit suite | `dotnet test SharpTS.Tests/SharpTS.Tests.csproj -c Release --filter "FullyQualifiedName~HostedInterpreterRuntimeTests"`: 32 passed, zero skipped, zero failed |
| GUI conformance | `dotnet test SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj -c Release --no-build --no-restore`: 51 passed, zero skipped, zero failed |
| Release solution build | `dotnet build SharpTS.sln -c Release --no-restore`: zero warnings, zero errors |
| Canonical core suite | CI/release exclusions for `LiveNetwork`, `LoadSensitive`, and `npm`: 16,536 passed, two documented HTTP lifecycle skips, zero failed |
| Packaged Windows x64 consumer | `SharpTS.Gui.Sdk.Consumer/Run-PackagedConsumer.ps1 -RuntimeIdentifier win-x64 -RealWindow`: package audit, path-with-spaces rebuild, IL verification, interpreted/compiled Headless and real-window directory runs, asset closure, and compiled single-file Headless/real-window runs passed |

## Phase 3A SDK workflow completion

Phase 3A is complete in the Phase 3A implementation commit. The `SharpTS.Gui.Sdk` NuGet package
now carries a `dotnet new sharpts-gui` template containing a minimal TSX application, strict
tsconfig, asset example, and a Headless assertion fixture. Its package pin matches the SDK release.
The package-consumer gate installs the template into an isolated hive, creates it under a path with
spaces, and exercises restore, IL-verified build, interpreted and compiled Headless runs, clean,
RID restore, framework-dependent publish, and startup from the published directory.

Incremental guest compilation now fingerprints all project TS/TSX files, inherited config,
generated GUI package files, project/SDK properties, descriptor schema, and compiler/bridge
binaries. An unchanged build retains the guest assembly timestamp; source and compilation-property
changes regenerate it. `SharpTSGuiIncludeSourcePayload=false` omits `Guest`, generated tsconfig,
and materialized TypeScript package files from a directory distribution, selects compiled mode by
default, and uses a compiled-only dependency validator. The packaged test proves that output has no
source payload and does not modify its installation directory while running.

The root README, package readme, and `docs/gui/sdk-development.md` document ordinary commands,
supported Windows RIDs and modes, approximate package/directory footprint, source-payload control,
preview limitations, dispatcher ownership, and why raw Avalonia/custom-control registration remains
internal.

## Current 0.2 development status

`0.2.0-preview.1` advances the manifest to GUI API 2 while keeping Hosted ABI 1. Its implemented
renderer slice adds a retained logical fiber tree, component/fragment key ownership,
layout-transparent fragments, duplicate logical-key validation, post-render effects, ignored
updates after unmount, render/effect error boundaries, dynamically diffed keyboard handlers, and
per-window key-repeat tracking. Native setter failures reverse to the last committed VNode tree;
if that recovery also fails, the damaged window root is disposed and the combined fatal error is
routed through the host. The Calculator and packaged consumer use the API 2 SDK, and API 1
manifests receive a migration-oriented rejection. The JSX checker now infers generic function
component props, validates callable-object signatures, checks `children` and `ref` as declared
props, and validates `key` through `JSX.IntrinsicAttributes`.

The generated descriptor contract, native-commit boundary recovery, full JSX parity set, GUI-aware
LSP metadata, SDK template, projectless CLI, multi-window lifecycle, resources/styles, typed data
controls, retained drawing, static custom providers, Windows platform services, hot reload,
devtools, and Windows environment conformance are complete for their declared surface. The x64
SDK now produces and runs a warning-clean Native AOT application under versioned performance and
artifact budgets. Installed MSIX applications also expose bounded informational local
notifications without adding a Windows App SDK runtime dependency. Native ARM64 execution, public
package onboarding, production signing identity, and native macOS evidence remain external gates. See
`docs/gui/migrating-api-1-to-2.md` for the preview.1 contract.

The notification adapter is a fixed, reflection-free WinRT ABI binding guarded by package-identity
detection. Headless calls validate and complete without native delivery; interpreted and compiled
multi-window guests exercise the public API, while an unpackaged native call proves the explicit
MSIX rejection. On 2026-08-09 the 76-test GUI conformance suite, 33 focused JSX/SDK tests, five
Windows distribution tests, Release package audit, full Windows x64 real-window/Native AOT
lifecycle, Windows ARM64 cross-publish, both macOS cross-publish matrices, and the canonical core
suite (16,555 passed, two documented skips) passed. The exact
candidate was 39,752,127 bytes with SHA-256
`8A5081BC1779A84B84A563E693C7EE2539D35E398C772384F3E9CA20E09F04E3`.

## Phase 3B CLI and candidate status

`sharpts new avalonia` creates a TypeScript-only application with an explicit Avalonia host marker
and pinned `0.2.0-preview.1` SDK. `sharpts app run|build|compile|publish` applies explicit-host,
manifest, safe-inference, then console precedence and routes Avalonia work through a deterministic
internal `SharpTS.Gui.Sdk` project. SDK and CLI package gates compare ABI/API/schema manifest fields
and managed/native closures.

The exact local candidate is 26,332,519 bytes with SHA-256
`B0E3EEF8C1B9A0329097D858C01409BED28155FE287684F28300580CA57BB28E`. On Windows x64 it passed
direct-SDK and CLI build/no-op/clean/publish gates, interpreted and compiled Headless runs,
real-window runs, and self-contained single-file execution with an invalid `DOTNET_ROOT`. The same
package bytes passed ARM64 directory and single-file cross-publish. Native ARM64 execution and
public NuGet publication remain unpassed external gates; `publish: false` is intentionally retained.

| Phase 3B gate | Result on 2026-08-09 |
| --- | --- |
| Release solution build | Passed with zero warnings and zero errors |
| Canonical core suite | 16,547 passed, two documented HTTP lifecycle skips, zero failed |
| GUI conformance | 51 passed, zero skipped, zero failed |
| Exact x64 candidate | 14 retained traces; SDK and CLI Headless/directory/single-file plus real-window gates passed |
| Exact ARM64 candidate | The same SHA-256 package produced both SDK and CLI directory/single-file outputs; five host-architecture development traces passed |
| NuGet release helper | Nine preflight/publication-inventory scenarios passed |
| Public NuGet state | Exact package search returned no `SharpTS.Gui.Sdk`; ID onboarding and API-key scope remain external blockers |

## API 1 baseline decision

- The Windows x64 public-preview implementation is complete and verified. It passed the isolated
  SDK package lifecycle, interpreted and compiled Headless and real-window execution, and a
  self-contained compiled single-file run without a usable `DOTNET_ROOT`.
- Windows ARM64 directory and single-file cross-publish pass. Native ARM64 Headless and
  real-window execution remain outstanding; cross-publish evidence is not native execution
  evidence.
- `SharpTS.Gui.Sdk` is prepared as `0.2.0-preview.1` but is unpublished. NuGet package-ID
  onboarding and separate API-key scope validation are prerequisites to publication.
- Preview productization cleanup is complete. Projects, paths, tests, traces, workflows, renderer
  units, bridge ownership, host services, conformance hooks, and RID declarations now use durable
  boundaries without changing the preview contract.
- The TSX application API is implemented at GUI API version 2: typed function components,
  natural children, keyed fragments, standard hooks, typed refs, keyboard/focus input, a broad
  built-in control set, desktop dialogs/clipboard, and reproducible packaged assets. The
  `Examples/Calculator` application exercises this surface in interpreted and compiled modes.
- macOS is reactivated as an experimental SDK candidate. Both RIDs cross-publish and bundle
  structurally, but no native macOS or Apple release gate has passed, so this is not a support claim
  and does not gate the Windows preview.

The result is a go for the implemented `win-x64` preview and for `win-arm64` cross-publish, not a
claim that every Windows architecture has executed natively and not a cross-platform product go.

## Completed capabilities

### Hosting and scheduling

- Avalonia owns the process main thread, dispatcher, synchronization context, and classic desktop
  lifetime. SharpTS neither replaces that synchronization context nor starts a competing blocking
  event loop.
- Interpreted and compiled guests are dispatcher-driven. One coalesced host turn runs at most one
  external callback, timer, or module job and then drains the complete guest microtask checkpoint.
  The earliest guest timer is represented by one cancellable host deadline; there is no polling
  pump.
- Off-thread notifications are queued for the owner thread, synchronous return-valued off-thread
  calls are rejected, and owner-thread reentrancy runs inline while deferring its checkpoint to the
  outermost guest boundary.
- Framework-neutral dispatcher, lifetime, and error-sink contracts serve both execution modes.
  Hosted ABI 1 validates assembly metadata before constructing a compiled guest.
- Initialization faults and uncaught errors route through the host error sink and ordered shutdown.
  Graceful shutdown observes `beforeExit`, reverse cleanup, and `exit`; `process.exit(code)` requests
  host shutdown without calling `Environment.Exit`.
- Hosted interpreted and compiled ESM support static and dynamically discovered top-level-await
  graphs, including awaited compound expressions, conditionals, loops, dynamic-import specifiers,
  ordered module-job microtask checkpoints, and cancellation during suspended initialization.

### Renderer and TypeScript surface

- The retained renderer validates a complete incoming tree before structural mutation and
  reconciles keyed or positional children in place. Key/kind matches retain native controls;
  kind changes replace them.
- Signals use `Object.is` equality, coalesce invalidations through a hosted microtask, and commit
  dynamic dependencies only after a successful render. Work queued after root disposal is ignored.
- Refs preserve native identity and detach before removal. Event wrappers read the latest guest
  callback while retaining one native subscription for the mounted lifetime. Cleanup is
  deterministic, child-first, and idempotent.
- The descriptor registry is internal. The public node set now covers core layout, text/images,
  actions, text and selection input, numeric/date/time input, tabs, menus, tool/status bars, and
  fragments. List and combo data remain string-backed in GUI API 1.
- Typed props cover layout, per-edge spacing, colors, typography, content alignment, visibility,
  enabled/opacity state, tooltips, accessibility names, Grid/Dock placement, focus refs, and
  normalized keyboard events. Controlled input commits suppress native callback echoes; real user
  changes dispatch the latest guest callback.
- TSX produces a JavaScript element tree. Typed function components and natural primitive children
  are materialized by a lifecycle-aware root with `useState`, `useReducer`, `useEffect`, `useMemo`,
  `useCallback`, `useRef`, and `useControlRef`. `createSignal` remains the supported external-state
  primitive. Conformance helpers stay isolated under `@sharpts/gui/internal-testing`.

### SDK, deployment, and release controls

- An isolated consumer with no project references restores `SharpTS.Gui.Sdk` from a package feed,
  including from a path containing spaces. SDK targets generate the consumer launcher, hosted ABI
  manifest, tsconfig overlay, and compiled guest, while retaining source payloads for interpreted
  development execution.
- Development builds and framework-dependent directory publishes support interpreted and compiled
  modes. A Windows RID publish defaults to a Windows-subsystem, self-contained, compiled
  single-file executable containing the guest and native payload; interpreted mode is rejected
  with a durable fatal diagnostic.
- Package contents are filtered to `win-x64` and `win-arm64`, contain no PDBs, and expose no
  absolute repository paths in MSBuild assets. Both single-file publish directories contain only
  the consumer executable.
- Interactive Windows-subsystem failures are written under `%LOCALAPPDATA%\SharpTS.Gui\Errors`
  and may show a minimal native dialog when no console is attached. Opt-in detailed traces use the
  sibling `Traces` directory.
- Release preflight requires the fixed preview artifact and a registered package ID before any
  stable package push. The release manifest excludes the GUI SDK from publication and inventory
  with `publish: false` until maintainers intentionally complete onboarding and remove the guard.

### Productization boundaries

- The renderer is split into root reconciliation, mounted-node ownership, validation,
  common-property, registry, and control-descriptor units while retaining its original commit
  ordering and cleanup semantics.
- A host-owned application runtime context holds the dispatcher callbacks, trace recorder, and
  single active root. Production VNode/microtask interop and conformance-only controls use distinct
  facades; normal `@sharpts/gui` imports cannot reach the conformance hooks.
- Host option parsing, manifest and embedded-payload loading, Avalonia lifetime coordination, and
  fatal diagnostics are separate services. Windows logging paths and the native error dialog are
  confined to the Windows diagnostics adapter.
- `Sdk/SupportedPlatforms.props` is the source of truth for `win-x64` and `win-arm64`. SDK
  validation, package assets, the consumer harness, and workflow-coverage tests consume or verify
  that declaration.
- GUI API version 1 is written into file and embedded manifests and rejected on host mismatch.
  Project-local assets and SHA-256-pinned HTTP(S) assets are prepared at build time and embedded
  under stable `asset:///` logical names.

## Milestones

| Milestone | What it proved | Earlier limitation retired |
| --- | --- | --- |
| Phase 0 | Avalonia could own a responsive Windows UI thread while interpreted and compiled guests mounted a real window, handled an event, timer, promise, and off-thread completion, and published a complete framework-dependent asset closure. | Established feasibility, but used a 5 ms dispatcher pump, one-time mounting, a four-control surface, and separate host/guest assemblies. |
| Phase 1A | A framework-neutral interpreted dispatcher/lifetime/error contract delivered deterministic turns, timers, lifecycle, cleanup, static and dynamic ESM module jobs, and expanded top-level-await syntax. | Removed polling and synchronous promise pumping from interpreted hosting and replaced ad hoc task delivery with a deterministic scheduler; compiled syntax and trace parity remain Phase 1B work. |
| Phase 1B | Hosted ABI 1 and the compiled scheduler matched interpreted scheduling, lifecycle, expanded top-level-await, dynamic-import, synchronization-context, and no-pump behavior. | Removed compiled polling and the prototype initializer/pump pair; replaced it with validated hosted assembly metadata, asynchronous module runners, and a shared runtime contract. |
| Phase 2 | A retained keyed renderer, signals, refs, latest-callback events, validation, and deterministic cleanup produced identical interpreted and compiled traces. | Replaced one-time recursive mounting and imperative sample updates; retained the original small descriptor set and local source package. |
| Phase 3A | A separately restored MSBuild SDK drove an isolated consumer through build, clean/rebuild, publish, IL verification, Headless, and real-window execution. | Replaced repository-local host compilation and copying with an SDK-owned manifest, overlay, launcher pipeline, and packaged consumer boundary. |
| Windows preview | Durable assembly names, typed layout/display/form controls, controlled inputs, Windows-only RID filtering, generated consumer launcher, and compiled self-contained single-file deployment passed on x64; ARM64 cross-publish passed. | Expanded beyond the four-control and framework-dependent-only experiment, reduced the all-platform package, and added release-preflight and Windows fatal-diagnostic safeguards. |
| Preview productization | Durable root-level project identities, decomposed renderer/bridge/host services, isolated conformance APIs, and one validated Windows RID declaration passed the packaged x64 and ARM64 gates. | Removed prototype paths and process-global test state, retired the renderer and host monoliths, isolated Windows diagnostics, and prevented package/SDK/harness/workflow RID drift. |

## Decision ledger

### Retained

| Decision | Current rule |
| --- | --- |
| UI ownership | Avalonia owns the UI thread, dispatcher loop, synchronization context, and desktop lifetime. |
| Hosted contract | Hosted ABI remains version 1 for interpreted/compiled parity and persisted-assembly loading. |
| Root model | A runtime configuration supports one active `Window` root. Multi-window semantics are not inferred from Avalonia capabilities. |
| Control metadata | The descriptor registry remains an internal, explicit mapping rather than public reflection or custom registration. |

### Changed

| Earlier position | Current position |
| --- | --- |
| Dispatcher polling was acceptable for feasibility. | Both guest modes use wake/coalescing and exact host deadlines; polling is removed. |
| Rendering was a one-time recursive mount. | Rendering uses retained keyed/positional reconciliation with signals, refs, validation, fresh callbacks, and cleanup. |
| Deployment was framework-dependent and directory-based. | Framework-dependent directories remain for dual-mode development; RID publish adds a compiled self-contained single-file Windows executable. |
| A real-window macOS run was the immediate next feasibility gate. | Native Intel and Apple Silicon jobs now encode that gate. They remain unpassed until those workflows execute and do not gate the Windows preview. |

### Deferred

- Native Windows ARM64 Headless and real-window execution.
- NuGet ID reservation/onboarding, API-key scope validation, and package publication.
- Production signing-certificate execution and publication of an application-owned MSIX identity;
  local packaging, update, SBOM/provenance, diagnostics, enterprise, and support-policy gates exist.
- Arbitrary public control templates and a full editing DataGrid.
- Native AOT ARM64 certification and native macOS certification/signing evidence.

## Architecture boundaries

Portability depends on keeping the following seams intact during future evolution:

- Keep dispatcher, lifetime, error-sink, and hosted ABI contracts independent of Avalonia and
  Windows. Framework-neutral scheduling semantics must not acquire control or platform types.
- Keep reconciliation, validation, descriptors, mounted-node ownership, refs, and event freshness
  independent of Windows packaging. Renderer behavior must be testable without a WinExe or native
  dialog.
- Confine RID filtering, subsystem selection, native fatal dialogs, and platform assets to
  platform-specific packaging or host adapters. A consumer manifest and generated launcher should
  not encode Windows behavior into the hosted ABI.
- Keep one source of truth for supported platforms and RIDs so package contents, SDK validation,
  consumer harnesses, and workflows cannot drift.
- Do not turn macOS cross-publish or structural bundle evidence into a compatibility claim before
  native execution, signing, and notarization gates pass.

## Completed preview productization cleanup

The pre-publication cleanup is complete and behavior-preserving:

1. Prototype project, namespace, test, trace, workflow, and path identities were replaced with
   durable `SharpTS.Gui`, `SharpTS.Gui.Host`, SDK-consumer, and conformance-oriented names.
2. The 1,383-line renderer was decomposed while retaining validation-before-mutation, keyed
   identity, event/ref ordering, and deterministic cleanup.
3. Bridge state now belongs to an explicit application runtime context with a scoped host
   registration and the existing single-root policy.
4. Host startup is separated into option, payload, lifetime, and platform-diagnostics services.
5. Conformance state and controls are isolated behind `@sharpts/gui/internal-testing`; contract
   tests verify that the normal package entry point does not expose them.
6. Supported and candidate desktop RIDs have one declaration that drives SDK validation and package assets and
   is consumed or checked by the package harness and workflow tests.

## Windows preview publication gate

Publication is complete only when all of the following are recorded against the candidate commit
and exact `0.2.0-preview.1` artifact:

1. Run native ARM64 Headless and real-window scenarios on a Windows ARM64 machine and retain both
   traces. Cross-publish success is necessary but does not satisfy this gate.
2. Reserve/onboard the `SharpTS.Gui.Sdk` NuGet ID with an approved preview and separately verify
   that the release API key is scoped to it. The stable release workflow must not perform the
   first publication of a new ID.
3. Run the complete release dry run and public-feed preflight, inspect the package, run packaged
   x64 and native ARM64 gates, run the full solution tests and Release build, and run repository
   diff/whitespace checks. Confirm the artifact is unchanged after validation.
4. Review the release inventory and documentation version pins, then intentionally remove the
   manifest's `publish: false` guard in a dedicated, reviewable change.
5. Publish only after every prior step passes. Retain the package hash, package inventory,
   workflow URLs, x64 and ARM64 traces, and NuGet verification result as the preview release
   record.

## Post-preview stabilization

Compatibility/versioning policy, recoverable native commit errors, leak/soak and deterministic
renderer gates, GUI performance budgets, static custom-control providers, and the local Windows
distribution/support toolchain are now implemented. Remaining release stabilization is evidence:
native ARM64 execution, public package ownership, production certificate-backed installer runs,
and native/signing evidence for the macOS candidate. Packaged informational notifications are
locally implemented; production identity-backed execution remains part of the installer evidence.

## macOS reactivation status

Track E implementation is locally complete. `osx-x64` and `osx-arm64` are explicit SDK entries;
the host uses macOS log/alert behavior; both architectures cross-publish from the same audited
package; real Mach-O outputs pass `.app`, plist, symbol, architecture, and checksum validation;
and native Intel/Apple-Silicon plus protected signing/notarization workflows are committed. The
current exact local package is 39,752,127 bytes with SHA-256
`8A5081BC1779A84B84A563E693C7EE2539D35E398C772384F3E9CA20E09F04E3`.

Still required before support: execute interpreted and compiled Headless and real-window cases
natively on both architectures, then run Developer ID signing, app/DMG notarization, stapling,
Gatekeeper validation, and provenance against the exact candidate. This workstation supplies
neither macOS execution nor Apple credentials, so those are external evidence gates, not passes.
See [macOS GUI preview and distribution](macos-distribution.md).

## Evidence appendix

### Current-main integration verification

The Avalonia baseline was replayed onto `main` at `a97e361e` and validated at integration commit
`2ab6186093087628d62fc3146d062b0f265f2f7a` on 2026-08-08.
This is a historical integration snapshot; the Phase 3B table above supersedes its candidate hash
and test counts.

| Gate | Integrated result |
| --- | --- |
| Release solution build | `dotnet build SharpTS.sln -c Release` passed with zero warnings and zero errors |
| Focused hosted/JSX/SDK tests | 48 passed, zero skipped, zero failed |
| GUI conformance | 25 passed, zero skipped, zero failed |
| Canonical core suite | 16,521 passed, two documented HTTP lifecycle skips, zero failed using the CI/release exclusions for `LiveNetwork`, `LoadSensitive`, and `npm` |
| Isolated x64 package lifecycle | Restore, package audit, path-with-spaces build/no-op/clean/rebuild, IL verification, interpreted and compiled Headless execution, framework-dependent directory publish, asset closure, compiled single-file publish, and missing-manifest diagnostics passed |
| Candidate package | `SharpTS.Gui.Sdk.0.2.0-preview.1.nupkg`, 26,209,832 bytes, SHA-256 `F8E73734D26FEF43B59A9D5626F77F4065DD472FA0010F8E28FB162543C826F5` |

The unfiltered local core run additionally executed opt-in live-network tests. Its compiled
`DnsRecordTypeTests.LiveSmoke_ResolveNs_Promise` case produced no output; the canonical suite
excludes this category by design and deterministic dual-mode DNS tests passed. No ARM64 native or
macOS execution was performed for this integration gate.

### TSX application API verification

The GUI API 1 worktree passed the following focused gates on 2026-08-08:

| Gate | Result |
| --- | --- |
| Desktop/conformance tests | 18 passed in interpreted/compiled Headless integration; zero failed |
| JSX type-checker and GUI SDK task tests | 25 passed; zero failed |
| Calculator reducer contract | Eight arithmetic, chaining, percent, edit, digit-limit, and error-recovery scenarios passed |
| Packaged Calculator build | Release build and persisted-IL verification passed against a freshly packed `SharpTS.Gui.Sdk.0.1.0-preview.1` |
| Packaged Calculator execution | Interpreted and compiled Headless smoke runs passed |
| Package/manifest audit | `gui/runtime.ts`, tasks, GUI bridge, hosted ABI 1, and GUI API 1 were present; generated SDK intermediates remained under `obj` |

A full `SharpTS.Tests` run did not complete in this sandbox. Hang attribution recorded 15,917
completed tests and one active case before the run was stopped:
`StandaloneDllTests.Isolated_Tls_ShouldExecuteWithoutSharpTsDll`. Running that TLS case alone also
timed out after three minutes without an assertion or compiler error. This is not recorded as a
full-suite pass; CI or a network-capable environment must close that regression gate.

### Final Windows verification

| Gate | Final recorded result |
| --- | --- |
| Release solution tests | 16,547 core tests and 51 desktop/conformance tests passed; two documented HTTP lifecycle skips; zero failures |
| Release solution build | Passed with zero warnings and zero errors |
| Isolated x64 package lifecycle | Direct SDK and projectless CLI restore, build, no-op build, clean/rebuild, path-with-spaces, IL verification, parity, and missing-entry diagnostics passed |
| Development modes | Interpreted and compiled traces matched |
| x64 framework-dependent directory | Headless and real-window passed in interpreted and compiled modes; dependency closure passed |
| x64 compiled single file | Headless and real-window passed with invalid `DOTNET_ROOT`; interpreted mode rejection produced the expected fatal diagnostic |
| ARM64 artifacts | Directory and single-file cross-publish passed with correct RID payload and no sidecars |
| ARM64 native execution | Pending; neither Headless nor real-window execution has been recorded |
| macOS | Deferred; not run and not passed |

Earlier gates established the progression: Phase 1B recorded 18 hosted runtime/ABI tests and two
dual-mode Headless tests; Phase 2 recorded ten renderer/integration tests and 19 hosted runtime
tests; Phase 3A recorded ten renderer/source Headless tests and 23 hosted runtime/SDK task checks.
The final counts above supersede those snapshots as the current release evidence.

### Artifact sizes

| Artifact | Size/count |
| --- | ---: |
| `SharpTS.Gui.Sdk.0.1.0-preview.1.nupkg` | 26,150,323 bytes |
| x64 framework-dependent directory | 69 files; 48,767,053 bytes |
| x64 single executable | 121,877,925 bytes |
| ARM64 single executable, cross-published | 129,519,545 bytes |

For historical comparison, the Phase 3A all-platform local package had 112 entries and was
201,368,297 bytes. The initial Phase 0 framework-dependent closure validated 49 managed, 40
native, and 38 resource assets across 38 selected libraries. The preview's Windows-only package
and single-file evidence retire those experiments' all-platform payload and directory-only
limitations.

### Representative retained-renderer and lifecycle evidence

The compiled x64 single-file real-window trace included:

```text
4   view-render-1
31  mount
33  identities-initial
36  view-render-2
59  coalesced-update-complete
60  identities-reordered
76  forms-events-complete
85  guest-click
93  unmount
94  late-reactive-work-ignored
95  dispatcher-sentinel
96  guest-timer
```

Window, layout, form, action, and keyed `a`/`b` controls retained native identity. A keyed
TextBlock-to-Button kind change received a new identity, reordered keys moved without recreation,
the latest callbacks fired, and disposal released refs and subscriptions before ignoring queued
reactive work. The sentinel and timer completed without polling.

Deterministic hosted-runtime conformance separately fixed the lifecycle orders:

| Scenario | Asserted order |
| --- | --- |
| Turn fairness | `macro-1`, `micro-1`, `promise-1`, host `sentinel`, `macro-2` |
| Graceful shutdown | `beforeExit`, `beforeExit-microtask`, cleanup 2, cleanup 1, `exit` |
| Forced exit | synchronous `exit-7-7`; no `beforeExit`; lifetime request 7 |

### Evidence boundary

The x64 results above are native execution evidence because the published applications ran on
Windows x64. The ARM64 results prove restore, asset selection, architecture-safe validation, and
artifact construction from an x64 host; they do not prove that the ARM64 CLR/native payload
starts, opens a real window, or preserves dispatcher behavior on ARM64 hardware. Only the
hardware-backed workflow gate can close that distinction.
