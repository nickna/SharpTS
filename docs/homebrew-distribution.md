# Apple Silicon CLI distribution

The managed and Native AOT `osx-arm64` jobs publish on `macos-15`, execute the published
binary, and then execute the extracted final archive before uploading it. Both archives keep
the root-level `sharpts`, `LICENSE`, and `README.md` layout. The archive helper checks the
executable hash against the publish payload, checks its version, runs interpretation and saved
compilation, and exercises each SKU's verification contract. Windows and Linux archives use
the same final-archive gate on their matching runners.

Homebrew publication is prepared but requires the signing credentials and initial tap below.
Keep issue #1357 open until a real stable release has passed signing, Gatekeeper, cask install,
upgrade, and uninstall acceptance on Apple Silicon. The main README should advertise the
Homebrew commands only after the tap is live. Version 1.0.8 has no suitable binary assets.

## Configure macOS signing

Create a protected GitHub environment named `macos-cli-distribution`, restricted to release tags
and approved release dispatches. The other RIDs use the `binary-release` environment and do
not need Apple credentials. Configure these exact environment secret names:

| Secret | Value |
| --- | --- |
| `MACOS_DEVELOPER_ID_P12_BASE64` | Base64-encoded Developer ID Application certificate and private key exported as PKCS#12. |
| `MACOS_DEVELOPER_ID_P12_PASSWORD` | Password protecting that PKCS#12 export. |
| `MACOS_DEVELOPER_ID_APPLICATION` | Full signing identity, including its team identifier. |
| `MACOS_NOTARY_KEY_BASE64` | Base64-encoded App Store Connect API private key (`AuthKey_*.p8`). |
| `MACOS_NOTARY_KEY_ID` | The key's identifier. |
| `MACOS_NOTARY_ISSUER_ID` | The API issuer UUID. |

Set the repository variable `MACOS_CLI_SIGNING_ENABLED=true` only after those secrets are
available. A missing secret then fails the job before packaging. The script imports the
certificate into an ephemeral keychain, signs each executable with hardened runtime and a
secure timestamp, verifies its signature, submits a temporary ZIP using `notarytool --wait`,
requires `Accepted`, and assesses the executable using `spctl`. Managed bundles receive the
JIT entitlement required by their runtime; Native AOT does not receive it. Private material and
the keychain are removed in `finally`.

The distributed assets remain `.tar.gz`. The temporary ZIP is Apple's submission container;
the final tarball must contain the identical signed executable. The archive gate compares its
SHA-256 with the publish payload and repeats signature and Gatekeeper assessment after
extraction. A bare command-line executable cannot carry a stapled ticket, so this procedure
requires online Gatekeeper assessment and makes no offline-install guarantee. No user
Gatekeeper workaround is part of the installation flow.

The existing unsigned release route remains available while signing is disabled. Homebrew
automation additionally requires signing to be enabled and independently assesses the actual
published archives. An enabled flag cannot substitute for successful artifact validation.

See Apple's [notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow)
and Microsoft's [macOS deployment guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos).

## Publish the initial tap

1. Create the public `nickna/homebrew-tap` repository using `brew tap-new nickna/tap`. Retain
   its generated maintenance workflows and add the supplied cask CI workflow from
   `distribution/homebrew/.github/workflows/cask-ci.yml`, along with its `global.json`.
2. Run the release workflow in dispatch mode to validate all matching runners. Publish a new
   stable release only after the signing gates pass for both Apple Silicon archives.
3. Download both exact stable assets and generate the initial casks:

   ```powershell
   ./scripts/generate-homebrew-casks.ps1 -Version <stable-version> `
     -ManagedArchive ./sharpts-<stable-version>-osx-arm64.tar.gz `
     -NativeArchive ./sharpts-native-<stable-version>-osx-arm64.tar.gz `
     -OutputDirectory ./tap
   ```

   This computes both hashes from the files and produces deterministic UTF-8 casks. Preview
   versions, wrong asset names, and missing archives fail. No checked-in installable cask
   contains a placeholder hash.
4. Copy the generated `Casks` and adapt the supplied tap README. Review the PR and run the
   Apple Silicon cask jobs. They audit/style both casks, install each, check version, signature,
   Gatekeeper, interpretation, supported compilation and managed-only diagnostics, test the
   reciprocal conflict, and uninstall while checking the binary link is gone.
5. Verify Intel rejection from the `depends_on arch: :arm64` requirement on an Intel Mac.
   Once a second signed stable release exists, test upgrades from the previous cask version
   and switching SKUs. These hardware/publication checks cannot be confirmed on Windows.

The casks share the `sharpts` command and conflict with each other. Neither requires .NET for
basic installation; running emitted DLL output needs a compatible .NET host. The Native AOT
cask describes its managed-only limits before installation. See the
[Homebrew Cask Cookbook](https://docs.brew.sh/Cask-Cookbook) for the DSL and architecture rules.

## Enable stable updates

Configure the tap's required checks as `Apple Silicon (sharpts)` and
`Apple Silicon (sharpts-native)`, and enable its repository auto-merge setting. In the upstream
`homebrew-release` protected environment, add `HOMEBREW_TAP_TOKEN`: a fine-grained PAT restricted
to Contents and Pull Requests read/write for `nickna/homebrew-tap` only. Its account must be
able to read the tap's checks. Set upstream `HOMEBREW_TAP_READY=true` after the initial casks
have been published and tested.

On a published stable release, `.github/workflows/homebrew.yml` downloads the public assets,
executes and assesses both signed archives on Apple Silicon, computes hashes, and opens a
two-cask update PR with that restricted token. The update job requires both configured checks
to exist, waits for required checks to pass, and then requests auto-merge. The upstream
`GITHUB_TOKEN` performs no cross-repository writes. Preview releases are skipped. An update
failure leaves the published SharpTS release intact and requires inspection before rerunning.

Local checks are `./scripts/test-homebrew-casks.ps1` and
`./scripts/test-cli-release-archive.ps1` against an archive for the current platform. Signing,
notarization, Homebrew audit/install, Intel rejection, and upgrades require their actual
credentials, platforms, and released artifacts; local helper checks do not claim those gates.
