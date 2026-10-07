# SharpTS language server

`sharpts-lsp` is a standard language server over stdio. It provides SharpTS-specific .NET interop
diagnostics, hover, completion, signature help, and quick fixes in every mode. Its full mode also
provides document symbols, definition, references, and completeness-gated rename for standalone
editors.

Formatting is supplied by the editor and an external formatter. Neither language-feature mode
advertises document, range or on-type formatting. See [Formatting TypeScript and TSX](formatting.md)
for the pinned Prettier workflow, format-on-save recipes and formatter ownership when another
TypeScript server is active.

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

Editor requests capture one CLR metadata generation and inspect assembly bytes without locking
the original DLLs. Project, manifest, and assembly changes are revalidated before a result is
returned. Package acquisition happens at startup; requests use already restored package assets.
If a manifest adds a package that has not been restored, restore it and restart the server.
An explicit `--sdk-path` is revalidated, while the automatically selected installed SDK/runtime
is fixed for the server lifetime; restart after replacing that installation.

Interop hover, completion, and diagnostics honor these custom references. General checker
`dotnet:` synthesis still uses the existing runtime registry and may not resolve a custom
reference's ordinary TypeScript symbols. The server does not guess navigation or rename results
for unresolved symbols.

## Source definitions

Full mode resolves lexical names and supported source class members using the checked
receiver's declaration identity. Inherited members target their base declaration; overrides
target their own declaration. Getter/setter pairs and overload groups return their exact
declaration-name locations. Source class expressions, user `.d.ts` classes, parameter-properties,
auto-accessors and ECMAScript `#private` members participate where the checker proves the origin.

Definition preserves lexical/type/namespace/label navigation and GUI navigation precedence.
A known member target can be returned even when an unrelated project root is unavailable.
Ambiguous unions, unproved receiver paths, structural members, dynamic keys and locationless
CLR/builtin members supply no new class-member target. The
[member provenance contract](editor-member-provenance.md) lists the exact supported operations
and current checker limits. Definition support alone does not grant member rename permission.

## Shared analysis

Definition, references, lexical rename, and full diagnostics share completed analyses for the
same captured open buffers and project state. File notifications invalidate analyses promptly;
clients without watching support still get physical dependency/configuration validation on every
reuse. Creating a missing import, changing a closed file, or changing project membership causes
a fresh check. Results that become stale during a request are discarded.

The cache keeps at most eight completed entries and evicts the least recently used entries when
their estimated source/semantic payload exceeds 64 MiB. This estimate excludes CLR assembly
images and is not a limit on total process memory. Oversized analyses can serve active requests
without entering the cache. Concurrent identical requests share work, and cancelling one request
does not cancel another request's analysis. Interop-only with `sharpts-only` diagnostics keeps
general analysis lazy; explicitly selecting `all` still requests full parser/checker diagnostics.

Checked source documents also retain exact written syntax ranges for later editor queries.
Unfinished member and call/new contexts use a separate bounded parse of the same text; these
recovery artifacts cannot supply authoritative rename locations or change the checked graph.
The recovery cache retains at most 32 artifacts and 8 MiB within the shared 64 MiB estimate.

Completed analyses also retain [bounded semantic query values](editor-semantic-queries.md):
checked declaration/occurrence types, visible bindings, accessible receiver members and actual
call/new candidate decisions. These values prepare the later ordinary hover, completion and
signature-help handlers. They do not run a second checker or retain its mutable environments.

For a local protocol smoke test after a Release build, run:

```bash
node scripts/test-analysis-snapshots.mjs
```

The test exercises real stdio navigation, source class member targets, watched closed-file
changes, dirty overlays, reverse importer creation, rename, cancellation transport and
interop-only capability isolation.

The [editor analysis benchmark](../benchmarks/editor-analysis/README.md) records a comparison with
`df4589b7` on Windows Arm64/.NET 10.0.12 (2026-10-07 UTC). The sequence contains definition,
workspace references, statement retrieval, and full diagnostics; JIT/filesystem caches are warm.

| Fixture | Cache-cold median, before → shared | Warm median, before → shared | Checks, cold / warm before → shared |
| --- | --- | --- | --- |
| One source file | 18.03 → 16.65 ms | 13.65 → 5.38 ms | 4 / 3 → 1 / 0 |
| 25 source files, four projects | 188.80 → 136.19 ms | 117.69 → 21.21 ms | 6 / 5 → 3 / 0 |

Cold definitions add about 2.12 ms and 11.70 ms respectively for capture, immutable publication,
and validation. Warm diagnostics also pay validation costs instead of trusting a stale cache.
Direct document validation medians were 0.55 ms and 2.44 ms; workspace validation was 0.70 ms
and 6.17 ms. Measured cache retention was about
19 KiB and 3.42 MB respectively; these fixtures exclude CLR metadata and do not predict large
project memory. The benchmark report includes allocations, percentiles, and measurement limits.

## Rename safety

Full mode produces a rename edit only when the server has loaded every configured project root and
project reference needed for the selected semantic binding. It deliberately refuses rename for an
incomplete graph rather than returning a partial cross-file edit. General object/class
property-member rename is not currently offered.

Constructor parameter-properties, such as `constructor(public value: number)`, also refuse rename
from their declaration or constructor-local uses: those edits would need to coordinate the
property name and its uses. Ordinary constructor parameters retain lexical rename support when
the configured graph is complete.
