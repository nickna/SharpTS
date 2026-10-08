# Formatting TypeScript and TSX

Use Prettier **3.9.9**, pinned in [tools/formatter-interop](../tools/formatter-interop/package.json),
to format SharpTS source. The editor owns formatting in both `full` and `interop-only` language-server
modes. `sharpts-lsp` does not advertise document, range or on-type formatting and does not require
Node.js or Prettier to run.

## Install and verify

Use Node.js 22 or newer (the repository version is recorded in [`.node-version`](../.node-version)).
From the repository root, install the exact development-tool versions in the lockfile and run the
compatibility smoke check with PowerShell 7 and the repository's .NET SDK:

```powershell
npm ci --prefix tools/formatter-interop
pwsh ./scripts/test-formatter-interop.ps1
```

The check formats representative TypeScript, declaration and TSX inputs, checks a second pass for
idempotence, and passes original and formatted inputs through SharpTS with matching options. It
also compares representative execution results and checks the server's formatting capabilities.
This is an opt-in development check; ordinary .NET builds and the packaged language server do not
install a formatter.

For a separate SharpTS application, install the same version as a project development dependency:

```powershell
npm install --save-dev --save-exact prettier@3.9.9
```

Use that application's `node_modules/prettier` in place of `tools/formatter-interop/node_modules/prettier`
in the editor recipes below, and commit its package lockfile.

Keep project formatting choices in a `.prettierrc.json` or another
[Prettier configuration file](https://prettier.io/docs/configuration). For example:

```json
{
  "tabWidth": 2,
  "semi": true,
  "endOfLine": "lf"
}
```

Use the installed CLI directly when diagnosing editor differences:

```powershell
node tools/formatter-interop/node_modules/prettier/bin/prettier.cjs --version
node tools/formatter-interop/node_modules/prettier/bin/prettier.cjs --write path/to/app.ts
```

When sending a buffer through stdin, pass its real filename with `--stdin-filepath`, including its
`.ts`, `.d.ts` or `.tsx` extension. That lets Prettier infer the TypeScript parser and discover the
configuration for that file. A `.tsx` filename also selects TSX syntax. Do not use a fictitious
`stdin.ts` filename or force a JavaScript parser. See the
[Prettier CLI documentation](https://prettier.io/docs/cli#--stdin-filepath).

## Select one formatting owner

The same Prettier installation can serve both language-server arrangements:

| Editing arrangement | SharpTS mode and diagnostics | Ordinary TypeScript features | Formatting |
| --- | --- | --- | --- |
| SharpTS as the sole server in Neovim or Helix | `full --diagnostics all` | SharpTS's supported navigation and interop features | External Prettier |
| SharpTS alongside an existing TypeScript server | `interop-only --diagnostics sharpts-only` | The existing TypeScript server | External Prettier |
| VS Code with the SharpTS extension | Extension's default `interop-only`; `sharpts.diagnostics: sharpts-only` | VS Code's TypeScript service | Prettier extension using the pinned module |

Full mode's supported features are described in the [language-server guide](language-server.md);
it does not provide every feature of TypeScript's language service. Disable other format-on-save
hooks for these files when choosing Prettier. Import organization and lint fixes are separate
actions and are not required for formatting.

## VS Code

Install **Prettier - Code formatter** (`esbenp.prettier-vscode`). In a checkout of this repository,
merge the following into `.vscode/settings.json`:

```json
{
  "prettier.prettierPath": "./tools/formatter-interop/node_modules/prettier",
  "sharpts.diagnostics": "sharpts-only",
  "[typescript]": {
    "editor.defaultFormatter": "esbenp.prettier-vscode",
    "editor.formatOnSave": true
  },
  "[typescriptreact]": {
    "editor.defaultFormatter": "esbenp.prettier-vscode",
    "editor.formatOnSave": true
  }
}
```

`prettier.prettierPath` names the module directory, not its CLI script. For another workspace,
use the path to the same pinned module there. Keep the language settings in separate blocks.
Use **Format Document With...** to confirm the selected owner and the **Prettier** output channel
to check the resolved module/version. The extension uses the open document's filename, so no
stdin override is needed. Local module loading requires a trusted workspace; an untrusted
workspace can use the extension's bundled version instead. See the official
[Prettier VS Code settings](https://github.com/prettier/prettier-vscode#settings).

## Neovim

For Neovim 0.11+, register SharpTS with the recipe in the
[language-server guide](language-server.md#install-and-launch). Use `full --diagnostics all` when
it is the sole server. When keeping an existing TypeScript client, use
`interop-only --diagnostics sharpts-only` for SharpTS and keep that client's ordinary feature setup.

The following `init.lua` example sends the unsaved buffer to the pinned CLI before saving. Replace
the absolute CLI path with your checkout's path; forward slashes also work in Windows paths.
`node` must be available on the editor's `PATH`. It runs the executable directly, without a shell.

```lua
local prettier_cli = "/absolute/path/to/SharpTS/tools/formatter-interop/node_modules/prettier/bin/prettier.cjs"

vim.api.nvim_create_autocmd("BufWritePre", {
  group = vim.api.nvim_create_augroup("sharpts_prettier", { clear = true }),
  pattern = { "*.ts", "*.tsx" },
  callback = function(ev)
    local path = vim.api.nvim_buf_get_name(ev.buf)
    if path == "" or vim.bo[ev.buf].buftype ~= "" then
      return
    end
    local lines = vim.api.nvim_buf_get_lines(ev.buf, 0, -1, false)
    local newline = vim.bo[ev.buf].fileformat == "dos" and "\r\n" or "\n"
    local input = table.concat(lines, newline)
    if vim.bo[ev.buf].endofline then
      input = input .. newline
    end
    local tick = vim.api.nvim_buf_get_changedtick(ev.buf)
    local ok, result = pcall(function()
      return vim.system(
        { "node", prettier_cli, "--stdin-filepath", path },
        { stdin = input }
      ):wait(5000)
    end)
    if not ok or result.code ~= 0 then
      local detail = ok and result.stderr or tostring(result)
      vim.notify("Prettier failed; saving unformatted: " .. (detail or ""),
        vim.log.levels.WARN)
      return
    end
    if not vim.api.nvim_buf_is_valid(ev.buf)
      or vim.api.nvim_buf_get_changedtick(ev.buf) ~= tick then
      vim.notify("Buffer changed; skipped stale formatting", vim.log.levels.WARN)
      return
    end
    if result.stdout == input then
      return
    end
    local output = result.stdout or ""
    local fileformat = output:find("\r\n", 1, true) and "dos" or "unix"
    output = output:gsub("\r\n", "\n")
    local endofline = output:sub(-1) == "\n"
    if endofline then
      output = output:sub(1, -2)
    end
    local view = vim.fn.winsaveview()
    vim.api.nvim_buf_set_lines(ev.buf, 0, -1, false,
      vim.split(output, "\n", { plain = true }))
    vim.bo[ev.buf].fileformat = fileformat
    vim.bo[ev.buf].endofline = endofline
    vim.fn.winrestview(view)
  end,
})
```

This uses Neovim's documented [direct process API](https://neovim.io/doc/user/lua/#vim.system())
and a bounded synchronous save hook. A formatting failure keeps and saves the original buffer.
Do not add a second TypeScript formatting autocmd calling
`vim.lsp.buf.format()`. If another formatter plugin already owns these files, configure that
plugin to invoke this pinned Prettier CLI with the buffer filename instead of installing this hook.

## Helix

Merge this into your project's `.helix/languages.toml`. Replace both absolute CLI paths and ensure
`node` and `sharpts-lsp` are available on `PATH`:

```toml
[language-server.sharpts]
command = "sharpts-lsp"
args = ["--language-features", "full", "--diagnostics", "all"]

[[language]]
name = "typescript"
language-servers = ["sharpts"]
auto-format = true
formatter = { command = "node", args = ["/absolute/path/to/SharpTS/tools/formatter-interop/node_modules/prettier/bin/prettier.cjs", "--stdin-filepath", "%{buffer_name}"] }

[[language]]
name = "tsx"
language-servers = ["sharpts"]
auto-format = true
formatter = { command = "node", args = ["/absolute/path/to/SharpTS/tools/formatter-interop/node_modules/prettier/bin/prettier.cjs", "--stdin-filepath", "%{buffer_name}"] }
```

Helix passes the buffer through stdin; `%{buffer_name}` supplies the filename. The external
`formatter` takes precedence over LSP formatting, and `auto-format` enables it on save. See
[Helix language configuration](https://docs.helix-editor.com/languages.html).

For coexistence, change SharpTS's arguments to
`["--language-features", "interop-only", "--diagnostics", "sharpts-only"]` and replace each
`language-servers` list with:

```toml
language-servers = [
  { name = "typescript-language-server", except-features = ["format"] },
  "sharpts",
]
```

This uses Helix's built-in TypeScript-server definition and your existing installation of that
server. Completions, code actions and diagnostics from both servers are merged. Hover and signature
help use the first server advertising the feature: this order prioritizes ordinary TypeScript
results. Put `sharpts` first if you prefer SharpTS-specific hover/signature results; ordinary results
for those requests may then be unavailable. Server ordering does not merge these two providers.

## Recorded shipping and native editing smoke

The October 7, 2026 Windows ARM64-host editing checks use the current Release server from source
`a20b07d7` and pinned Prettier 3.9.9. VS Code and Neovim use Node.js 25.5.0; the Helix run uses
repository-pinned Node.js 22.23.2 ARM64 with the x64 editor build:

| Client | Mode and diagnostics | Actual client verification |
| --- | --- | --- |
| VS Code 1.134.0 with installed SharpTS VSIX 0.2.0 and prettier-vscode 12.4.0 | Shipping `interop-only --diagnostics sharpts-only` defaults | Built-in TypeScript navigation remains active; real shipping extension activation supplies CLR decorator/member hover before and after two dirty saves. Extension logs identify the pinned local formatter. [Smoke and evidence](../scripts/editor-smoke/vscode-shipping/README.md). |
| Neovim 0.12.5, SharpTS alone | `full --diagnostics all` | Native hover/navigation, applied completion and lexical/versioned private edits, unfinished signatures, dirty-dependency diagnostics and close restoration; actual `.ts`/`.tsx` saves use the Lua hook above and fresh post-save requests. [Native editing smoke](../scripts/editor-smoke/neovim-editing/README.md). |
| Neovim 0.12.5 with real TypeScript adapter 6.0.1 / TypeScript 6.0.3 | SharpTS `interop-only --diagnostics sharpts-only` | Ordinary navigation/diagnostics belong to TypeScript; CLR content reaches Neovim's native merged hover. One external save hook formats both extensions while both servers remain attached. [Exact tool lock](../tools/editor-interop/package-lock.json), [compact evidence](../scripts/editor-smoke/neovim-editing/last-verified.json). |
| Helix 25.07.1 (`a05c151b`), SharpTS alone | `full --diagnostics all` | Actual native completion insertion, lexical/private edit application, unfinished signatures, navigation and dirty diagnostics. `.ts`, `.tsx` and dependency saves match Prettier bytes with fresh hovers before and after formatting. [Native smoke](../scripts/editor-smoke/helix-editing/README.md), [recorded result](../artifacts/editor-contract/helix/run-ZAkEIw/result.json). |

These tests exercise actual native edit application and the shipping VSIX. They do not claim every
editor/plugin combination. Neovim's recorded default capabilities refuse private rename; a separate
explicit `documentChanges` opt-in proves native application of the versioned edit. SharpTS never
advertises formatting in either mode. The TypeScript adapter does advertise formatting, but the
documented coexistence setup selects only the external Prettier save hook.

The aggregate `pwsh ./scripts/test-editor-contract.ps1` runs the formatter compatibility evidence,
six real-stdio feature suites and isolated installed-tool checks. The same aggregate gates Ubuntu
and Windows CI; optional native editor runs retain their own versioned evidence.

## Earlier formatter-only smoke

On October 6, 2026, the following checks passed on Windows ARM64 with Node.js 25.5.0, Prettier
3.9.9 and a Release build of `SharpTS.LanguageServer`:

| Client | Mode | Execution and result |
| --- | --- | --- |
| VS Code 1.134.0, ARM64; prettier-vscode 12.4.0 | `interop-only` | Two dirty format-on-save cycles matched the pinned local module exactly; fresh SharpTS CLR hover worked before and after each save. The extension log confirmed Prettier 3.9.9 loaded from the requested module directory. |
| Neovim 0.12.5, ARM64 | `full`, `interop-only` | Headless editor using the exact Lua save hook above and native LSP client. Dirty `.ts`/`.tsx` saves matched Prettier byte for byte; fresh CLR hover worked before and after formatting; a second dirty save produced identical formatted output. |
| Helix 25.07.1 (`a05c151b`), x64 | `full`, `interop-only` | Interactive terminal session using the external formatter above. Dirty `.ts`/`.tsx` saves matched Prettier byte for byte; correlated LSP requests resolved CLR hover before and after each formatted save. |

All three clients verified that SharpTS did not supply formatting. Reproduction instructions are in the
[VS Code smoke](../scripts/editor-smoke/vscode/README.md),
[Neovim smoke](../scripts/editor-smoke/neovim/README.md) and
[Helix smoke](../scripts/editor-smoke/helix/README.md) guides. These runs attached SharpTS in each
mode. The Neovim and Helix sessions did not attach a second TypeScript server or test third-party
formatter plugins. The VS Code smoke used the installed Prettier extension and a test-only SharpTS
hover bridge; it did not exercise the shipping SharpTS extension's activation or packaging.

## Troubleshooting and compatibility limits

- Run `npm ci --prefix tools/formatter-interop` if the pinned module cannot be loaded. Invoke its
  CLI with `--version` and check that the editor resolves that same installation.
- Confirm the buffer has its real filename and correct `.ts`/`.tsx` extension. If configuration
  seems wrong, use `--find-config-path path/to/file.ts` with the pinned CLI and check project
  overrides and ignore files.
- Disable duplicate save hooks or formatting providers if a file changes twice. Selecting
  `interop-only` removes duplicate SharpTS navigation; it does not select a formatter in the editor.
- A formatter parser error leaves source unchanged. Correct incomplete syntax or disable
  format-on-save while editing it; do not remove supported SharpTS constructs to make formatting run.
- The smoke fixture set is a compatibility sample, not a promise about every combination of syntax,
  plugins and configuration. Preserve a failing supported example and rerun the smoke command when
  upgrading the pinned formatter. An unsupported combination needs a verified workaround or an
  explicit compatibility issue.

The separate compatibility command checks syntax and behavior beyond these small editor fixtures.
Recheck editor integration when changing client, formatter or save-hook versions.
