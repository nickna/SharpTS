# Release operations

SharpTS publishes several NuGet packages from one tag. NuGet cannot atomically publish the set, so
release safety comes from artifact and registration preflight, deterministic idempotent pushes, and
post-publish inventory verification.

Historical failures are recorded separately in [Release incident history](release-incidents.md).

## Package manifest

`.github/nuget-packages.json` schema 2 is the source of truth for expected package IDs,
deterministic push order, and membership in the tagged release. Every entry contains only `id` and
inherits the repository tag version. Per-package `version`, `publish`, and `sha256` fields are
rejected so the manifest cannot silently split the release train.

When adding a package:

1. Onboard its ID on NuGet separately from a release tag and assign it to the `nbn` package owner.
2. Confirm the `SharpTS` Trusted Publishing policy is active; it applies to packages owned by `nbn`.
3. Add the entry in the intended push order and run `./scripts/test-nuget-release.ps1`.
4. Run the Publish workflow manually. Its dry run builds and packs the set and performs public-feed
   preflight without publishing.

The tagged release is blocked if any manifest ID is not registered. This prevents an untested
permission from publishing established packages before failing on a new ID.

The contract covers `SharpTS`, `SharpTS.Sdk`, `SharpTS.Hosting`, `SharpTS.LanguageServer`,
`SharpTS.DebugAdapter`, and `SharpTS.Gui.Sdk`. Publication uses NuGet Trusted Publishing rather
than a stored API key. The nuget.org policy is owned by `nbn` and binds GitHub Actions to repository
`nickna/SharpTS`, workflow `publish.yml`, and the tag-restricted `nuget-release` environment. The
release job exchanges its GitHub OIDC token immediately before publication and uses the resulting
one-hour API key for all six package pushes.

`eng/GuiVersion.props` supplies the non-publishable `0.0.0-local` fallback for source-built GUI
workflows. Tagged and manually dispatched Publish runs stage their effective version into that
property and every artifact-bearing projection before restore and build. CLI scaffolding, the
`dotnet new` template, embedded `@sharpts/gui` package, GUI nuspec, package README, and compiled
assemblies therefore carry the tag or dry-run version. Independently built local packages are never
included in tagged NuGet publication.

## Normal release

The Publish workflow builds and tests artifacts before the release job. Preflight verifies that
every manifest-selected `.nupkg` exists, its embedded ID and version match the manifest and tag, and
every package ID is registered. The release job pushes
missing versions explicitly with `--skip-duplicate`, then queries NuGet up to 30 times at 20-second
intervals until every ID exposes the tag version. A push error is fatal only if final inventory
still lacks the version, which safely handles a lost response after NuGet accepted a package.

Rerunning a failed release is safe: exact versions already visible on NuGet are not pushed again,
and only missing packages are retried using the same artifacts. Do not rebuild one package from a
different commit to complete a partial tag.

User-facing documentation uses versionless commands or `<version>` placeholders. Publication does
not require a follow-up documentation-version advance.

## WinGet publication

SharpTS uses two WinGet package identities because the managed and Native AOT archives have the
same architecture and command name. `SharpTS.SharpTS` is the default, full-featured distribution;
`SharpTS.SharpTS.NativeAOT` is the closed-world native distribution documented in
[Native AOT](native-aot.md). Both expose `sharpts` and are mutually exclusive. Switching requires
uninstalling the current identity before installing the other one. The Windows executables are not
currently Authenticode-signed.

The initial manifests must be submitted manually, one pull request per identity, after a successful
stable release contains all four required assets:

- `sharpts-<version>-win-x64.zip`
- `sharpts-<version>-win-arm64.zip`
- `sharpts-native-<version>-win-x64.zip`
- `sharpts-native-<version>-win-arm64.zip`

Use the schema requested by the current `microsoft/winget-pkgs` pull request template. Each package
uses a version manifest, an `en-US` locale manifest, and a ZIP/portable installer manifest with x64
and ARM64 nodes. The nested file is the root-level `sharpts.exe`, the portable alias and command are
`sharpts`, and `ArchiveBinariesDependOnPath` remains unset. The archives are self-contained and do
not declare a .NET package dependency. Validate and install each local manifest, test it with
`Tools/SandboxTest.ps1`, and verify architecture selection, fresh-terminal command discovery,
`sharpts --version`, interpretation, compilation, uninstall cleanup, and both switching directions
on native x64 and ARM64 Windows systems.

Only enable automated updates after both initial identities are visible in the public WinGet
catalog:

1. Create a protected `winget-release` environment.
2. Create a classic GitHub PAT with only `public_repo`; fine-grained PATs are not supported by
   WinGetCreate. Store it as the environment secret `WINGET_CREATE_GITHUB_TOKEN`. Do not grant the
   optional `delete_repo` scope and do not pass the token as a command-line argument.
3. Set the repository variable `WINGET_AUTOMATION_ENABLED` to `true`.

For an exact stable tag (`v<major>.<minor>.<patch>`), the downstream Windows matrix downloads the
pinned WinGetCreate executable, verifies its SHA-256, and submits separate managed and Native AOT
update pull requests using the published x64 and ARM64 release URLs. Prerelease tags and manual
workflow dry runs never submit WinGet changes. If only one matrix entry fails, inspect the upstream
pull requests and rerun only failed jobs; before rerunning the entire workflow, close or reconcile
any already-open pull request for the same identity and version to avoid a duplicate submission.
After the first automated update is published, verify upgrading each identity from the preceding
catalog version.

Add the final `winget install --id ... -e` commands to the root README only after both initial
packages are searchable in the public catalog.

## Recovering a partial publish

1. Record the tag, workflow run, expected IDs, successful pushes, artifact checksums, and exact
   errors before changing credentials.
2. Verify package ownership in the NuGet Gallery and ensure the key covers every expected ID.
   Rotate or broaden it through normal secret management; never print or commit the key.
3. Reuse the original workflow artifact when available. Otherwise check out the exact tag and
   rebuild with the same SDK and `-p:MinVerVersionOverride=<tag-version>`.
4. Inspect package IDs, selected versions, and hashes before publication. Do not substitute bytes
   built from another commit.
5. Push each missing package explicitly:

   ```bash
   dotnet nuget push <package> \
     --source https://api.nuget.org/v3/index.json \
     --api-key <key> \
     --skip-duplicate
   ```

6. Query `https://api.nuget.org/v3-flatcontainer/<lowercase-id>/index.json` for every publishable
   entry and confirm its manifest-selected version appears.
7. Rerun the original workflow only after the inventory is understood so idempotent pushes can
   converge and the GitHub release step can complete.

After recovery, add factual context to [Release incident history](release-incidents.md) only when
it teaches a durable operational lesson. Keep this runbook generic.
