# VS Code packaging dependency review

The extension uses the locked VSCE 3.9.2 toolchain with Node 22. The Azure Identity
override stays within VSCE's supported 4.x range: 4.13.0 retains MSAL Node 3.8.10,
outside the affected 5.x–7.0.x range of
[GHSA-xcx6-jx8p-x3h5](https://github.com/advisories/GHSA-xcx6-jx8p-x3h5).
Review this override when Azure Identity supports the patched MSAL release.
The existing brace-expansion override now selects 2.1.7.

The 2026-10-26 locked installation reports no high or critical advisories. Four
moderate npm audit entries propagate one underlying advisory,
[GHSA-w5hq-g745-h8pq](https://github.com/advisories/GHSA-w5hq-g745-h8pq), through
`uuid -> @azure/msal-node -> @azure/identity -> @vscode/vsce`. The advisory concerns
the v3/v5/v6 APIs with caller-provided output buffers. Inspection of the locked
MSAL CommonJS bundle and ESM `crypto/GuidGenerator.mjs` shows its only UUID call is
`v4()` without arguments. These affected APIs are not reached by that dependency
path. Keep the advisory visible instead of forcing a UUID major version outside
MSAL's supported dependency range.

VSCE and Azure authentication are development dependencies used for packaging and
publishing. The extension's runtime production dependency is
`vscode-languageclient`. Inspect the packaged archive independently of npm audit:
VSCE's production file inventory excludes `@vscode/vsce`, `@azure/identity`,
`@azure/msal-node`, and `uuid`.

Reproduce the review from the repository root:

```powershell
npm ci --ignore-scripts --prefix extensions/vscode-sharpts
npm audit --json --prefix extensions/vscode-sharpts
npm test --prefix extensions/vscode-sharpts
```

Use the [shipping preparation workflow](../scripts/editor-smoke/vscode-shipping/README.md)
to produce a real VSIX with coordinated server and adapter binaries. Compare its
ZIP entries with the locked production dependency tree before delivery. Revisit
the advisory's reachability and this inventory whenever the lock file changes.
