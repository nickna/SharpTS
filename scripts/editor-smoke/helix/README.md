# Helix format-on-save smoke

Install the pinned formatter and build the language server, then prepare an isolated workspace:

```powershell
npm ci --prefix tools/formatter-interop
dotnet build src/SharpTS.LanguageServer -c Release
node scripts/editor-smoke/helix/prepare.mjs --mode full --hx /absolute/path/to/hx.exe
```

The preparation command prints the editor, runtime, config, log and workspace paths. Launch that
editor in a real terminal with `HELIX_RUNTIME` set to its extracted `runtime` directory:

```powershell
hx -c <workspace>/config.toml --log <workspace>/helix.log -vvv -w <workspace> <workspace>/format-on-save.ts
```

In each `.ts` and `.tsx` fixture, change `original` to `edited`. Put the cursor on `DotNetType` and
use Space+k to request hover; confirm the CLR type/XML documentation appears. Use `:write` and
confirm Prettier expanded the compact source. Request hover again after the save. Open the second
file with `:open format-on-save.tsx`,
repeat the edit/hover/save, and quit. Then verify the saved bytes and real LSP log:

```powershell
node scripts/editor-smoke/helix/verify.mjs artifacts/formatter-interop/helix/full
```

Repeat preparation and the terminal workflow with `--mode interop-only`, then verify that mode's
artifact directory. This uses the external formatter and filename expansion from `docs/formatting.md`
with the real server active. It does not require or test a second TypeScript server.

Verification matches each hover response to its JSON-RPC request and requires successful hovers
both before and after each formatted `didSave`. It also checks initial and dynamic formatting
capabilities. An empty generated GUI contract bounds metadata discovery to the disposable fixture;
the fixture project is never built or restored.
