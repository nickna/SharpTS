# Release distribution acceptance

Snapshot: 2026-10-30, combined source `3b61153b6576629b8b41b0db8c48b7db9add6660`.
This handoff covers [#1324](https://github.com/nickna/SharpTS/issues/1324),
[#1356](https://github.com/nickna/SharpTS/issues/1356) and
[#1357](https://github.com/nickna/SharpTS/issues/1357). It chooses no release version and
publishes no tag, package, catalog entry or installable cask. The public tap has been initialized;
secret inspection used names only.

## Current evidence

[Publish dry run 40483664022](https://github.com/nickna/SharpTS/actions/runs/40483664022)
passed at `9111c66f98d18ad78f149b4e847e963ef1d21d83`: build, all six standalone shards,
all five managed and all five native RID jobs. Publication jobs were skipped. This is evidence
for that commit, not certification of later changes; dispatch the final frozen release ref again.

The current release matrix is **five RIDs and ten CLI archives**: `win-x64`, `win-arm64`,
`linux-x64`, `linux-arm64` and `osx-arm64`, each with managed/native SKUs. Intel macOS was
retired; #1324's older six-RID/twelve-archive milestones are historical. The separate native
*guest* workflow's six-RID matrix is not the CLI distribution matrix.

[Latest stable v1.0.8](https://github.com/nickna/SharpTS/releases/tag/v1.0.8) has no binary
assets. [v1.0.12](https://github.com/nickna/SharpTS/releases/tag/v1.0.12) is a prerelease and
also has no binary assets. Older releases contain NuGet packages, not the required CLI archives.
No current public release can supply the initial WinGet/cask installers.

| Stream | Prepared and verified | Remaining real acceptance |
| --- | --- | --- |
| #1324 | Matching-runner managed/native publishing and final-archive smokes; successful five-RID dry run | Explicit stable version and frozen commit, pre-tag NuGet onboarding, fresh final-ref dry run, successful tagged workflow and ten attached CLI archives |
| #1356 | Both manifest identities, deterministic release-asset/hash/architecture checks, schema fixtures, gated update workflow and token name configured | Four actual stable Windows ZIPs, generated real manifests, x64/ARM64 install/PATH/uninstall/switching/sandbox evidence, two initial upstream PRs and CLA/review, catalog availability, second-version upgrade |
| #1357 | Public tap initialized; Apple Silicon matching runners and extracted-archive smokes; signing/notary helper; ARM-only conflicting cask templates, cask CI and gated update workflow | Apple signing configuration, signed/notarized stable archives and Gatekeeper evidence, real cask publication/audit/install/uninstall/conflict/Intel rejection/switching, second-version upgrade and restricted update token |

## Inputs still absent or not established

- **Release version and commit:** use the explicitly selected stable version after the intended
  source is frozen. Manual dispatch always stamps `0.0.0-dryrun.<run-number>` and never publishes,
  including dispatches on existing tags. Do not promote its synthetic version or rename its assets.
- **First DebugAdapter package:** the public NuGet flat-container lookup for
  `SharpTS.DebugAdapter` returned HTTP 404; the other five manifest IDs were registered through
  1.0.12. Follow [exact first-release onboarding](releasing.md#package-manifest), assign owner `nbn`
  and confirm its NuGet Trusted Publishing policy. The dry run does not test registration or
  credentials. The tag-restricted `nuget-release` environment exists; policy validity on NuGet
  itself was not established by secret-name inspection. Its runtime OIDC key is not a stored secret.
- **Apple CLI signing:** `macos-cli-distribution` exists but contains none of the six required
  secret names: `MACOS_DEVELOPER_ID_P12_BASE64`, `MACOS_DEVELOPER_ID_P12_PASSWORD`,
  `MACOS_DEVELOPER_ID_APPLICATION`, `MACOS_NOTARY_KEY_BASE64`, `MACOS_NOTARY_KEY_ID`,
  `MACOS_NOTARY_ISSUER_ID`. The `MACOS_CLI_SIGNING_ENABLED` flag is absent. Configure the
  protected environment/tag or approved-dispatch policy described in the
  [Apple distribution guide](homebrew-distribution.md#configure-macos-signing); the audited
  environment currently has no protection rules or branch restriction. Existing unsigned dry-run
  success establishes no Developer ID, notarization or Gatekeeper acceptance.
- **Homebrew publication:** the public [nickna/homebrew-tap](https://github.com/nickna/homebrew-tap)
  now exists with its `Casks/` directory and an availability README. Real casks await signed
  stable archives. The `homebrew-release` environment, `HOMEBREW_TAP_TOKEN` secret and
  `HOMEBREW_TAP_READY` flag were absent at inspection. Later automation
  needs the restricted token, both required Apple Silicon checks and repository auto-merge.
- **WinGet:** no initial public `SharpTS.SharpTS` manifests were found. The `winget-release`
  environment contains the `WINGET_CREATE_GITHUB_TOKEN` name and the automation flag is
  **disabled**. Token validity/scope was not inspected. Its environment currently has no
  protection rules; apply the [protected update setup](releasing.md#winget-publication) before
  enabling it after both initial identities are accepted. Existing Windows policy is unsigned;
  Authenticode is optional for these portable packages and requires no new credential to retain.

Upgrade acceptance in both catalogs requires a second selected release and cannot be
certified from the first package version.

## Reviewable artifacts and execution order

1. Merge/freeze the intended source, select the release version explicitly and finish the exact
   DebugAdapter onboarding. Run `gh workflow run publish.yml --repo nickna/SharpTS --ref <frozen-ref>`.
   Confirm the final-ref build, standalone and ten RID jobs; this command is only a dry run.
2. Complete the Apple signing configuration when those external inputs are available and require
   both actual Apple Silicon archive gates. Then tag that exact selected commit using the normal
   [release operations](releasing.md#normal-release). Confirm all ten CLI archives plus the NuGet
   inventory before consuming release URLs in either catalog.
3. Generate real WinGet manifests with [new-winget-manifests.ps1](../scripts/new-winget-manifests.ps1).
   Reuse the [two initial PR drafts](../distribution/winget/README.md), fill the chosen version
   and actual validation evidence, complete the hardware/sandbox/CLA checks, and submit each identity.
   Enable updates only after both identities are accepted.
4. Populate the initialized public tap, retaining Homebrew's generated maintenance workflows.
   Reuse [cask CI](../distribution/homebrew/.github/workflows/cask-ci.yml),
   [global.json](../distribution/homebrew/global.json), the
   [user-facing README draft](../distribution/homebrew/TAP-README.md.in) and the existing
   [stable-archive cask generator](../scripts/generate-homebrew-casks.ps1). Generate casks from
   actual signed stable archive bytes; run the real Apple Silicon/Intel acceptance before advertising
   commands or enabling the restricted update workflow.

The generators reject unsuitable versions/assets instead of writing installable placeholder
manifests. No version or checksum should be filled into catalog metadata from test fixtures.

## Local validation record

The reused `artifacts/p2-work/winget-local-validation` inputs are explicitly **synthetic schema
fixtures**, not release archives. Both sets passed `winget validate` again with installed WinGet
`1.30.100-preview`; they were not installed or submitted. Existing generator controls passed:
WinGet 6/6; Homebrew hash/determinism/architecture/conflict/version/SKU checks; release-dispatch
truth table 9/9. Workflow policy validation passed for all 11 workflows. These checks do not
substitute for native hardware, signed Apple binaries, real catalog installation or upgrades.

The source audit and current logs are retained at
`artifacts/p2-work/release-acceptance-inventory/` in the verification worktree. Public service
and secret-name observations can change; recheck them immediately before publication.
