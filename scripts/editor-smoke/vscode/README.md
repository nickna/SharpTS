# Isolated VS Code format-on-save smoke

This optional extension-host smoke uses the installed VS Code executable, the
real Prettier extension, the locally pinned Prettier package and a built SharpTS
language server. It creates its workspace, user data, downloaded extension and
results under `artifacts/formatter-interop/vscode`; normal editor settings and
extensions are untouched. This directory is a development/test extension only.

Acquire the optional formatter package with `npm ci --prefix tools/formatter-interop`.
Install the pinned editor extension into the isolated directories:

```powershell
code --extensions-dir artifacts/formatter-interop/vscode/extensions --user-data-dir artifacts/formatter-interop/vscode/user-data --install-extension esbenp.prettier-vscode@12.4.0 --force
```

Build the Release language server, then run:

```powershell
dotnet build src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj -c Release
node scripts/editor-smoke/vscode/run.mjs
```

`--code`, `--server`, `--dotnet` and `--artifacts` accept explicit paths. The
default editor path is the standard Windows per-user VS Code installation;
other platforms use `code` from PATH. The runner stops after 45 seconds.

The test opens and dirties a TypeScript document, then calls the actual editor
save pipeline twice. The installed Prettier extension must produce exactly the
output of local Prettier 3.9.9, and its current-run extension log must confirm that
both saves used that exact local module and version. A small test-only hover bridge keeps a real
`sharpts-lsp --language-features interop-only` process active and obtains SharpTS
.NET decorator hover through VS Code before and after both saves. The initialize
response must advertise hover and no formatting capabilities. This verifies
editor-owned formatting coexistence; it does not exercise the shipping SharpTS
VS Code extension's activation or packaging.

The machine-readable evidence is `artifacts/formatter-interop/vscode/result.json`;
extension-host output is saved alongside it.
