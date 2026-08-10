# SharpTS GUI desktop status

## Current preview

The source tree targets `SharpTS.Gui.Sdk` `0.3.0-preview.1` as the next Windows desktop preview. It
provides retained TSX over Avalonia, interpreted and compiled hosted guests, Headless execution,
multi-window application sessions, hot reload, devtools, Windows packaging, and compiled
single-file and Native AOT paths.

“Preview” is a release-channel and compatibility designation, not a marker for disposable build
code. The desktop-preview workflows, artifact names, and preview package train are maintained
product infrastructure for as long as that channel exists. They should be renamed only as part of
an explicit lifecycle transition to a stable channel, not removed as cleanup.

`createDesktopApplication` is the lifecycle API. It owns explicit windows, trays, resources,
styles, and shutdown policy.

| Target | Status |
| --- | --- |
| `win-x64` | Supported preview target; Headless, real-window, packaged, single-file, and Native AOT gates are required for release. |
| `win-arm64` | Supported preview RID; cross-publish is automated and native ARM64 execution remains a release-evidence gate. |
| `osx-arm64` | Experimental Apple Silicon package candidate only. Cross-publish does not establish support; native execution, signing, and notarization remain deferred. |

## Contracts and package boundaries

- Hosted ABI 1, GUI API 1, descriptor schema 1/hash, and custom-provider contract 1 are checked
  before guest initialization. A mismatch fails fast; no historical GUI compatibility path runs.
- `@sharpts/gui` is the application API, `@sharpts/gui/devtools` provides inspection and Headless
  snapshots, and `@sharpts/gui/testing` provides window-scoped supported Headless interaction.
- Fault injection, scheduler manipulation, trace staging, renderer identity, and subscription
  counters belong to the non-packaged repository conformance support.
- The NuGet package is an atomic SDK distribution containing the matching compiler, host, GUI
  bridge, MSBuild tasks, TypeScript module, native assets, launcher, and templates.

## Current boundaries

- Windows is the supported product focus. The Apple Silicon macOS asset is experimental and does not block Windows; macOS Intel is not supported.
- Each window has one `Window` root. Custom controls are statically registered; runtime descriptor
  discovery and arbitrary Avalonia templates are not supported.
- Simple string-backed `ComboBox` and `ListBox` items remain alongside typed item-template helpers.
  A full editing `DataGrid` is not included.
- Installed MSIX identity is required for Windows notifications. Unpackaged calls fail explicitly.
- Hosting APIs remain marked experimental for the preview line.

## PR and release gates

Before merging or publishing, require a warning-clean Release solution build, the canonical core and
GUI suites, generated-contract verification, SDK/CLI/template package lifecycles, Windows
distribution checks, x64 real-window and Native AOT execution, ARM64 cross-publish, macOS candidate
cross-publish/structure checks, package-content audit, and `git diff --check`.

Publishing additionally requires the approved NuGet credentials, production Windows signing
identity, immutable `0.3.0-preview.1` package bytes, and the native hardware evidence claimed by the
release notes. See [SDK development](sdk-development.md), [Windows distribution](windows-distribution.md),
[macOS candidate distribution](macos-distribution.md), and [support policy](support-policy.md).
