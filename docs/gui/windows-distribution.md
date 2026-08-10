# Windows GUI distribution

SharpTS GUI applications use MSIX as the supported Windows installer identity. Packaging remains a
layer above `dotnet publish`: the renderer and Hosted ABI do not know about certificates, update
feeds, or enterprise deployment. The package identity and certificate publisher become immutable
once an application is distributed because Windows uses that pair for upgrades, data ownership,
and notification identity.

That package identity is also the prerequisite for `showNotification`. The preview notification
API submits informational `ToastGeneric` content through the inbox Windows Runtime notification
interfaces; it does not register a COM activator or expose click/actions callbacks. Consequently
the manifest needs no notification-specific capability or activation extension. An unpackaged
executable is rejected before native notification activation rather than being assigned an
unstable application identifier.

## Build and package

Publish a compiled application first, preferably with the warning-clean Native AOT profile:

```powershell
dotnet publish App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishAot=true -o artifacts/publish
```

Prepare application-owned PNG assets named `Square44x44Logo.png`, `Square150x150Logo.png`, and
`StoreLogo.png`, then create the installer and release evidence:

```powershell
.\scripts\package-gui-windows.ps1 `
  -PublishDirectory artifacts/publish `
  -OutputDirectory artifacts/distribution `
  -PackageIdentity Contoso.Product `
  -Publisher 'CN=Contoso Software, O=Contoso Software, C=US' `
  -PublisherDisplayName 'Contoso Software' `
  -DisplayName 'Contoso Product' `
  -Description 'Contoso Product desktop application' `
  -Version 1.2.3.0 `
  -Architecture x64 `
  -Executable Product.exe `
  -AssetsDirectory packaging-assets `
  -PackageUri https://downloads.contoso.example/stable/Contoso-Product.msix `
  -CertificateThumbprint $thumbprint `
  -RequireSigned
```

The packager stages a symbol-free payload, emits and validates `AppxManifest.xml`, signs payload
binaries, builds and signs the MSIX with Windows SDK tools, verifies Authenticode, and writes:

- the `.msix` installer and optional `.appinstaller` update descriptor;
- an SPDX 2.3 file-level SBOM;
- an in-toto statement with a SLSA provenance v1 predicate;
- `SHA256SUMS` covering the release evidence.

`-StageOnly` validates identity, assets, manifest, SBOM, provenance, and update metadata without
requiring the Windows SDK. It is useful in pull requests but is not release evidence. A release
must use `-RequireSigned`; an unsigned MSIX is only a development artifact.

The protected `windows-gui-distribution.yml` workflow imports a production PFX into the ephemeral
runner, packages the already-tested x64 Native AOT consumer, removes the certificate in an
`always()` step, and publishes GitHub Sigstore provenance and SBOM attestations. Production secrets
belong in the `windows-gui-distribution` environment and must never be printed or committed.

Verify downloaded artifacts independently:

```powershell
Get-FileHash .\Product.msix -Algorithm SHA256
signtool verify /pa /v .\Product.msix
gh attestation verify .\Product.msix --owner nickna
```

## Updates and rollback

The supported updater is Windows App Installer, not an in-process updater. A channel has a stable
HTTPS `.appinstaller` URI and immutable MSIX artifacts. Stable, preview, and internal rings use
different package identities so a machine can hold them side-by-side and a preview cannot replace
a stable install.

- Every release increases the four-part MSIX version; never reuse a version for different bytes.
- Publish the versioned MSIX first, verify its hash/signature/attestation, then atomically replace
  the `.appinstaller` document.
- Keep at least the previous two installers available for incident analysis.
- Rollback by shipping the last known-good source as a **new, higher** version. Normal MSIX update
  policy does not downgrade an installed package.
- Security revocation or forced-update behavior is an application-owner decision and must be
  documented before enabling update activation blocking.

## Enterprise deployment

Enterprises can deploy the signed MSIX and its certificate chain with Intune, Configuration
Manager, DISM/provisioned packages, or their existing application-management product. Administrators
should pin the package identity, publisher subject, SHA-256, architecture, minimum Windows version,
and update channel. Offline deployments distribute the MSIX plus trusted certificate chain and do
not use the public AppInstaller URI. Per-machine policy, firewall rules, file associations, and
protocol activation remain application-owned; the SharpTS SDK does not silently add them.

Uninstalling the package follows ordinary MSIX policy. Application data under the package's local
state or `%LOCALAPPDATA%\SharpTS.Gui` is not uploaded and should only be removed under an explicit
retention policy.

## Crash and support diagnostics

Fatal host diagnostics are retained under `%LOCALAPPDATA%\SharpTS.Gui\Errors`; opt-in renderer
traces are under `Traces`. Create a bounded support bundle with:

```powershell
.\scripts\collect-gui-support-bundle.ps1 -OutputPath .\sharpts-support.zip -ApplicationName Product
```

The collector includes recent error logs plus OS/runtime metadata, replaces user-profile and temp
paths, omits user names, rejects files over 10 MiB, and records hashes in `support.json`. Traces can
contain application values and are excluded unless the user explicitly passes `-IncludeTraces`.
Support bundles are user-controlled artifacts; no telemetry or automatic upload is implemented.
