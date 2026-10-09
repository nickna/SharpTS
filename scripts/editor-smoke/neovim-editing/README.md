# Native Neovim editing and coexistence smoke

This optional smoke runs Neovim's actual native LSP client, buffer notifications,
floating hover UI, completion text-edit application, and workspace-edit application.
It uses the Release language-server DLL already on disk and never builds it.

Prepare the Release server with the repository's normal .NET build, install
Neovim 0.12.5, and install the two optional locked tool sets:

```sh
npm ci --prefix tools/formatter-interop --ignore-scripts --no-audit --no-fund
npm ci --prefix tools/editor-interop --ignore-scripts --no-audit --no-fund
node scripts/editor-smoke/neovim-editing/run.mjs --nvim /absolute/path/to/nvim
```

On the recorded Windows ARM64 host, the default editor path is
`artifacts/editor-tools/nvim-win-arm64/bin/nvim.exe`. Other installations should
pass `--nvim` with an absolute executable path. Optional `--server`, `--dotnet`,
and `--artifacts` arguments accept a value after each flag. No Neovim plugins or
user configuration are loaded (`--headless -u NONE`); XDG directories are isolated
under the run directory. Each editor process has a two-minute hard deadline and
stops its servers in cleanup. This is editor contract verification, not a latency
benchmark or a proof of every interactive UI command.

## Tested scenarios

Full-alone starts SharpTS with the explicit arguments
`--language-features full --diagnostics all`. It verifies:

- Ordinary hover in the native floating window, cross-file member definition,
  and three references, including an exact UTF-16 range after a non-BMP character.
- Unfinished `item.` completion, application of the returned edit through
  `vim.lsp.util.apply_text_edits`, and a fresh hover on the inserted member.
- Unfinished `pair(1, ` signature help with the second active parameter and no
  fabricated selected signature.
- Lexical rename applied through `vim.lsp.util.apply_workspace_edit` and a fresh
  hover on the new name.
- Dirty dependency changes, importer diagnostics, shifted definition ranges,
  and disk-source restoration after discarding and closing the dependency buffer.
- One documented external Prettier save hook for `.ts` and `.tsx`, exact saved
  bytes, and fresh post-format requests. SharpTS advertises no LSP formatting.
- Private rename refusal with Neovim's default capabilities, which omit
  `workspace.workspaceEdit.documentChanges` in the tested version.
- A separate explicitly opted-in client applying the versioned private edit
  natively; the selected domain changes while another class's `#old` stays intact.

Coexistence starts both actual processes in the same buffers:

```text
SharpTS: --language-features interop-only --diagnostics sharpts-only
TypeScript: node tools/editor-interop/node_modules/typescript-language-server/lib/cli.mjs --stdio
```

The TypeScript adapter is **6.0.1** and its backend is **TypeScript 6.0.3**, both
exactly locked in `tools/editor-interop`. The adapter's
[tagged installation instructions](https://github.com/typescript-language-server/typescript-language-server/blob/v6.0.1/README.md)
require TypeScript 6 because TypeScript 7 does not expose the tsserver API.
The [adapter release](https://github.com/typescript-language-server/typescript-language-server/releases/tag/v6.0.1)
is community maintained; it is separate from Microsoft's VS Code TypeScript
extension. The smoke supplies the exact `tsserver.js` path, disables automatic
typing acquisition, uses one semantic server, and verifies the backend's real
`$/typescriptVersion` notification. Node must satisfy the locked adapter's
`>=22.22.2` engine requirement.

Native definition dispatch has exactly one ordinary owner, TypeScript; SharpTS
does not advertise definition, references, or rename in interop mode. Ordinary
hover is available only from TypeScript, and an introduced ordinary type error
appears only in TypeScript diagnostics. At the `@DotNetType` fixture, only SharpTS
returns the CLR-qualified description. Neovim 0.12.5's native `vim.lsp.buf.hover`
merges the attached clients' responses, and its real floating window must contain
that CLR description. No custom hover routing replaces the native client.

The same one external Prettier `BufWritePre` hook from
[`docs/formatting.md`](../../../../docs/formatting.md) handles saves in both modes.
Although the TypeScript adapter advertises formatting, this setup never calls
LSP formatting. The hook is the only formatting owner. Expected Prettier output
uses the same fixture working directory, real filename, and discovered config
as the native save hook. SharpTS post-save requests wait for diagnostics for the
exact new document version. The TypeScript adapter deduplicates unchanged
diagnostics, so its post-save check is a fresh native request without requiring
a duplicate diagnostic publication.

## Evidence

Every run writes a unique `artifacts/editor-editing/neovim/run-*/summary.json`,
per-mode `result.json`, process logs, isolated native LSP debug logs, tsserver
logs, initial/final source, effective commands/capabilities, and configuration.
The summary records OS/architecture, Node/.NET/editor/tool versions, source
commit and dirty-state flag, server/core DLL hashes, harness hashes, tool-lock hash
and extracted formatter-recipe hash. A failure includes its named
scenario and preserves the workspace. `last-verified.json` is a compact tracked
record of the successful Windows ARM64 run; raw logs stay in ignored artifacts.

The harness is portable through the explicit editor path but the recorded
execution currently covers Windows ARM64. The aggregate CI protocol/package
checks cover supported Windows and Ubuntu hosts separately; this native editor
run does not imply an unrecorded host or editor version was exercised.
