# macOS GUI distribution

SharpTS.Gui.Sdk contains an intentional `osx-arm64` payload for Apple Silicon. It can be
cross-published from Windows, but macOS is a candidate platform until the native workflow has
recorded Headless and real-window traces. Cross-publish and bundle inspection do not establish
runtime compatibility. macOS Intel is not supported. See the canonical
[platform status](README.md#platform-status) for the current support designation.

## Build and bundle

Create a compiled, self-contained Mach-O executable with either the projectless CLI or the SDK:

```bash
sharpts app publish --rid osx-arm64 --self-contained true --single-file true
```

Or use an explicit SDK project:

```bash
dotnet publish -c Release -r osx-arm64
```

A RID publish defaults to a compiled-only single file; use
`-p:SharpTSGuiPublishMode=Directory --self-contained false` when interpreted and compiled modes
must remain available together.

`package-gui-macos.ps1` validates the Mach-O architecture of the executable and every published
`.dylib`, removes symbol sidecars, emits an `.app` with a validated `Info.plist`, and records a
SHA-256 inventory. `-StageOnly` works cross-platform for structural inspection:

```powershell
./scripts/package-gui-macos.ps1 `
  -PublishDirectory artifacts/publish `
  -OutputDirectory artifacts/macos-bundle `
  -BundleIdentifier dev.example.counter `
  -DisplayName "Counter" `
  -ShortVersion 0.3.0 `
  -BuildVersion 1 `
  -Architecture arm64 `
  -Executable Counter `
  -StageOnly
```

A distributable ZIP or DMG must be created on macOS. The packager refuses to silently downgrade
`-RequireSigned` or `-RequireNotarized`: both require the protected Developer ID and App Store
Connect notarization configuration.

## Native and release gates

[`desktop-gui.yml`](../../.github/workflows/desktop-gui.yml) builds one exact
SDK package, then runs the packaged SDK and TypeScript-only CLI on native Apple Silicon. The
candidate must pass interpreted and compiled Headless runs, automatic
real-window launch/close, asset-closure parity, single-file execution, Mach-O validation, and
unsigned `.app`/ZIP creation.

[`macos-gui-distribution.yml`](../../.github/workflows/macos-gui-distribution.yml) is the manual
SharpTS certification caller. It builds and executes the native candidate, uploads its already-
published files under `publish/`, and calls
[`reusable-macos-gui-distribution.yml`](../../.github/workflows/reusable-macos-gui-distribution.yml).
The reusable workflow never rebuilds the input artifact. It requires these protected-environment
secrets:

- `MACOS_DEVELOPER_ID_P12_BASE64` and `MACOS_DEVELOPER_ID_P12_PASSWORD`
- `MACOS_DEVELOPER_ID_APPLICATION`
- `MACOS_NOTARY_KEY_BASE64`, `MACOS_NOTARY_KEY_ID`, and `MACOS_NOTARY_ISSUER_ID`

It imports the certificate into an ephemeral keychain, applies hardened-runtime signing, submits
and staples both the app archive and DMG, validates the stapled ticket, emits checksums, and creates
GitHub provenance attestations. Signing material is removed in an unconditional cleanup step.
Other applications may call it after uploading a `publish/` artifact and supplying the executable
file name, bundle identity, display name, versions, and architecture.

## Diagnostics

On macOS, default fatal logs are retained under
`~/Library/Logs/SharpTS.Gui`; traces use
`~/Library/Application Support/SharpTS.Gui/Traces`. Interactive fatal errors use the native macOS
alert path after the durable log is written.

Candidate-specific package sizes, checksums, and pins belong in release metadata and evidence
artifacts. Native execution, signing, and notarization remain required before Apple Silicon macOS
can become a supported platform.
