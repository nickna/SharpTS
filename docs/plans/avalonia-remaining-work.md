# Avalonia GUI remaining-work plan

**Status:** Proposed implementation plan
**Date:** 2026-08-08
**Primary release scope:** Windows x64 and Windows ARM64 desktop applications
**Deferred platform scope:** macOS, until explicitly reactivated
**Baseline implementation:** `codex/avalonia-phase-0` at `989af7f4`

## Purpose

This document turns the incomplete portions of
[`avalonia-12-tsx-investigation.md`](avalonia-12-tsx-investigation.md) into an executable completion
plan. It covers only phases with residual work: Phase 0, Phase 1A, Phase 1B, Phase 2B, Phase 3A,
Phase 3B, and Phase 4. Phase 2A's retained-renderer vertical slice is complete and is treated as a
dependency rather than reopened here.

The current implementation is already a substantial Windows preview: Avalonia owns the UI thread,
both SharpTS execution modes use hosted scheduling, GUI API 2 provides a retained TSX renderer and
component hooks, and the SDK can produce a Windows x64 self-contained single-file application.
The remaining work is principally integration, lifecycle correctness, contract generation,
tooling, release closure, and ecosystem depth.

## Scope decisions

- Windows is the active product target. A Windows preview does not wait for macOS.
- Managed, self-contained publishing remains the release path. Native AOT is a later gate.
- Hosted ABI 1 remains the baseline unless a required semantic change cannot be made compatibly.
- GUI API 2 is the only active GUI API. API 1 is historical and must not appear as a current
  release target.
- The SDK remains the canonical build and publish implementation. Future CLI commands must invoke
  it rather than reconstruct NuGet or MSBuild behavior.
- One runtime currently owns one dispatcher and one `Window` root. Multi-window support is a
  deliberate later API change, not an accidental extension of the current contract.

## Status summary

| Phase | Current assessment | Completion target |
| --- | --- | --- |
| Phase 0 | Windows feasibility passed; original macOS gate was not run | Reconcile the original cross-platform criterion with the Windows-only roadmap and preserve macOS as an explicit deferred gate |
| Phase 1A | Hosted interpreter scheduling is implemented | Close normal-window lifecycle, top-level-await, tracing, and conformance gaps |
| Phase 1B | Compiled hosted ABI and scheduler are implemented | Match the completed interpreter contract and stabilize the ABI/tool terminology |
| Phase 2A | Complete | No additional phase work; regression coverage only |
| Phase 2B | Broad typed surface exists, but generation/LSP/performance work is incomplete | Establish one generated contract and finish diagnostics, recovery, and quality gates |
| Phase 3A | SDK build/run/publish works | Add templates, documentation, install-location robustness, and complete SDK ergonomics |
| Phase 3B | Windows SDK publishing works; TypeScript-only CLI and release publication do not | Deliver the CLI front door and close Windows preview publication gates |
| Phase 4 | Selected services landed early; most ecosystem/AOT work remains | Stabilize and expand the platform after the public preview |

## Prerequisite integration gate

The GUI work must first be integrated with current `main`. At the time of this plan, the GUI branch
is two commits ahead of its merge base and 302 commits behind `main`. Several changed files overlap
recent interpreter and promise work, so a green build on the isolated branch is not sufficient
release evidence.

### Work

1. Rebase or merge `codex/avalonia-phase-0` onto current `main` without dropping either branch's
   behavioral fixes.
2. Review conflicts in interpreter scheduling, promise dispatch, property access, compiler entry
   points, and shared tests semantically rather than selecting one side mechanically.
3. Ensure the GUI projects, package assets, workflow, examples, and documentation are present in
   the integrated tree with durable paths.
4. Run `git diff --check`, a Release solution build, the complete core test suite, GUI conformance,
   hosted-runtime conformance, JSX diagnostics, SDK task tests, and packaged-consumer tests.
5. Record the integrated commit and exact test counts in `docs/gui/desktop-status.md`.

### Exit criterion

The GUI implementation is based on current `main`; the worktree is clean; Release builds with no
warnings; all core and GUI suites pass; and the evidence record identifies the exact integrated
commit. No later phase may claim completion against the pre-integration branch.

## Phase 0 residual: scope and platform feasibility record

The Windows x64 feasibility spike passed in interpreted and compiled modes. The original Phase 0
exit criterion also required a real-window macOS run, but the product roadmap now explicitly
defers macOS. This phase needs a documentation decision, not speculative macOS code.

### Work

1. Mark the original Phase 0 result as **Windows complete, cross-platform incomplete**.
2. Move every unrun macOS gate to the Phase 4 macOS reactivation track without presenting it as
   current compatibility.
3. Retain the Windows feasibility evidence for dispatcher ownership, timer delivery, asynchronous
   continuation, input, rendering, and shutdown in both execution modes.
4. Ensure current documents do not alternate between “Windows preview complete” and “Windows and
   macOS Phase 0 complete.”

### Exit criterion

The engineering record accurately distinguishes proven Windows behavior from deferred macOS work,
and no release or status document claims unrecorded platform evidence.

## Phase 1A residual: hosted interpreter lifecycle and semantics

### 1A.1 Normal window-close lifecycle

The normal `Window.Closed` path currently shuts down the Avalonia lifetime directly, while the
automated close path awaits the guest runtime's `ShutdownAsync`. Direct runtime disposal does not
deliver the complete hosted shutdown sequence.

#### Work

1. Introduce a single host shutdown coordinator used by normal window close, automated close,
   startup failure, fatal guest error, and explicit host shutdown.
2. Handle `Window.Closing` so a cancelled close neither begins guest shutdown nor loses the active
   runtime.
3. On accepted normal close, await hosted shutdown before ending the Avalonia lifetime.
4. Preserve the required order: `beforeExit`, complete microtask checkpoint, reverse registered
   cleanup, `exit`, host exit request, runtime disposal.
5. Make repeated close/shutdown requests idempotent and ignore callbacks arriving after shutdown
   begins.
6. Add real-host tests for normal close, cancelled close, repeated close, close during
   initialization, and close after a queued timer or off-thread notification.

#### Exit criterion

Normal and automated close produce the same lifecycle trace in interpreted mode. Cancelled close
keeps the window and runtime usable. No close path bypasses registered cleanup or process lifecycle
events.

### 1A.2 Production tracing and diagnostics

#### Work

1. Make detailed conformance tracing opt-in for ordinary applications.
2. When tracing is requested without an explicit path, write under a user-writable diagnostics
   directory such as `%LOCALAPPDATA%\SharpTS.Gui`, not beside the executable.
3. Treat trace-write failures as diagnostics rather than application failures during otherwise
   successful shutdown.
4. Keep fatal-error logs separate from conformance traces and apply a bounded retention policy.
5. Add tests that run from a read-only application directory and with an unwritable explicit trace
   path.

#### Exit criterion

A published application can start and close successfully from a read-only install directory, and
production execution does not create a trace unless requested.

### 1A.3 Complete interpreted top-level-await contract

The hosted interpreter currently supports prepared static graphs but rejects some top-level-await
shapes.

#### Work

1. Support awaited values nested in compound expressions, conditionals, and loop bodies without a
   synchronous promise pump.
2. Define and implement hosted dynamic-import initialization and rejection behavior.
3. Preserve dependency evaluation order, cycle handling, error attribution, and one complete
   microtask checkpoint at every module-job boundary.
4. Add cancellation/shutdown behavior for suspended module initialization.
5. Extend trace conformance to cover successful and rejected awaits in every supported shape.

#### Exit criterion

Hosted interpreted modules no longer reject supported JavaScript top-level-await syntax merely
because of expression shape, and the deterministic host plus Avalonia Headless pass the expanded
module-job suite without `WaitForPromise`, nested dispatchers, polling, or synchronization-context
replacement.

## Phase 1B residual: compiled hosted parity and ABI stabilization

### Work

1. Implement compiled parity for every top-level-await shape accepted by Phase 1A, including
   dynamic import if it is accepted for the preview contract.
2. Run one trace-based suite against interpreted and compiled runtimes for initialization,
   callbacks, timers, microtasks, off-thread delivery, normal close, cancelled close, forced exit,
   startup failure, and cleanup.
3. Replace “hosted prototype” and “experimental prototype” wording where the code now implements
   the versioned ABI. Keep the .NET experimental annotation while the public contract is preview.
4. Define compatibility rules for emitted Hosted ABI, GUI API, SDK package, application manifest,
   and generated descriptor schema versions.
5. Verify that an ABI mismatch fails before guest initialization and produces a durable diagnostic
   in console, Headless, and Windows-subsystem applications.
6. Decide whether Hosted ABI 1 can absorb the completed lifecycle and top-level-await semantics. If
   not, document and test an ABI 2 migration rather than silently changing ABI 1 behavior.

### Exit criterion

Interpreted and compiled guests produce equivalent observable traces for the complete hosted
contract. Persisted assemblies fail fast on incompatible hosts, and public/internal terminology no
longer describes the production SDK path as an ad hoc prototype.

## Phase 2B residual: generated surface, typing, and renderer stabilization

### 2B.1 One generated control contract

The current C# descriptor registry and TypeScript component/prop switch are maintained separately.

#### Work

1. Define a versioned control-manifest format covering:
   - control kind and native Avalonia type;
   - child strategy and arity;
   - props, defaults, converters, setters, and attached properties;
   - events, normalized payloads, and synchronous-return policy;
   - ref handle type and supported operations;
   - trimming/AOT annotations and documentation.
2. Generate the C# descriptor registry, TypeScript components and props, declaration/docs metadata,
   and a deterministic descriptor schema hash from that manifest.
3. Put the schema version/hash in the packaged runtime and application manifest and validate it at
   host startup.
4. Add a generator determinism test and fail the build when generated files are stale.
5. Define a future extension boundary for third-party controls without opening unrestricted
   reflection in the built-in path.

#### Exit criterion

One reviewed manifest is the source of truth for the C# and TypeScript surfaces; drift is detected
at build time and incompatible packaged surfaces fail before mounting.

### 2B.2 JSX checker completion

#### Work

1. Implement the remaining standard JSX contracts for class components, including
   `JSX.ElementClass` and `JSX.ElementAttributesProperty`.
2. Complete constituent-aware union diagnostics and the selected generic inference behavior.
3. Verify `JSX.ElementChildrenAttribute`, `IntrinsicAttributes`, typed `ref`, keyed generic
   components, callable objects, overloads, and exact child arity against focused positive and
   negative fixtures.
4. Produce source-located diagnostics for unsupported asynchronous components and invalid native
   child models.

#### Exit criterion

The GUI declaration fixtures cover the supported TypeScript JSX contracts without silent
`any`/unknown fallbacks for supported component shapes, and invalid children, refs, keys, and props
fail at the JSX use site.

### 2B.3 Commit failure and error-boundary recovery

#### Work

1. Define whether an error boundary may catch a failure thrown after an Avalonia setter, ref, or
   child mutation has begun.
2. If boundaries catch native-commit failures, connect the managed commit error to the owning
   logical boundary and render its fallback only after the previous native state is restored.
3. If recovery cannot restore the previous tree, dispose the damaged root and report the original
   and rollback failures together.
4. Test property-setter failure, ref attach/detach failure, child-collection failure, fallback
   failure, and repeated reset in both execution modes.

#### Exit criterion

The documented error-boundary behavior matches native commit behavior, and no failure path leaves
an apparently live but structurally damaged root.

### 2B.4 Tooling, performance, and leak gates

#### Work

1. Teach the language server to discover the SDK-materialized `@sharpts/gui` package and surface
   control, prop, event, ref, attached-property, and source documentation.
2. Add completion/navigation tests for generated declarations.
3. Establish benchmarks for cold start, initial mount, scalar update, batched update, keyed
   insertion/move/removal, allocation, and input-to-render latency.
4. Compare against Avalonia code-only setters and a representative compiled-XAML application.
5. Add leak/soak tests for roots, controls, guest callbacks, event subscriptions, refs, effects,
   timers, and repeated mount/unmount cycles.

#### Exit criterion

LSP discovery works from an isolated SDK consumer; benchmark results are recorded; scalar updates
do not reconcile unrelated subtrees; and soak tests show no unbounded retained roots, controls, or
guest callbacks.

## Phase 3A residual: dedicated SDK development workflow

### Work

1. Ship a `dotnet new sharpts-gui` template containing a minimal TSX application, project file,
   tsconfig, assets example, Headless test, and package-version pin.
2. Add template creation/build/run/test/clean/publish tests from an isolated directory, including a
   path containing spaces.
3. Make framework-dependent interpreted and compiled development output work without requiring
   repository-local scripts.
4. Verify incremental build inputs include imported TS/TSX sources, generated package files,
   assets, config inheritance, and relevant SDK properties.
5. Add root README and SDK documentation that make the GUI preview discoverable and clearly state
   supported Windows RIDs, execution modes, package size, and preview limitations.
6. Document raw Avalonia interop, its threading rules, and why custom controls are not yet a stable
   public extension surface.
7. Add an SDK option to disable source payloads in compiled framework-dependent distributions when
   interpreted fallback is not desired.

### Exit criterion

A new user can create, restore, type-check, test, run, clean, build, and framework-dependently
publish a Windows GUI application using ordinary `dotnet` commands, without repository scripts,
C#, or AXAML. The same project works from a read-only install location after publication.

## Phase 3B residual: TypeScript-only CLI and Windows preview release

### 3B.1 TypeScript-only CLI front door

#### Work

1. Add `sharpts new avalonia -n <name>` with an explicit application-host marker and compatible
   pinned GUI SDK/package versions.
2. Define extensible `application.type` or equivalent manifest syntax and precedence:
   explicit CLI option, project manifest, safe inference, then console default.
3. Add an explicit Avalonia/console host override and diagnostics for ambiguous or false-positive
   inference.
4. Generate/cache a deterministic internal SDK project for TypeScript-only build and publish.
5. Route that project through `SharpTS.Gui.Sdk`; do not duplicate restore, native-asset, launcher,
   or publish logic in the CLI.
6. Add CLI options for `--rid`, `--self-contained`, and `--single-file`, keeping executable target,
   bundling, self-contained deployment, and Native AOT as distinct concepts.
7. Remove the user-facing limitation that hosted output is DLL-only by making the CLI's desktop EXE
   path generate the SDK-backed host plus hosted guest assembly.
8. Compare SDK and CLI outputs for manifest, guest behavior, app host, managed/native/content
   closure, and diagnostics.

#### Exit criterion

A TypeScript-only user can create, run, compile, and self-contained-publish a Windows x64 desktop
application using only `sharpts` commands and without authoring a `.csproj`. CLI and direct SDK
front doors use the same implementation and pass artifact-parity tests.

### 3B.2 Windows preview publication

#### Work

1. Reserve/onboard the `SharpTS.Gui.Sdk` NuGet ID and separately verify release API-key scope.
2. Run native Windows ARM64 Headless and real-window scenarios; cross-publish alone does not close
   this gate.
3. Re-run packaged x64 and ARM64 restore/build/no-op/clean/publish/launch gates against the exact
   candidate package.
4. Validate x64 self-contained output on a clean Windows environment without a usable installed
   .NET runtime.
5. Complete the full Release solution build/test run and retain exact counts and traces.
6. Make `docs/gui/desktop-status.md`, SDK readme, examples, migration guide, package manifest, and
   workflow agree on GUI API 2 and the exact preview version.
7. Inspect package contents and hashes, then remove `publish: false` only in a dedicated release
   change after every prior gate passes.
8. Publish once, verify the public package, and retain the package hash, workflow URLs, x64/ARM64
   traces, and NuGet result as the release record.

#### Exit criterion

The exact GUI API 2 preview package is publicly restorable; Windows x64 and ARM64 have native
execution evidence; SDK and CLI consumers launch from clean environments; documentation matches
the artifact; and the release record is reproducible.

## Phase 4: ecosystem depth, distribution, and deferred platforms

Phase 4 is not one release gate. Work should be selected in independent tracks after the preview
contract stabilizes.

### Track A: application and renderer APIs

- Replace the single-root `renderDesktop` contract with an explicit multi-window application API.
- Define window ownership, activation, shutdown modes, modal relationships, and per-window error
  isolation.
- Add public Avalonia-native styles, resources, themes, selectors, templates, and resource lookup.
- Add generic item sources/templates, virtualization-preserving list controls, trees, data grids,
  rich text, drawing/canvas, and custom rendering.
- Add a reviewed third-party/custom-control registration and packaging contract.

### Track B: desktop services and developer experience

- Expand menus, dialogs, clipboard, drag/drop, file associations, notifications, system tray,
  printing, and platform services.
- Add interpreted remount/hot reload with explicit state-preservation rules.
- Add a component inspector and Headless visual-regression workflow.
- Complete accessibility, keyboard navigation, focus, IME, high-DPI, multiple-monitor, and theme
  conformance.

### Track C: optimization and Native AOT

- Remove runtime reflection from the supported GUI control and callback path.
- Close the custom-control registration set for trimming/AOT analysis.
- Add trimming annotations and publish tests for every supported descriptor and service.
- Establish startup, working-set, allocation, throughput, and artifact-size budgets.
- Certify Native AOT only after Windows x64 and ARM64 publish/run tests pass with the complete
  supported surface.

### Track D: distribution

- Add code signing, artifact provenance/SBOM, installer packaging, update strategy, crash/support
  diagnostics, and enterprise deployment guidance.
- Define package and application support/versioning policy before a stable 1.0 release.

### Track E: macOS reactivation

This track remains unscheduled until explicitly selected.

- Add intentional `osx-x64` and `osx-arm64` payloads and a macOS diagnostics adapter.
- Prove Headless and real-window interpreted/compiled behavior natively on both architectures.
- Validate `.app` structure and metadata.
- Add signing, notarization, and distribution gates without weakening Windows packaging rules.

### Phase 4 exit criterion

Each selected track defines and passes its own compatibility, native execution, performance, and
release evidence. Phase 4 is not declared complete merely because isolated controls or services
land; a stable release requires an explicit supported-surface and distribution decision.

## Verification matrix

Every phase should add the narrowest reliable tests locally and then pass the cumulative gates
below before its exit criterion is accepted.

| Gate | Interpreter | Compiled | Headless | Real window | Packaged |
| --- | ---: | ---: | ---: | ---: | ---: |
| Hosted scheduling/lifecycle | Required | Required | Required | Required for close/lifetime changes | No |
| Top-level await/module jobs | Required | Required | Required | Smoke | No |
| Renderer/refs/events/effects | Required | Required | Required | Representative smoke | No |
| JSX and generated declarations | Required | Required | N/A | N/A | Isolated SDK consumer |
| SDK build/run/clean/publish | Required | Required | Required | Required on supported native RIDs | Required |
| CLI artifact parity | Required | Required | Required | Required on supported native RIDs | Required |
| Release closure | Development only | Required for self-contained single file | Required | Required | Clean environment |

## Recommended execution order

1. Complete the prerequisite integration gate.
2. Fix Phase 1A normal-close lifecycle and production trace behavior.
3. Complete Phase 1A/1B top-level-await and trace parity.
4. Make GUI API 2 documentation and version metadata internally consistent.
5. Complete Phase 2B generation, JSX, commit recovery, LSP, benchmark, and leak gates.
6. Finish Phase 3A templates and SDK ergonomics.
7. Implement the Phase 3B TypeScript-only CLI through the SDK.
8. Close native ARM64 and NuGet publication gates and publish the Windows preview.
9. Select Phase 4 tracks based on preview feedback; do not implicitly expand the supported surface.

## Completion record policy

For every completed item, update `docs/gui/desktop-status.md` with:

- the exact commit and package version;
- commands and test counts;
- native architecture and whether evidence is build-only, Headless, or real-window;
- artifact hashes and sizes for release gates;
- explicitly skipped or deferred work;
- any changed decision and its compatibility impact.

Historical API 1 evidence should remain clearly labeled as historical. Current status, current
release gates, and current package metadata must refer only to GUI API 2 until another deliberate
version transition is made.
