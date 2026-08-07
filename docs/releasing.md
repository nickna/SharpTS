# Release Operations

SharpTS publishes several NuGet packages from one tag. NuGet does not provide a
transaction that can atomically publish the complete set, so release safety
comes from preflight checks, explicit idempotent pushes, and post-publish
inventory verification.

## Package manifest

`.github/nuget-packages.json` is the source of truth for the expected package
IDs, their deterministic push order, and the stable `SharpTS.Sdk` version used
by copyable documentation examples. When adding a package:

1. Onboard the package ID on NuGet separately from a release tag.
   Publishing an approved prerelease package is the only reliable end-to-end
   validation for a new ID and API-key scope.
2. Confirm the release API key is scoped to the new ID. NuGet does not expose an
   API that lets the workflow inspect an API key's package scopes.
3. Add the ID to the manifest and run `./scripts/test-nuget-release.ps1`.
4. Run the Publish workflow manually. Its dry run builds and packs the complete
   set and runs the public-feed preflight without publishing.

The tagged release is deliberately blocked if any manifest ID is not already
registered. This prevents an untested new-package permission from publishing an
established package first.

## Normal release

The Publish workflow builds and tests all artifacts before its release job. The
release job pushes each package explicitly with `--skip-duplicate`, records a
result for every package, and then queries NuGet until every manifest ID exposes
the tag version. The job fails if any push failed or the final inventory remains
incomplete.

Rerunning a failed release is safe. Already published packages are skipped and
missing packages are retried, allowing a partial release to converge without
changing package files or versions.

Only advance `documentedSdkVersion` and the matching documentation examples
after that SDK version is publicly visible. The preflight rejects inconsistent
pins and versions absent from NuGet.

## Recovering a partial publish

1. Record the tag, workflow run, expected IDs, successful pushes, and exact
   errors before changing credentials.
2. Verify package ownership in the NuGet Gallery and ensure the API key covers
   every expected ID. Rotate or broaden the key through NuGet's normal secret
   management process; never print or commit it.
3. Reuse the original workflow artifact when available. Otherwise check out the
   exact tag and rebuild with the same SDK and
   `-p:MinVerVersionOverride=<tag-version>`.
4. Inspect the resulting package IDs and versions before publication. Do not
   substitute packages built from another commit.
5. Push each missing package explicitly:

   ```bash
   dotnet nuget push <package> \
     --source https://api.nuget.org/v3/index.json \
     --api-key <key> \
     --skip-duplicate
   ```

6. Query `https://api.nuget.org/v3-flatcontainer/<lowercase-id>/index.json` for
   every expected ID and confirm the tag version appears.
7. For a tag created before this hardening, rerun its original workflow only
   after the manual inventory is complete so `--skip-duplicate` can reach the
   GitHub release step. Future tags use the hardened inventory check directly.

## v1.0.8 repair record

Run `29182139378` published `SharpTS` 1.0.8, then received HTTP 403 for
`SharpTS.LanguageServer`; the wildcard stopped before `SharpTS.Sdk`. The source
tag's intended package set was `SharpTS`, `SharpTS.LanguageServer`, and
`SharpTS.Sdk`. `SharpTS.Hosting` was introduced later and is intentionally not
part of the v1.0.8 repair.

As of the issue investigation, the public state is:

| Package | v1.0.8 | Required action |
| --- | --- | --- |
| `SharpTS` | Published | None; `--skip-duplicate` makes retries safe. |
| `SharpTS.Sdk` | Missing | Rebuild from tag `v1.0.8`, publish, and verify. |
| `SharpTS.LanguageServer` | Unregistered | Resolve ownership/API-key policy, then either publish the tag artifact or document an intentional exception. |
| `SharpTS.Hosting` | Not in tag | No v1.0.8 package is expected. Onboard before its first tagged release. |

Until an operator completes that repair, copyable SDK examples remain pinned to
the verified `SharpTS.Sdk` 1.0.7 release.
