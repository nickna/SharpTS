# Plan: Replace the LspBridge with a real Language Server (Direction B)

**Status:** In progress. Landed: Phase 0, Phase 1 (interop analyzer + project refs),
LspBridge teardown, Phase 2 (decorator hover, completion, CLR-type-name completion,
signatureHelp), Phase 3 (extension rewrite), Phase 4a (token-based interop navigation),
source spans and document symbols, plus the first Phase 4b slice: checker-resolved local-value
go-to-definition. The server lives in the separate `SharpTS.LanguageServer` executable /
`sharpts-lsp` tool, keeping OmniSharp out of the core package. Remaining: type-namespace and
cross-module definition, references, safe rename, and Phase 5 lifecycle/polish.
**Author:** investigation + design pass, 2026-06-22
**Decision:** Throw away the bespoke `LspBridge` JSON protocol and the hand-rolled
VS Code providers. Stand up a genuine LSP server over SharpTS's own
`TypeChecker`, consumed by a thin `vscode-languageclient`-based extension.

---

## 1. Goal

Today `LspBridge/` is **not** an LSP server — it is a bespoke line-delimited JSON
protocol that does one thing: reflect over referenced .NET assemblies to power
`@DotNet*` decorator IntelliSense. Meanwhile SharpTS has a full `TypeChecker`
producing `Diagnostic` records with `SourceLocation` and `TSnnnn` codes that the
editor never sees.

This plan replaces the bridge with a standard LSP server so that:

1. **SharpTS's own type checker drives live diagnostics** in the editor.
2. **One server, many editors** — Neovim / Helix / Zed / Emacs work for free over
   stdio LSP, not just VS Code.
3. The bespoke transport, client-side process management, regex decorator parsing,
   and TTL cache all **disappear** — `vscode-languageclient` owns transport,
   restart, cancellation, and request lifecycle.

### Locked decisions (from design review)

| Decision | Choice | Consequence |
|----------|--------|-------------|
| Coexistence with VS Code's built-in TS server | **Complement** | We add value on top of `tsserver`; we do not replace it. We must filter our diagnostics to avoid double squiggles (see §4). |
| Server transport / framework | **OmniSharp.Extensions.LanguageServer** | Batteries-included .NET LSP SDK: strongly-typed handlers, lifecycle, capability negotiation, cancellation. |

---

## 1a. Spike results (2026-06-22) — read before building

A de-risking spike measured the three things the plan was assuming. Two passed; one
**reshaped Phase 1**.

**(c) OmniSharp on net10 — PASS.** A throwaway host builds clean on net10 (0 errors)
and completes a real `initialize → initialized → shutdown/exit` handshake at runtime,
returning an `InitializeResult` with capabilities. Cost is real: the transitive
closure is **22 DLLs / ~6 MB** (MediatR, System.Reactive, Nerdbank.Streams, the
OmniSharp packages) — heavier than anything currently in the repo, but it resolves
and runs cleanly.

**(b) Parse+check latency — fine for typical files, watch large graphs.** Median
single-file parse+check: `algorithms.ts` (161 ln) **1.98 ms**, `password-generator.ts`
(187 ln) **1.74 ms**, `web-server.ts` (431 ln) **3.48 ms**. But a synthetic file of
**400 top-level functions (401 ln) took 33.9 ms** — ~10× a real file of the same line
count. Cost scales with **declaration/symbol count, not lines**, with a super-linear
hint. Conclusion: single files are comfortably interactive; the risk is a large module
graph re-checked whole on every keystroke (no incremental checking exists). Debounce +
cancel + graph-scoping (§7) are load-bearing, not optional.

**(a) Diagnostic yield under "complement" — the reframe.** Measured what survives the
`TsCode` filter on real error cases:
- Ordinary type errors (`TS2322` assignability, `TS2304` undefined var, …) **all carry
  a `TsCode` → all suppressed** (tsserver already shows them).
- The null-`TsCode` "published" remainder is a thin grab-bag: SharpTS quirk messages
  (e.g. "Import statements require module mode" — which won't even occur once we use the
  module path, §5.1) and **parse errors**, which tsserver reports anyway.
- The genuinely unique, tsserver-impossible diagnostics — `@DotNetType` type-not-found,
  unresolved interop members/overloads — are emitted at **interpret time**
  (`Execution/Interpreter.DotNet.cs:32`) and **compile time**
  (`Compilation/ILEmitter.Calls.ExternalInterop.cs`, `CompileException`), **not** by the
  type checker. `TypeChecker` treats `@DotNetType` as an opaque `ExternalDotNetType` and
  only validates decorator *syntax* (`TypeChecker.Decorators.cs` `BuiltInDecorators`).

**Consequence:** "Phase 1 = surface the existing type checker's diagnostics" delivers
**almost nothing** under the complement model. The real value requires a **net-new,
check-time `@DotNet*` interop analyzer** in the LSP layer (validate .NET type/member
names against the loaded assembly metadata the `DecoratorService` already holds, emit
null-`TsCode` diagnostics). That is the differentiator tsserver structurally cannot
provide. Phase 1 is re-scoped accordingly (§5.2). The pure-reuse path is not the win;
the interop analyzer is.

> If surfacing SharpTS's *full* type checking (the suppressed `TsCode` diagnostics) is
> actually the goal, "complement" defeats it and the deferred **"own SharpTS files"**
> coexistence option (§13) is the only way to show them without double-squiggling.

---

## 2. What we keep vs. throw away

### Keep / reuse (the genuinely valuable core)
- `Compilation/AssemblyReferenceLoader.cs` — `MetadataLoadContext` reflection (inspect-only, no execution, `IDisposable`). The decorator features depend on this.
- `LspBridge/Documentation/XmlDocLoader.cs` — XML-doc extraction → move to `LanguageServer/Documentation/`.
- `LspBridge/Project/CsprojParser.cs` — `.csproj` → assembly path resolution → move to `LanguageServer/Project/`.
- The type-check pipeline: `Lexer` → `Parser` → `TypeChecker.CheckWithRecovery` / `CheckModules`, plus `ModuleResolver`'s `virtualFiles` overlay.

### Throw away
- `LspBridge/LspBridge.cs` (the bespoke message loop).
- `LspBridge/Protocol/BridgeRequest.cs`, `BridgeResponse.cs`.
- `LspBridge/Handlers/ICommandHandler.cs` and the four handlers — **logic ported** into server services, the *protocol* deleted.
- CLI: the `lsp-bridge` subcommand (`ParsedCommand.LspBridge`, `ParseLspBridgeCommand`, `RunLspBridge`) → replaced by `sharpts lsp`.
- VS Code client: `src/bridge/*` (`BridgeClient`, `BridgeCache`, `BridgeProtocol`) and `src/features/{Completion,Signature,Hover}Provider.ts` — all become server-side LSP capabilities.
- `SharpTS.Tests/LspTests/*` — these test the deleted JSON protocol.
- `DeclarationGenerator.ts`'s dynamic per-assembly `.d.ts` generation (the N+1 round-trip + workspace pollution). Replaced by a **static** bundled `sharpts.d.ts` for the six builtin `DotNet*` decorators (so `tsserver` doesn't flag them as undefined); live attribute info now comes from the server.

---

## 3. Architecture

```
        VS Code (or Neovim/Helix/Zed/Emacs)
                  │  LSP over stdio (JSON-RPC 2.0)
                  ▼
   sharpts lsp   ──────────────────────────────────────────────
   (OmniSharp.Extensions.LanguageServer host)
     ├─ TextDocumentSyncHandler ── DocumentStore (uri → text+version)
     ├─ DiagnosticsService ─┐
     │                      ├─ Lexer → Parser → TypeChecker
     │                      │   (ModuleResolver with virtualFiles = open docs)
     │                      └─ SourceLocation → LSP Range + TsCode filter
     ├─ CompletionHandler  ─┐
     ├─ HoverHandler       ─┼─ DecoratorService
     ├─ SignatureHelpHandler┘   (AssemblyReferenceLoader + XmlDocLoader)
     └─ DefinitionHandler ──── PositionIndex   (Phase 4)
                  ▲
   WorkspaceContext: AssemblyReferenceLoader + project refs,
   reloaded on .csproj / bin change (file watcher)
```

### Proposed directory layout (`LanguageServer/`, replacing `LspBridge/`)

```
LanguageServer/
  SharpTSLanguageServer.cs        // host bootstrap: LanguageServer.From(...)
  Workspace/
    DocumentStore.cs              // open docs; produces virtualFiles overlay
    WorkspaceContext.cs           // AssemblyReferenceLoader lifetime + reload
  Services/
    DiagnosticsService.cs         // source/overlay → filtered LSP diagnostics
    DecoratorService.cs           // ports List/Info/Documentation handler logic
    PositionIndex.cs              // (Phase 4) position → AST node → type
  Handlers/
    TextDocumentSyncHandler.cs
    CompletionHandler.cs
    HoverHandler.cs
    SignatureHelpHandler.cs
    DefinitionHandler.cs          // (Phase 4)
  Conversions/
    LspConversions.cs             // SourceLocation↔Range, severity, end-span synth
  Documentation/  XmlDocLoader.cs // moved from LspBridge
  Project/        CsprojParser.cs // moved from LspBridge
```

---

## 4. Coexistence model: complementing `tsserver`

The user edits plain `.ts` files, where VS Code's built-in TypeScript server is
already producing diagnostics, hover, and completion. Our server runs **alongside**
it. The risk is duplicate or conflicting diagnostics, because SharpTS's checker is
not byte-for-byte `tsc`.

**The filter — use the existing `Diagnostic.TsCode` field.** Per `Diagnostics/Diagnostic.cs`,
every diagnostic that mirrors a `tsc` diagnostic carries a canonical `TSnnnn` code,
while *SharpTS-only* diagnostics (the comment explicitly calls out `@DotNetType`
errors) leave `TsCode` null. So:

- `TsCode != null` → `tsserver` already reports the equivalent → **suppress** (default).
- `TsCode == null` → SharpTS-specific (e.g. .NET interop / decorator errors) → **publish**.

Expose this as a setting so users who *don't* run `tsserver` (or who want SharpTS as
the source of truth) can opt into everything:

```jsonc
"sharpts.diagnostics": "sharpts-only" | "all" | "off"   // default: "sharpts-only"
```

**Ambient declarations.** Ship a *static* `sharpts.d.ts` (the six builtin `DotNet*`
decorator signatures) referenced via the extension so `tsserver` doesn't underline
the decorators. Live, per-assembly attribute info comes from `DecoratorService`, not
a generated file — this kills the current N+1 generator and the git-polluting
workspace write.

---

## 5. Server design — capability by capability

### 5.1 Document sync + dirty-buffer overlay  (the foundation)
- `TextDocumentSyncHandler` maintains `DocumentStore`: `uri → (text, version)`.
  Use **incremental** sync (apply `contentChanges` ranges) to avoid resending whole
  files; OmniSharp gives us the change events.
- On every relevant change, build the overlay: `IReadOnlyDictionary<string,string>`
  of `absolutePath → currentText` for all open docs, and pass it as the
  `virtualFiles` arg to `new ModuleResolver(workspaceRoot, overlay)`
  (`Modules/ModuleResolver.cs:67`). All reads route through
  `ResolverFileExists`/`ResolverReadAllText` (lines 89, 103), so **unsaved edits are
  type-checked without writing to disk** — no VFS layer to build.

### 5.2 Diagnostics  (Phase 1)
> **Re-scoped after the spike (§1a).** Under "complement", reusing the type checker's
> diagnostics publishes almost nothing tsserver doesn't already show. The headline
> value of Phase 1 is the **`@DotNet*` interop analyzer** below, not diagnostic reuse.

**5.2a — `@DotNet*` interop analyzer (the actual Phase 1 win — net-new).**
Walk the parsed AST for `@DotNetType` / `@DotNetMethod` / `@DotNetProperty` / external
(`declare class`) declarations and the checker's `ExternalDotNetType` annotations, and
validate each against the **already-loaded** `AssemblyReferenceLoader` metadata that
`DecoratorService` holds: type names resolve, referenced members/overloads exist, etc.
Emit these as null-`TsCode` diagnostics (`source = "sharpts"`). This reproduces, at
*check* time, the errors that today only surface at interpret time
(`Interpreter.DotNet.cs`) / compile time (`ILEmitter.Calls.ExternalInterop.cs`) — the
unique, tsserver-impossible value. Running the interpreter or IL compiler per keystroke
is a non-starter, so this is a purpose-built static resolver, not reuse.

**5.2b — type-checker diagnostics (secondary; gated by `sharpts.diagnostics`).**
For `"all"` mode (users who want SharpTS as source of truth and accept divergence
noise), also run the normal check and publish unfiltered. In the default
`"sharpts-only"` mode this is suppressed to the interop analyzer's output. Pipeline:
- `DiagnosticsService` per changed document:
  - **No imports** (`!source.Contains("import "/"export ")`, mirroring
    `Program.cs` `RunFile`): `new Lexer(src).ScanTokens()` →
    `new Parser(tokens, decoratorMode).Parse()` →
    `new TypeChecker(strictNullChecks, maxErrors: int.MaxValue, …)` →
    `CheckWithRecovery(statements)` → `.Diagnostics`.
    **Note:** the `TypeChecker` `maxErrors` default is 10 — pass a large value so the
    editor isn't capped at 10 squiggles.
  - **With imports**: `ModuleResolver(root, overlay)` →
    `LoadModule` → `GetModulesInOrder` → `checker.CheckModules(modules, resolver)` →
    `checker.GetDiagnostics()`. Publish diagnostics grouped by each module's URI so
    errors in imported open files light up too.
  - Always include parser diagnostics (`ParseDiagnosticResult`) so syntax errors show.
- Convert via `LspConversions` (§6), apply the `TsCode` filter (§4), and
  `PublishDiagnostics` per URI.
- **Fresh `TypeChecker` per check.** The explore pass flagged the checker as
  single-threaded with instance-level mutable state (`_environment`, caches reset on
  entry). A fresh instance per check sidesteps cross-request contamination; allocation
  is cheap relative to a parse+check.

### 5.3 Decorator features: completion / hover / signature  (Phase 2)
Port the three live handlers into `DecoratorService`, backed by the **reused**
`AssemblyReferenceLoader` + `XmlDocLoader`:

| LSP capability | Current handler logic to port | Trigger |
|----------------|-------------------------------|---------|
| `completion` (trigger char `@`) | `ListAttributesHandler` (collect `System.Attribute` subtypes, strip `Attribute` suffix) | line text before cursor matches `@(\w*)$` |
| `signatureHelp` (trigger `(` `,`) | `GetAttributeInfoHandler` (ctor params, optional/default) | inside `@Name( … `, count depth-0 commas for active param |
| `hover` | `GetDocumentationHandler` + `XmlDocLoader.GetTypeSummary` | word at position matches `@[\w.]+` |

These need only the **current line text** (already in `DocumentStore`), not the full
position index — so they ship before Phase 4. Fidelity equals today's extension; the
difference is the logic now lives once, server-side, editor-agnostic.

Also surface the half-built **named-arg properties** (`GetAttributeInfoHandler`
returns settable properties that the old client never used) as additional completion
items inside the decorator parens — small, real UX improvement.

### 5.4 General navigation (Phase 4)

The reference-keyed `SpanTable` and per-file `SourceDocument` have now landed. Document symbols
use statement spans, while definition uses a checker-produced `BindingIndex`: independent value
and type identities are attached when `TypeEnvironment` defines declarations and uses are captured
at the same lookup seam. The language server checks the resolver's real dependency graph, overlays
dirty open documents on disk, and carries source identities through named/default imports and
re-exports. This makes hoisting, shadowing, parameters, overload merging, aliases, and module
provenance follow the real checker instead of a second editor-only scope walker.

`textDocument/references` now uses the inverse side of that same index. It unions type/value facets
only when the selected token has both and honors `includeDeclaration`. The navigation model now
loads every root in the nearest `tsconfig.json` using that project's module-resolution, JSX,
strictness, declaration-library, and ambient-type settings. An explicit reverse-edge map then
selects the connected component, so references include closed importers in the configured project
without merging unrelated script globals. The model records whether every configured root loaded;
missing/broken configs and the open-document-only fallback are explicitly incomplete so safe rename
can refuse them rather than silently emitting a partial edit.

For multi-project workspaces, the server captures the initialized workspace folders, discovers each
`tsconfig.json` (skipping dependency/build/tool directories), and follows in-workspace project
references including explicitly named config files. Each project keeps its own module-resolution
and checker options. References are joined across those independent semantic models by canonical
declaration document/offset/namespace identity, so consuming projects with different `paths`
mappings are both covered. A malformed config, failed root, or project reference outside the
workspace makes the aggregate graph explicitly incomplete while preserving valid navigation
results.

The server advertises these features only in `--language-features full`; the VS Code extension
continues to launch `interop-only`, leaving ordinary TypeScript navigation to `tsserver`.

Still required for the complete navigation milestone: safe rename and the remaining type/member
domains (notably type parameters and qualified namespace/property members). Property/member
definition remains deliberately absent until it can resolve a complete semantic domain.

---

## 6. Coordinate & diagnostic conversion (`LspConversions`)
- **LSP is 0-based** for line and character; `SourceLocation` is **1-based** (see
  `Diagnostics/SourceLocation.cs`). So `lspLine = Line - 1`, `lspChar = Column - 1`.
- **End span:** `EndLine`/`EndColumn` are nullable and often absent. When null,
  synthesize a sensible range: extend to end of the token/word at the start position,
  or to end-of-line as a fallback. (Phase 4's spans make this exact.)
- **Severity:** map `DiagnosticSeverity` → LSP `DiagnosticSeverity`.
- Set `code` from `Diagnostic.Code`/`TsCode` and `source = "sharpts"` so users can
  tell our diagnostics from `tsserver`'s.

---

## 7. Concurrency, lifecycle & performance
- **Debounce** document-change checks (~250–300 ms) and **cancel** in-flight checks
  via the per-request `CancellationToken` OmniSharp supplies. (Today's providers
  ignore cancellation entirely and use 30 s timeouts.)
- **Scope re-checks** to the module graph reachable from the changed/open files;
  don't re-check the whole workspace on every keystroke.
- **WorkspaceContext** owns one `AssemblyReferenceLoader` per workspace, built from
  `sharpts.projectFile`/`additionalReferences` via `CsprojParser`. Reload it on a
  file-watcher event for the `.csproj` or the referenced `bin/` outputs (replaces the
  current "config changed → please restart the bridge" prompt).
- No incremental type-checking exists; full parse+check per change is the model.
  Acceptable for typical files with debounce; flagged as a risk (§10) for very large
  module graphs.

---

## 8. CLI changes
- Replace `sharpts lsp-bridge […]` with `sharpts lsp [--stdio] [--project <csproj>] [-r <dll>] [--sdk-path <p>]`.
  Default transport stdio. Reuse the existing arg parsing in
  `Cli/CommandLineParser.cs` (`ParseLspBridgeCommand` → `ParseLspCommand`).
- `Program.cs`: `RunLspBridge` → `RunLanguageServer`, which builds
  `WorkspaceContext` from `--project`/`-r` and starts the OmniSharp host
  (`await LanguageServer.From(opts => opts.WithInput(Console.OpenStandardInput())…)`).
- Drop the bespoke "ready" handshake — LSP `initialize`/`initialized` replaces it.

---

## 9. Client (VS Code extension) rewrite
The extension shrinks dramatically. New `extension.ts` (~50–70 lines):
- Add dependency `vscode-languageclient`.
- `ServerOptions`: spawn `dotnet <bundledSharpTS.dll> lsp --stdio` (plus `--project`
  from `sharpts.projectFile`). Allow a `sharpts.dotnetPath` setting (today `dotnet`
  is assumed on PATH with no override).
- `LanguageClientOptions`: `documentSelector` for `typescript` + `typescriptreact`;
  forward `sharpts.*` settings via `synchronize`/`initializationOptions`.
- Keep `commands/CompileCommands.ts` as-is (compile/run are not LSP concerns).
- Ship the static builtin `sharpts.d.ts` (no generator).
- Gate `activationEvents` so we don't spawn a server in every TS project — e.g.
  `workspaceContains:**/sharpts.json` or presence of `@DotNet` usage, plus an explicit
  "SharpTS: Start" command. (Today it activates on *any* `.ts` file.)
- `LanguageClient` handles restart, status, cancellation, and request correlation —
  delete `BridgeClient` (256 lines), `BridgeCache` (66), `BridgeProtocol` (65), and
  the three provider files.

### Multi-editor packaging
Because the server is standard stdio LSP, document a Neovim/Helix/Zed/Emacs setup
(command = `dotnet SharpTS.dll lsp --stdio` or a published self-contained binary).
Consider packaging the server as a `dotnet tool` (`sharpts`) or a self-contained
single-file binary so non-VS-Code editors don't need the full SDK.

---

## 10. Risks & mitigations
| Risk | Mitigation |
|------|------------|
| Double/conflicting diagnostics with `tsserver` | `TsCode`-based filter defaulting to SharpTS-only (§4); `sharpts.diagnostics` setting. |
| Full re-check per keystroke is slow on large module graphs | Debounce + cancel + scope to reachable graph; fresh-but-cheap `TypeChecker`; budget/skip if a check exceeds a threshold. |
| Hover/go-to-def needs AST spans that don't exist | Deferred to Phase 4; Phases 1–3 don't depend on it. Spans double as the fix for imprecise diagnostic columns. |
| OmniSharp SDK dependency weight / protocol lag | In keeping with existing dep appetite; if it bites, the fallback is StreamJsonRpc + `Microsoft.VisualStudio.LanguageServer.Protocol`. |
| `MetadataLoadContext` holding stale metadata after a rebuild | File-watch `.csproj`/`bin`; dispose + rebuild `AssemblyReferenceLoader`. |
| Decorator detection via line-text heuristic (no AST yet) | Same fidelity as today's regex; replace with AST-driven detection once Phase 4 spans land. |
| Server bundle = whole SharpTS.dll (heavy VSIX) | Acceptable v1 (already true today); revisit a trimmed server assembly later. |

---

## 11. Phased delivery

- ✅ **Phase 0 — Scaffold.** OmniSharp SDK added; `sharpts lsp` CLI; OmniSharp host;
  `LanguageServer/` tree. *(commit: language server + interop diagnostics)*
- ✅ **Phase 1 — Interop diagnostics (re-scoped per §1a).** `DocumentStore`;
  the **`@DotNet*` interop analyzer** (Tier 1/2/3a–d); conversions; `publishDiagnostics`.
  Plus **Phase 1b**: project-reference wiring (`--project`/`-r` → `AssemblyReferenceLoader`).
  9 analyzer tests; verified end-to-end (didOpen/didChange) against the real binary.
- ✅ **Teardown.** `LspBridge` protocol + handlers + tests deleted; `CsprojParser`/
  `XmlDocLoader` relocated into `LanguageServer/`; `lsp-bridge` CLI removed.
- ✅ **Phase 2 — Decorator IntelliSense.** `DecoratorService` + `HoverHandler` /
  `CompletionHandler` / `SignatureHelpHandler`: hover on `@DotNetType("X")` shows the resolved
  CLR type + XML doc; hover on a builtin decorator shows its description; completion after `@`
  offers builtins, and inside `@DotNetType("…")` offers CLR type names from referenced
  assemblies; signatureHelp for builtin decorator calls. 12 tests + e2e (hover/completion/sig).
  *Member-level hover (hovering a method inside a `@DotNetType` class) needs Phase 4's position
  index and is deferred there.*
- ✅ **Phase 3 — Extension rewrite.** `vscode-languageclient` over `dotnet … lsp`;
  `dotnetPath`/`projectFile`/`additionalReferences` settings; bridge client + providers
  + DeclarationGenerator deleted; compile/run kept. Compiles clean (tsc).
- ✅ **Phase 4a — token-based interop navigation (§14).** `PositionMap` (offset→line/col from
  `Token.Start`); precise diagnostic columns in `InteropAnalyzer`; `MemberHoverService` —
  member hover for `@DotNetType` members (declarations token-based; usages via the type
  checker's `TypeMap`, mapping the receiver's class name back to the binding). No parser
  surgery, no record-equality risk. ~7 tests + e2e (precise columns, declaration + usage hover).
- 🟨 **Phase 4b — standalone general navigation.** Reference-keyed source spans, document
  symbols, semantic value/type binding identities, and module-aware go-to-definition through
  imports/re-exports have landed. The inverse binding index, configured-project reverse graph,
  closed-importer references, multi-config/project-reference workspace expansion, and explicit
  graph-completeness boundaries have also landed. Safe rename, type-parameter navigation, and
  qualified member navigation remain. VS Code stays in `interop-only`; these capabilities are for
  standalone clients.
- ⬜ **Phase 5 — Polish & reach.** Multi-editor docs; `dotnet tool`/self-contained
  packaging; debounce/cancellation; config→server wiring (`sharpts.diagnostics`);
  loader reload on `.csproj`/`bin` change; STATUS.md/README updates. **NOT YET DONE.**

---

## 12. Testing strategy
- **Delete** `SharpTS.Tests/LspTests/*` (they test the removed protocol).
- **Service unit tests** (no transport): feed source + overlay to `DiagnosticsService`
  and assert filtered LSP diagnostics (ranges, severities, `TsCode` filtering);
  feed position + line text to `DecoratorService` and assert completion/signature/hover.
- **LSP integration tests:** drive the server in-process via OmniSharp's test harness
  (or a JSON-RPC client over a pipe): assert `initialize`, `didOpen → publishDiagnostics`,
  `completion`, `signatureHelp`, `hover`.
- **Extension smoke test** (optional, low priority): `@vscode/test-electron` — open a
  fixture, assert diagnostics arrive.
- Keep the standalone Test262 / TS-conformance suites untouched (orthogonal).

---

## 13. Open decisions for later (not blocking Phase 1)
- Server packaging for non-VS-Code editors: `dotnet tool` vs. self-contained binary.
- Whether to keep a `sharpts.diagnostics: "all"` users' experience tuned (ordering vs
  `tsserver`), and whether to offer a "SharpTS owns this workspace" mode later
  (the "Own SharpTS files" option we deferred).

---

## 14. Phase 4a design — token-based interop navigation (decided 2026-06-22)

**Scope decision:** Phase 4 splits. **4a (now):** interop-focused, *token-based* — no
parser-wide AST-span surgery. **4b (deferred):** general TS hover/go-to-def (parser-wide
spans + position→`TypeMap` index + symbol table) — only if it's worth competing with
`tsserver`, which already does TS navigation. Go-to-definition is dropped from 4a
(`@DotNetType` members compile to .NET — no source target).

**Key enabling fact:** the lexer sets `Token.Start` (global char offset) on *every* token
(`Lexer.AddToken` → `new Token(…, _tokenStartLine, _start)`), and `Lexeme` gives length. So
any token-backed AST node yields a precise `[Start, Start+len)` span. No `SourceSpan` field
on records (avoids the value-equality landmine), no parser changes.

**4a deliverables:**
1. **`PositionMap`** (`LanguageServer/PositionMap.cs`) — precompute line-start offsets from
   the document text; `ToLineCol(globalOffset) → (line0, col0)`; `Span(Token) → SourceLocation`
   (start + end from `Start`/`Lexeme.Length`).
2. **Precise diagnostic columns** — `InteropAnalyzer` takes an optional `PositionMap` and
   emits token-precise ranges (member `Name` token for Tier 2/3b/3c/3a; the `@DotNetType`
   decorator span — `AtToken`→callee-name token — for Tier 1; the `addEventListener` `Get.Name`
   token for Tier 3d). Falls back to line-only when no map (keeps existing unit tests green).
   `DiagnosticsService` builds the map from the buffer text and passes it.
3. **Member hover — declarations** (token-based, no type check): walk `@DotNetType` classes;
   if the cursor is on a member's `Name` token, resolve that member on the class's CLR type
   (reuse `DotNetTypeRegistry.GetMethods`/`GetPropertyOrField`) and render its real signature +
   overloads + XML doc (`XmlDocLoader.GetMethodSummary`/`GetPropertySummary`).
4. **Member hover — usages** (uses `TypeMap`): run `CheckWithRecovery` to get the `TypeMap`;
   if the cursor is on a member-access `Expr.Get.Name` token, look up the receiver
   (`getNode.Object`) in the `TypeMap` → unwrap to `ExternalDotNetType` (carries `ResolvedType`)
   → resolve + render the member. Receiver position detection is token-based (the `.Name`
   token), so no interval-tree index is needed.

`HoverHandler` tries decorator hover (existing), then member hover. New `MemberHoverService`
holds the resolver + `XmlDocLoader`; reuses the interop analyzer's resolution semantics.
