# Initial WinGet submission drafts

The initial package identities are `SharpTS.SharpTS` and `SharpTS.SharpTS.NativeAOT`.
[Managed](managed-pr.md.in) and [Native AOT](native-aot-pr.md.in) PR bodies are reviewable
drafts. Replace their `@VERSION@` and `@VALIDATION_EVIDENCE_URL@` markers only after an
explicitly selected stable release and the actual installation tests below. These Markdown
templates are not installable manifests and do not assert that tests have passed.

Use the existing [initial manifest generator](../../scripts/new-winget-manifests.ps1) against
that public stable release. It verifies the four versioned Windows archives, root executable
layout, PE architecture and archive hashes before writing either package set. Do not substitute
the schema-validation fixtures under `artifacts/p2-work/winget-local-validation`: they contain
synthetic PE headers and no runnable compiler.

```powershell
# Supply the explicitly selected, published stable version without its v prefix.
./scripts/new-winget-manifests.ps1 -Version $releaseVersion -OutputDirectory ./artifacts/winget-initial
winget validate --manifest "./artifacts/winget-initial/manifests/s/SharpTS/SharpTS/$releaseVersion"
winget validate --manifest "./artifacts/winget-initial/manifests/s/SharpTS/SharpTS/NativeAOT/$releaseVersion"
```

Before submitting one PR per identity to `microsoft/winget-pkgs`, retain evidence from native
Windows x64 and ARM64 systems: correct installer selection, local-manifest install, version,
interpreter/compiler smokes, fresh-terminal command discovery, uninstall/link cleanup, both
SKU switching directions and `Tools/SandboxTest.ps1`. Local schema validation alone does not
establish those outcomes. Upgrade testing needs a second actual package version. Follow the
upstream repository's current contribution/CLA and manifest-schema requirements when submitting.

The packages share the `sharpts` command, so uninstall one identity before installing the other.
Windows signing is currently optional and these artifacts follow the existing unsigned policy.
Enable automated updates only after both initial identities have been accepted and their actual
installation checks pass; the configured token's presence does not establish its validity or scope.
See [release operations](../../docs/releasing.md#winget-publication) and the
[current release acceptance inventory](../../docs/release-acceptance.md).
