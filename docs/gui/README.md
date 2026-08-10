# SharpTS GUI

SharpTS GUI is the retained TypeScript/TSX desktop application layer built on Avalonia. It provides
explicit application/window lifecycle, interpreted and compiled guests, Headless execution,
development reloads, Windows packaging, and compiled single-file and Native AOT deployment.

Preview identifies the release channel, not a frozen package number. Windows is the supported
product focus; Apple Silicon macOS remains an experimental candidate until its native and Apple
distribution gates pass.

## Build an application

1. Follow the [SDK development workflow](sdk-development.md) for CLI and explicit MSBuild projects.
2. Use the [TSX API reference](tsx-api.md) for lifecycle, components, built-in controls, resources,
   assets, and desktop services.
3. Use [testing and developer tools](testing-and-devtools.md) for Headless interaction tests, tree
   inspection, and visual snapshots.

`@sharpts/gui` is the public application API. `@sharpts/gui/testing` is the public Headless test
driver, and `@sharpts/gui/devtools` supplies read-only inspection and pixel snapshots. Repository
conformance hooks are not public application APIs.

There is no supported public third-party custom-control provider, descriptor-registration, raw
Avalonia object, or dynamic control-loading API. Internal provider seams are private, can change
without notice, and carry no compatibility promise. Public applications extend behavior through
components, hooks, typed item factories, resources/styles, assets, drawing commands, and the
documented desktop services.

## Platform status

| Target | Status |
| --- | --- |
| `win-x64` | Supported preview target; releases require Headless, real-window, packaged, single-file, and Native AOT evidence. |
| `win-arm64` | Supported preview RID for cross-publishing; native execution remains a certification requirement. |
| `osx-arm64` | Experimental candidate; native execution, Developer ID signing, and notarization are required before support. |

macOS Intel is not supported. Cross-publishing alone never changes a platform designation.

## Versioned public boundaries

The SDK is an atomic distribution of its compiler, host, bridge, TypeScript modules, native assets,
launcher, and templates. Hosted ABI, GUI API, and descriptor schema values are checked before guest
initialization. Incompatible payloads fail fast instead of loading a historical compatibility path.

Each window has one `Window` root. Applications use the built-in generated descriptors. Arbitrary
Avalonia templates, dynamic descriptor discovery, public third-party native controls, and a full
editing DataGrid are outside the public surface.

## Maintainer documentation

- [Performance and retention](performance.md) — benchmark suite and release budgets
- [Windows distribution](windows-distribution.md) — MSIX, signing, updates, enterprise deployment,
  provenance, and support bundles
- [macOS distribution](macos-distribution.md) — experimental bundle construction, native
  certification, signing, and notarization
- [Compatibility and support policy](support-policy.md) — versioned contracts, servicing boundaries,
  and the stable-release gate

GUI changes should pass the core and GUI suites, generated-contract verification,
SDK/CLI/template package lifecycles, distribution checks, native evidence for claimed platforms,
package-content audit, and `git diff --check`.
