# macOS GUI preview and distribution

SharpTS.Gui.Sdk contains intentional `osx-x64` and `osx-arm64` payloads. Both RIDs can be
cross-published from Windows, but macOS is a candidate platform until the native workflow has
recorded Headless and real-window traces on both architectures. Cross-publish and bundle
inspection do not establish runtime compatibility.

## Build and bundle

Create a compiled, self-contained Mach-O executable with either the projectless CLI or the SDK:

```bash
sharpts app publish --rid osx-arm64 --self-contained true --single-file true
# or
dotnet publish -c Release -r osx-arm64
```

The SDK also accepts `osx-x64`. A RID publish defaults to a compiled-only single file; use
`-p:SharpTSGuiPublishMode=Directory --self-contained false` when interpreted and compiled modes
must remain available together.

`package-gui-macos.ps1` validates the Mach-O architecture of the executable and every published
`.dylib`, removes symbol sidecars, emits an `.app` with a validated `Info.plist`, and records a
SHA-256 inventory. `-StageOnly` works cross-platform for structural inspection:

```powershell
scripts/package-gui-macos.ps1 `
  -PublishDirectory artifacts/publish `
  -OutputDirectory artifacts/macos-bundle `
  -BundleIdentifier dev.example.counter `
  -DisplayName "Counter" `
  -ShortVersion 0.2.0 `
  -BuildVersion 1 `
  -Architecture arm64 `
  -Executable Counter `
  -StageOnly
```

A distributable ZIP or DMG must be created on macOS. The packager refuses to silently downgrade
`-RequireSigned` or `-RequireNotarized`: both require the protected Developer ID and App Store
Connect notarization configuration.

## Native and release gates

[`macos-desktop-preview.yml`](../../.github/workflows/macos-desktop-preview.yml) builds one exact
SDK package, then runs the packaged SDK and TypeScript-only CLI on native Apple Silicon and Intel
runners. Each architecture must pass interpreted and compiled Headless runs, automatic
real-window launch/close, asset-closure parity, single-file execution, Mach-O validation, and
unsigned `.app`/ZIP creation.

[`macos-gui-distribution.yml`](../../.github/workflows/macos-gui-distribution.yml) is a manual,
protected-environment ceremony. It requires these secrets:

- `MACOS_DEVELOPER_ID_P12_BASE64` and `MACOS_DEVELOPER_ID_P12_PASSWORD`
- `MACOS_DEVELOPER_ID_APPLICATION`
- `MACOS_NOTARY_KEY_BASE64`, `MACOS_NOTARY_KEY_ID`, and `MACOS_NOTARY_ISSUER_ID`

For each architecture it imports the certificate into an ephemeral keychain, executes the exact
candidate natively, applies hardened-runtime signing, submits and staples both the app archive and
DMG, validates the stapled ticket, emits checksums, and creates GitHub provenance attestations.
Signing material is removed in an unconditional cleanup step.

## Diagnostics and current evidence

On macOS, default fatal logs are retained under
`~/Library/Logs/SharpTS.Gui`; traces use
`~/Library/Application Support/SharpTS.Gui/Traces`. Interactive fatal errors use the native macOS
alert path after the durable log is written.

On 2026-08-09, the current `SharpTS.Gui.Sdk.0.3.0-preview.1.nupkg` candidate (39,745,308 bytes, SHA-256
`1C0579F836C58A10895EB23227D936E2162A5B93D01CB0A64ED3C6434D5B3E5F`) passed package audit and
SDK/CLI cross-publish for both macOS RIDs. The resulting x64 and ARM64 Mach-O executables passed
stage-only `.app`, plist, architecture, symbol, and checksum validation. No native macOS runner,
Apple signing identity, or notarization credential was available locally, so the two workflows
remain required before macOS can become a supported platform.
