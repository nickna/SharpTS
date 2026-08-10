# SharpTS GUI

SharpTS GUI is the retained TypeScript/TSX desktop application layer built on Avalonia. The
current `SharpTS.Gui.Sdk` `0.3.0-preview.1` train supports explicit application and window
lifecycle management, interpreted and compiled guests, Headless execution, hot reload, Windows
packaging, and compiled single-file and Native AOT deployment.

Preview identifies the release channel and compatibility policy. The SDK, workflows, artifact
names, and package train are maintained product infrastructure until an explicit lifecycle change
moves them to a stable channel.

## Build an application

1. Use the [SDK development workflow](sdk-development.md) to create, run, test, and publish an
   application with either the SharpTS CLI or the .NET SDK template.
2. Use the [TSX API reference](tsx-api.md) for application lifecycle, components, controls,
   resources, assets, and desktop services.
3. Use [testing and developer tools](testing-and-devtools.md) for Headless interaction tests,
   structural inspection, and visual regression snapshots.

`@sharpts/gui` is the application API. The supported `@sharpts/gui/testing` subpath provides a
window-scoped Headless test driver, while `@sharpts/gui/devtools` provides read-only inspection and
pixel snapshots. Fault injection, scheduler manipulation, trace staging, renderer identity, and
subscription counters are repository-only conformance infrastructure.

## Maintain and release the GUI

- [Performance and retention](performance.md) documents the benchmark suite, release budgets, and
  dated measurement evidence.
- [Windows distribution](windows-distribution.md) covers MSIX identity, signing, updates,
  enterprise deployment, provenance, and support bundles.
- [macOS distribution](macos-distribution.md) covers the experimental Apple Silicon candidate,
  native certification, signing, and notarization.
- [Compatibility and support policy](support-policy.md) defines versioned contracts, servicing
  boundaries, and the stable-release gate.

## Platform status

| Target | Status |
| --- | --- |
| `win-x64` | Supported preview target. Release evidence requires Headless, real-window, packaged, single-file, and Native AOT execution. |
| `win-arm64` | Supported preview RID for cross-publishing. Native ARM64 execution remains a certification requirement. |
| `osx-arm64` | Experimental Apple Silicon candidate. Cross-publishing does not establish runtime support; native execution, signing, and notarization remain required. |

macOS Intel is not supported. Windows remains the supported product focus, and the experimental
macOS candidate does not block a Windows release.

## Versioned boundaries

The SDK package is an atomic distribution of the matching compiler, host, GUI bridge, MSBuild
tasks, TypeScript modules, native assets, launcher, and templates. Hosted ABI 1, GUI API 1, the
descriptor schema version and hash, and custom-provider contract 1 are checked before guest
initialization. Incompatible payloads fail before application code runs; the preview line does not
load a historical GUI compatibility path.

Each window has one `Window` root. Applications use built-in or statically registered descriptors;
runtime descriptor discovery, arbitrary Avalonia templates, public third-party control loading,
and a full editing `DataGrid` are not supported. Simple `ComboBox` and `ListBox` items remain
string-backed, with typed factories available for virtual lists, trees, and a windowed virtual
grid. Installed MSIX identity is required for Windows notifications.

## Repository release checks

GUI changes should pass a warning-clean Release solution build, the canonical core and GUI suites,
generated-contract verification, SDK/CLI/template package lifecycles, distribution checks, x64
real-window and Native AOT execution, ARM64 cross-publishing, macOS candidate structure checks,
package-content audit, and `git diff --check`.

Publishing additionally requires approved NuGet credentials, a production Windows signing
identity, immutable package bytes, and the native hardware evidence claimed by the release notes.
Platform-specific release requirements are documented in the distribution guides above.
