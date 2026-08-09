# GUI compatibility and support policy

This policy defines the contract required before a stable SharpTS GUI 1.0 release. Preview builds
remain evaluation releases: they receive best-effort fixes but no production support window or
forward-compatibility promise beyond the explicit fail-fast checks below.

## Versioned contracts

| Contract | Compatibility rule |
| --- | --- |
| `SharpTS.Gui.Sdk` package | Semantic versioning. Patch releases are compatible fixes; minor releases may add controls/props; breaking source or runtime behavior requires a major release. |
| GUI API in `app.json` | An integer runtime contract. The host rejects unsupported values before guest initialization. A breaking application API increments it. |
| Descriptor schema version/hash | Exact SDK/host match. A mismatch requires rebuilding the application; hashes are not negotiated. |
| Hosted ABI | Versioned independently from GUI API. A host may support multiple documented ABI versions, otherwise it fails before executing guest code. |
| Custom-control provider contract | Statically registered, versioned, and checked before guest initialization. Providers must declare compatible SDK ranges and rebuild when the contract changes. |
| Application/MSIX version | Owned by the application. It does not imply a SharpTS SDK version and must increase for every Windows update. |

Stable API removals require a major SDK release. Deprecations remain for at least one stable minor
line and include a migration diagnostic. New optional props, events, controls, and services may
ship in a minor version when old manifests and compiled guests continue to behave identically.
Bug fixes that change undocumented behavior may ship in a patch; changes to documented ordering,
cleanup, error routing, or native identity are compatibility changes.

The descriptor hash intentionally forces a rebuild even for some additive host changes. This is a
safety boundary, not a semantic-versioning exception: source compatibility and binary descriptor
compatibility answer different questions.

## Supported release lines

After 1.0, the latest patch of the current stable minor and the immediately previous stable minor
receive correctness and security fixes. The previous minor remains supported for 12 months after
its successor, or six months after the next major release, whichever is later. Only the latest
patch in a supported line is serviced. Critical security fixes may require upgrading to a newer
minor when backporting would weaken the Hosted ABI or native platform boundary.

Windows x64 is certifiable where the release workflow supplies signed native evidence. Windows
ARM64 becomes supported only after the same package passes native Headless and real-window tests on
ARM64 hardware. Cross-publish alone does not create a support claim. macOS has no support window
until Track E's native, bundle, signing, and notarization gates pass.

End-of-support dates and security advisories must be published before a line is removed. Preview,
nightly, source-built, unsigned, modified, and unsupported-platform artifacts are outside the
stable support window.

## Responsibility boundary

SharpTS owns reproducible failures in the supported SDK, generated launcher, hosted runtime,
renderer, and built-in services. Application owners own their code, assets, custom providers,
installer identity, certificate lifecycle, update feed, privacy notice, and enterprise policy.
Provider vendors own their native controls and trimming/AOT annotations.

A support report should include the exact SDK version, package/application version, RID, execution
mode, descriptor schema values, installer hash, signature/attestation verification, minimal
reproduction, and the redacted support bundle described in
[`windows-distribution.md`](windows-distribution.md). Secrets, signing keys, arbitrary application
data, and unreviewed traces must not be attached.

## Stable 1.0 release gate

A 1.0 claim requires all selected platform tracks to pass their native matrix; a signed installer
with immutable identity; SBOM and verifiable provenance; documented update/rollback and enterprise
deployment; support-bundle validation; completed security review; public package ownership; and a
published support/EOL calendar. Missing credentials, hardware, store ownership, or notarization are
release blockers and must be reported as such rather than converted into a compatibility claim.
