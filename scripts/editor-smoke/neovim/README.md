# Neovim format-on-save smoke

Install the pinned formatter and build the language server, then run from the repository root:

```powershell
npm ci --prefix tools/formatter-interop
dotnet build src/SharpTS.LanguageServer -c Release
node scripts/editor-smoke/neovim/run.mjs --nvim /absolute/path/to/nvim.exe --server src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll
```

The runner uses isolated workspaces and Neovim's headless mode, extracting the actual save-hook
recipe from `docs/formatting.md`. In both SharpTS modes it attaches the real server, checks its
formatting/navigation capabilities, obtains .NET interop hover from dirty `.ts` and `.tsx` buffers,
checks fresh hover again after formatting, and verifies a second dirty save against the pinned
Prettier CLI. Logs and version-stamped results go to
`artifacts/formatter-interop/neovim`; override that with `--artifacts`.

This exercises the editor's real save autocmd and LSP client. It does not test a graphical UI,
another TypeScript client, or arbitrary third-party Neovim formatting plugins.

An empty generated GUI contract in each disposable workspace bounds the server's existing GUI
metadata discovery; the fixture project is never built or restored.
