# SharpTS Homebrew tap template

This directory prepares `nickna/homebrew-tap`; the tap has not been published by these files.
Create it with `brew tap-new nickna/tap`, retain the generated maintenance workflows, and copy
the cask CI workflow and `global.json` from this directory. Generate the two `Casks/*.rb` files from a successful
stable release using the upstream `scripts/generate-homebrew-casks.ps1` helper. Never substitute
example checksums. Configure the two Apple Silicon cask jobs as required checks and enable
repository auto-merge before enabling upstream update automation.

Once the tap and signed release are live, its user-facing README should document:

```sh
brew install --cask nickna/tap/sharpts
# Alternative, with the Native AOT feature limitations described below:
brew install --cask nickna/tap/sharpts-native
brew upgrade --cask nickna/tap/sharpts
brew uninstall --cask nickna/tap/sharpts
```

Both casks support Apple Silicon Macs only, install `sharpts`, and conflict with each other.
Switch implementations by uninstalling the current cask before installing the other one.
Both executables include their runtime; basic installation needs no separate .NET dependency.
Running compiled DLL output requires a compatible .NET host.

The managed cask provides the full CLI feature set. Native AOT starts faster and uses a closed
.NET interop catalog. Arbitrary DLL/NuGet references, `--verify`, `--gen-decl`, and compiled
`child_process.fork` require the managed build. Both signed executables must pass Gatekeeper
without user workarounds. See the upstream [distribution guide](https://github.com/nickna/SharpTS/blob/main/docs/homebrew-distribution.md)
for release gates, credentials, and the remaining initial-publication checklist.
