# SharpTS language server

`sharpts-lsp` is a standard language server over stdio. It provides SharpTS-specific .NET interop
diagnostics, hover, completion, signature help, and quick fixes in every mode. Its full mode also
provides document symbols, definition, references, and completeness-gated rename for standalone
editors.

## Install and launch

Install the separately packaged tool:

```bash
dotnet tool install --global SharpTS.LanguageServer
```

The default is appropriate for an editor where SharpTS is the only TypeScript language server:

```bash
sharpts-lsp
```

The equivalent explicit launch is:

```bash
sharpts-lsp --language-features full --diagnostics sharpts-only
```

Configure any LSP client with:

- command: `sharpts-lsp`
- file types: TypeScript and TSX
- transport: stdio
- project root: the nearest `tsconfig.json`, `sharpts.json`, or workspace root

For example, Neovim 0.11 can register it as:

```lua
vim.lsp.config.sharpts = {
  cmd = { "sharpts-lsp", "--language-features", "full" },
  filetypes = { "typescript", "typescriptreact" },
  root_markers = { "tsconfig.json", "sharpts.json", ".git" },
}
vim.lsp.enable("sharpts")
```

Helix can launch the same server with:

```toml
[language-server.sharpts]
command = "sharpts-lsp"
args = ["--language-features", "full"]
```

Then add `sharpts` to the `language-servers` list for the `typescript` and `tsx` language entries.
If another TypeScript server is active, use the coexistence mode below to avoid duplicate general
navigation.

## Coexisting with another TypeScript server

VS Code's SharpTS extension starts the bundled server in `interop-only` mode because VS Code's
built-in `tsserver` already owns ordinary TypeScript navigation:

```bash
sharpts-lsp --language-features interop-only
```

This mode still advertises the features unique to SharpTS:

- .NET interop diagnostics;
- decorator/member hover, completion, and signature help;
- structured quick fixes for safe interop corrections.

It does not advertise document symbols, definition, references, or rename. The feature mode is
fixed during LSP initialization; restart the server to change it.

## Diagnostics

`--diagnostics` accepts:

- `sharpts-only` (default): publish interop and other SharpTS-specific diagnostics without
  duplicating `tsserver`;
- `all`: also publish the full SharpTS parser/type-checker result;
- `off`: publish no diagnostics and clear existing results.

Clients may change this live with `workspace/didChangeConfiguration`:

```json
{
  "settings": {
    "sharpts": {
      "diagnostics": "all"
    }
  }
}
```

VS Code exposes the same value as `sharpts.diagnostics`.

## Project and .NET references

The server discovers workspace `tsconfig.json` files and their in-workspace project references.
Use these launch options for CLR metadata:

```bash
sharpts-lsp --project ./MyApp.csproj
sharpts-lsp -r ./lib/MyInterop.dll -r ./lib/Another.dll
sharpts-lsp --sdk-path /path/to/reference/assemblies
```

A `sharpts.json` reference manifest found from the workspace root is also honored. Referenced
assembly outputs are reloaded safely when they change.

## Rename safety

Full mode produces a rename edit only when the server has loaded every configured project root and
project reference needed for the selected semantic binding. It deliberately refuses rename for an
incomplete graph rather than returning a partial cross-file edit. General object/class
property-member rename is not currently offered.
