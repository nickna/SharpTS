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

1. Onboard its ID on NuGet separately from a release tag. A prerelease publication is the reliable
   end-to-end check for ownership and API-key scope.
2. Confirm the release key is scoped to the new ID. NuGet does not expose an API for inspecting a
   key's package scopes.
3. Add the entry in the intended push order and run `./scripts/test-nuget-release.ps1`.
4. Run the Publish workflow manually. Its dry run builds and packs the set and performs public-feed
   preflight without publishing.

The tagged release is blocked if any manifest ID is not registered. This prevents an untested
permission from publishing established packages before failing on a new ID.

The contract covers `SharpTS`, `SharpTS.Sdk`, `SharpTS.Hosting`,
`SharpTS.LanguageServer`, and `SharpTS.Gui.Sdk`. Before the next tag, verify that the key stored in
`NUGET_API_KEY` can publish all five IDs. NuGet does not provide an API for inspecting key scope.

`eng/GuiPreviewVersion.props` remains the canonical version for source-built GUI preview
workflows. Tagged and manually dispatched Publish runs stage their effective version into that
property and artifact-bearing projections before build. CLI scaffolding, the `dotnet new` template,
embedded `@sharpts/gui` package, GUI nuspec, and package README therefore carry the tag or dry-run
version. Historical evidence and versionless user guides are not rewritten. Independently built
preview packages are never included in tagged NuGet publication.

## Normal release

The Publish workflow builds and tests artifacts before the release job. Preflight verifies that
every manifest-selected `.nupkg` exists and every package ID is registered. The release job pushes
missing versions explicitly with `--skip-duplicate`, then queries NuGet up to 30 times at 20-second
intervals until every ID exposes the tag version. A push error is fatal only if final inventory
still lacks the version, which safely handles a lost response after NuGet accepted a package.

Rerunning a failed release is safe: exact versions already visible on NuGet are not pushed again,
and only missing packages are retried using the same artifacts. Do not rebuild one package from a
different commit to complete a partial tag.

User-facing documentation uses versionless commands or `<version>` placeholders. Publication does
not require a follow-up documentation-version advance.

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
