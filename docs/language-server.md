# SharpTS language server

`sharpts-lsp` is a standard language server over stdio. It provides SharpTS-specific .NET interop
diagnostics, hover, completion, signature help, and quick fixes in every mode. Its full mode also
provides document symbols, definition, references, and completeness-gated rename for standalone
editors, plus semantic hover, lexical/member completion and signature help for ordinary TypeScript.

Formatting is supplied by the editor and an external formatter. Neither language-feature mode
advertises document, range or on-type formatting. See [Formatting TypeScript and TSX](formatting.md)
for the pinned Prettier workflow, format-on-save recipes and formatter ownership when another
TypeScript server is active.

## Install and launch

Install the separately packaged tool:

```bash
dotnet tool install --global SharpTS.LanguageServer
```

When SharpTS is the only TypeScript language server, enable both ordinary editor features and
full parser/type-checker diagnostics explicitly:

```bash
sharpts-lsp --language-features full --diagnostics all
```

The CLI defaults are independently `full` language features and `sharpts-only` diagnostics.
Launching without flags is equivalent to:

```bash
sharpts-lsp --language-features full --diagnostics sharpts-only
```

Configure any LSP client with:

- command: `sharpts-lsp --language-features full --diagnostics all` for sole-server editing
- file types: TypeScript and TSX
- transport: stdio
- project root: the nearest `tsconfig.json`, `sharpts.json`, or workspace root

For example, Neovim 0.11+ can register it as:

```lua
vim.lsp.config.sharpts = {
  cmd = { "sharpts-lsp", "--language-features", "full", "--diagnostics", "all" },
  filetypes = { "typescript", "typescriptreact" },
  root_markers = { "tsconfig.json", "sharpts.json", ".git" },
}
vim.lsp.enable("sharpts")
```

Helix can launch the same server with:

```toml
[language-server.sharpts]
command = "sharpts-lsp"
args = ["--language-features", "full", "--diagnostics", "all"]
```

Then add `sharpts` to the `language-servers` list for the `typescript` and `tsx` language entries.
If another TypeScript server is active, use the coexistence mode below to avoid duplicate general
navigation.

## Coexisting with another TypeScript server

VS Code's SharpTS extension starts the bundled server in `interop-only` mode because VS Code's
built-in `tsserver` already owns ordinary TypeScript navigation:

```bash
sharpts-lsp --language-features interop-only --diagnostics sharpts-only
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
Diagnostics selection is independent of language-feature mode and can change live. Language-feature
mode is fixed at initialization. For example, `interop-only --diagnostics all` requests full
SharpTS diagnostics while retaining interop-only editor providers; it can duplicate another
TypeScript server's diagnostics.

## Verified editor arrangements

The reproducible editor smokes use explicit presets and record versions, capabilities, source,
server hashes and logs. They exercise the actual clients and shipping extension:

| Client and arrangement | SharpTS preset | Verified behavior and trace |
| --- | --- | --- |
| VS Code 1.134.0, installed SharpTS VSIX 0.2.0, built-in TypeScript extension 10.0.0 with TypeScript 6.0.3 | `interop-only --diagnostics sharpts-only` (shipping defaults) | Actual extension activation; ordinary imported definition; CLR decorator/member hover before and after two dirty saves; Prettier extension 12.4.0 loading local Prettier 3.9.9. [Shipping smoke](../scripts/editor-smoke/vscode-shipping/README.md), [compact evidence](../scripts/editor-smoke/vscode-shipping/last-verified.json). |
| Neovim 0.12.5, SharpTS alone | `full --diagnostics all` | Eight native-client scenarios: hover UI, exact UTF-16 member navigation, applied completion/lexical/private edits, unfinished signatures, dirty dependency diagnostics and close restoration, TS/TSX saves. [Native smoke](../scripts/editor-smoke/neovim-editing/README.md), [compact evidence](../scripts/editor-smoke/neovim-editing/last-verified.json). |
| Neovim 0.12.5, TypeScript adapter 6.0.1 with TypeScript 6.0.3 plus SharpTS | `interop-only --diagnostics sharpts-only` | One ordinary navigation/diagnostic owner, unique CLR content in native merged hover, and one external formatter. The adapter and backend are [exactly locked](../tools/editor-interop/package-lock.json); the real backend version notification is checked. [Compact evidence](../scripts/editor-smoke/neovim-editing/last-verified.json). |
| Helix 25.07.1 (`a05c151b`), SharpTS alone | `full --diagnostics all` | Native completion insertion, lexical and versioned private edit application, unfinished signature help, definitions/references, dirty ordinary diagnostics and final clean results. `.ts`, `.tsx` and dependency saves match pinned Prettier with fresh before/after hovers. [Native smoke](../scripts/editor-smoke/helix-editing/README.md), [compact evidence](../scripts/editor-smoke/helix-editing/last-verified.json). |

These recorded executions use a Windows ARM64 host and source base `a20b07d7` plus the #1981
working-tree changes. All three final runs use the same server binary, including the shared
token-helper refactor; each compact report records its hash. Helix is the x64
editor build, and its formatter uses the repository-pinned Node 22.23.2 ARM64. Native default Neovim
capabilities omit `documentChanges`, so private rename is refused. A separate explicit capability
opt-in verifies versioned edit application; existing lexical rename works with either capability
setting. General property rename and TypeScript language-service parity are not claimed.

Neovim's native hover merges responses in the tested version. Helix's first-provider hover and
signature routing has different behavior; see the [formatting/coexistence recipe](formatting.md#helix)
before attaching two providers. The earlier formatter-only VS Code smoke used a test hover bridge;
the shipping smoke above proves the installed extension's real activation and registrations.

## Project and .NET references

The server discovers workspace `tsconfig.json` files and their in-workspace project references.
Use these launch options for CLR metadata:

```bash
sharpts-lsp --language-features full --diagnostics all --project ./MyApp.csproj
sharpts-lsp --language-features full --diagnostics all -r ./lib/MyInterop.dll -r ./lib/Another.dll
sharpts-lsp --language-features full --diagnostics all --sdk-path /path/to/reference/assemblies
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

## Source class member references

Full mode also finds proven uses of a selected source class member across configured projects
inside the initialized workspace roots. Lexical/type/namespace bindings keep priority, including
constructor-local parameter-property bindings. Inherited accesses belong to their base member;
overrides and unrelated classes keep separate identities. Overload/accessor declaration groups
honor `includeDeclaration`, and results use each target's own dirty or disk-backed source ranges.

Member discovery requires initialized roots and a complete configured discovery graph. A missing
root, unreadable configuration or unavailable project reference returns no new member result,
including otherwise known uses in the selected document. Canonical declaration path and full name
span connect independently checked projects; generation-local checker IDs are never compared.

Graph completeness establishes which configured projects were discovered. It does not make the
supported class-member proof domain exhaustive: structural/interface-typed accesses, unsupported
checker branches and unvisited source supply no guessed references. A complete reference result
does not authorize public/protected/TypeScript-private or parameter-property rename.

The [member-reference benchmark](../benchmarks/member-references/README.md) records cache-cold /
warm medians of 12.97 / 1.44 ms for one source and 258.62 / 8.52 ms for 25 sources across four
configurations. Whole-graph checker calls were 1 / 0 and 3 / 0 respectively. These service-only
Windows ARM64 measurements include input validation and exclude transport, editor UI and CLR
metadata; cache-byte estimates are reported separately from managed-heap measurements.

## Ordinary TypeScript hover

Full mode shows checked declaration and occurrence types for ordinary names, parameters,
functions, named annotations and supported source class members. It preserves flow-narrowed
occurrence types and the exact public overload signatures retained by the checker. Private
member names and `super` use their own semantic facts rather than the result of a surrounding
call, assignment or brand check. Unresolved analysis supplies no semantic hover.

Unknown annotation names also make their affected declarations and uses unavailable; an
explicit checked `any` remains displayable. A qualified annotation's final name can show its
resolved type. Earlier qualifiers require their own exact binding proof. Unused lazy aliases
are not resolved just for display. Structural/dynamic member access remains outside the
source class hover domain described by the semantic and member provenance contracts.

Hover uses exact half-open UTF-16 source ranges and the client's preferred Markdown or plain
text format. Existing GUI, decorator and CLR hover keeps priority. Full-mode CLR member usage
and ordinary hover share the checked snapshot, including files that mix interop and ordinary
TypeScript. Interop-only continues to serve SharpTS-specific hover without ordinary results.

## Ordinary TypeScript completion

Full mode offers visible names in supported value/type contexts and accessible members of
checked class, namespace, interface and record receivers. Imports use their local alias;
shadowing, TDZ visibility, static/instance access and private/protected accessibility follow
the checker. Unknown or dynamic receivers supply no guessed members. Namespace value access
omits type-only exports. Existing GUI and decorator completion keeps priority in both modes.

Manual invocation and partial identifiers use plain text items with bounded type details and
exact UTF-16 replacement edits, including the suffix after the cursor. Full mode adds `.` and
`?` triggers to the existing SharpTS triggers. Item kinds honor the client's advertised set;
semantic completion does not require snippets or an item-resolution request.

Unfinished `receiver.` and `receiver?.` expressions use a fresh checked cursor graph from the
captured program. Repeated requests reuse it. The bounded parser repairs missing expression
parts; it does not repair arbitrary damaged enclosing scopes. Lexical completion refuses
documents with parse errors, and comments, literals, nonmember dots and unsupported contexts
return no semantic candidates. A bare `this.#` currently fails lexing; `this.` can still offer
accessible private names. Auto-imports, quoted-key rewrites, keywords and ordinary JSX tag or
attribute completion are outside this feature.

Unused lazy type aliases have no available completion type until the checker resolves them.
Some annotated source-class function returns currently check a call as `any` despite the
function's declared return type; such receivers supply no members. Inferred class returns and
checked structural returns can supply their proven members.

## Ordinary TypeScript signature help

Full mode shows captured public function, method and constructor candidates for the innermost
supported call or `new` argument list. Callable aliases/interfaces and generic instantiations
participate where the checker proves their signatures. Unknown annotation fallbacks cannot
become fabricated `any` parameters; legitimate checked `any` remains displayable. Existing
decorator signature help keeps priority in both modes.

The parser's real delimiters and top-level commas determine the active argument, including
nested expressions, templates, generic arguments and multiline calls. Each signature maps
extra rest arguments to its rest parameter. The client receives parameter label offsets and
per-signature active parameters only when it supports them. Truncated signatures have no
guessed active parameter. The existing `(` and `,` triggers are retained.

Unfinished `f(`, trailing argument gaps and `new C(` use the shared fresh cursor-analysis path.
Recovered or erroneous calls can show proven candidates without claiming an overload winner.
If an explicit type argument is unresolved, an available formal signature can remain visible
without presenting an unproved instantiation. Instantiated signatures borrow parameter names
only from their exact original signature when their parameter counts match.
`activeSignature` is present only for an exact complete checker selection; a client's default
first signature is presentation rather than a semantic decision. Runtime constructors whose
checker validates only their implementation retain public candidates without a public winner.

Completed literal arguments can show the enclosing signature. Comments, raw JSX text,
unrelated callback bodies, malformed literals and expressions without a real argument-list
delimiter supply no result. New CLR call help is limited to the checker's available candidates;
this feature does not add a reflected overload resolver or execute calls.
An unfinished grouping expression such as `f((1` is outside the parser's current recovery
domain and supplies no signature help.
If an enclosing call fails before the checker visits its arguments, an inner call has no
signature facts and supplies no result, even when cursor recovery can parse it.

## Shared analysis

Hover, completion, signature help, definition, references, lexical rename, and full diagnostics share completed analyses for the
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

Checked source documents also retain exact written syntax ranges for editor queries. Cursor
recovery creates fresh tokens, AST and checker facts through the same configured module
pipeline, including captured dirty dependencies. It never mutates a completed analysis or
borrows a previous receiver type. Cursor analyses share admission, cancellation and the eight
entry/64 MiB cache budget, with an additional maximum of four completed cursor entries. A
configured program that cannot be reconstructed supplies no recovered result.

The separate parse-only recovery cache retains at most 32 artifacts and 8 MiB within the shared
64 MiB estimate. Those artifacts supply syntax context and cannot prove authoritative rename
locations or semantic results on their own.

Completed analyses also retain [bounded semantic query values](editor-semantic-queries.md):
checked declaration/occurrence types, visible bindings, accessible receiver members and actual
call/new candidate decisions. Ordinary hover, completion and signature help consume these
values. They do not retain the checker's mutable environments.

For a local protocol smoke test after a Release build, run:

```bash
node scripts/test-analysis-snapshots.mjs
node scripts/test-semantic-hover.mjs
node scripts/test-semantic-completion.mjs
node scripts/test-semantic-signatures.mjs
node scripts/test-member-references.mjs
node scripts/test-private-rename.mjs
```

The test exercises real stdio navigation, source class member targets, watched closed-file
changes, dirty overlays, reverse importer creation, rename, cancellation transport and
interop-only capability isolation.
The hover smoke runs full/interop-only clients with both markup formats, checking exact
ordinary ranges, dirty imported types, close-to-disk restoration and retained CLR/decorator
hover.
The completion smoke checks full/interop-only triggers, ordinary and unfinished buffers,
applied CRLF/UTF-16 edits, restricted item kinds, dirty dependencies and retained decorator
completion.
The signature smoke checks full/interop-only clients, label/parameter capability negotiation,
unfinished and nested calls, public candidates versus selected signatures, dirty dependency
restoration, and request cancellation with a surviving fresh request.
The member-reference smoke covers independently configured projects, closed reverse importers,
dirty source ranges, completeness refusal, rename denial and cancellation/currentness.

For the aggregate protocol, formatter and packaged-tool check, use:

```powershell
pwsh ./scripts/test-editor-contract.ps1
```

The `editor-contract` CI job runs this same aggregate on Ubuntu and Windows. It records a unique
artifact index and logs, runs the six protocol suites above, formats/checks representative inputs,
then packs and installs the exact local language-server package into an isolated tool path and
checks its installed shim in both modes. These deterministic checks gate CI. Native editor runs
and controlled [editor-workflow measurements](../benchmarks/editor-workflow/README.md) are separate;
no wall-clock performance threshold gates shared CI hosts.

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

Full mode produces a lexical rename edit only when the server has loaded every configured
project root and project reference needed for the selected semantic binding. Incomplete graphs
refuse cross-file rename. Public, protected, TypeScript `private` string-keyed and structural
member rename remains unavailable.

Constructor parameter-properties, such as `constructor(public value: number)`, also refuse rename
from their declaration or constructor-local uses: those edits would need to coordinate the
property name and its uses. Ordinary constructor parameters retain lexical rename support when
the configured graph is complete.

ECMAScript `#private` rename has a separate one-document proof. The selected class declaration or
expression must have a complete checked private domain: every private token inside that exact
source owner must have authoritative original syntax and the same lexical class owner. Supported
operations include instance/static fields and methods, reads, plain writes, calls, closures and
`#name in candidate`. A class inside an ordinary function can qualify. An owner nested in another
class private environment or containing any nested class is refused.

Private rename needs a known open-document version and client support for
`workspace.workspaceEdit.documentChanges`. Prepare returns the whole `#old` range and placeholder;
rename accepts `new` or `#new`, validates the private identifier and collisions, and emits exactly
`#new` in versioned document changes. `#constructor`, malformed names and collisions with another
private instance/static declaration are refused. Private keyword spellings such as `#new` and
`#class` are legal when the parser accepts them. Existing lexical edits keep their client behavior.

The private domain does not require complete workspace discovery, so an unrelated broken project
does not block it. Unresolved private occurrences, unvisited source bodies, any target-document
parse error and recovered source refuse the entire operation. The current parser rejects private
compound/logical assignment and prefix/postfix updates; those buffers remain refused. Private
accessor/auto-accessor declarations, multiple-declaration private method groups and nested private
environments are outside this delivery. Final document, dependency and metadata validation can
discard the whole edit if the captured state changes.

Private-domain and handler verification adds 67 cases, including applied before/after behavior in
both runtimes; the broader affected Release suite passed 3,542 tests. Reproduce the focused and
real-protocol checks after building the Release server:

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --filter "FullyQualifiedName~PrivateRename"
node scripts/test-private-rename.mjs
```

The stdio check uses seven full/interop-only client configurations and verifies whole UTF-16
tokens, captured document versions, LF/CRLF, applied fresh diagnostics, explicit refusals and
ordinary lexical rename with versioned-edit support present, false or omitted.
