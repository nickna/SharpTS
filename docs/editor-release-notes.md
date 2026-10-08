# Standalone editor support — unreleased

[Epic #1390](https://github.com/nickna/SharpTS/issues/1390) adds a useful standalone
editing workflow for SharpTS's supported TypeScript and TSX. Use
`sharpts-lsp --language-features full --diagnostics all` as the sole language server.
The shipped VS Code extension keeps its coexistence preset; another TypeScript
service owns ordinary language features there. See the [language-server guide](language-server.md)
and [formatter recipes](formatting.md) for exact configuration and tested clients.

Full mode now provides ordinary semantic hover, lexical and checked receiver
completion, complete and unfinished call/constructor signatures, source
definitions, checker-bound source member references, lexical rename and a narrow
safe ECMAScript-private rename. Completion replaces the full written identifier;
signature labels respect client UTF-16 capabilities. Private rename requires a
completely checked, supported class domain and versioned document edits. It edits
the entire `#name` token and refuses unknown, stale, nested or incompletely checked
domains. Public/structural, TypeScript-private/protected and parameter-property
member rename remain unavailable.

These consumers share immutable checked components and exact captured document,
configuration, resolution and CLR metadata inputs. Repeated unchanged requests
reuse analysis; dirty dependencies and physical changes invalidate it. Caret-local
completion/signature recovery can build a fresh component and remains bounded.
This is whole-component checking, with no incremental AST or checker reuse.
The completed-graph cache defaults to eight entries and an estimated 64 MiB,
including at most four checked cursor entries. Active leases, in-flight work,
runtime/CLR state and estimator error are outside that estimate; it is not a
process-memory cap. Unknown or ambiguous facts produce unavailable results rather
than invented bindings or overload winners.

Both modes retain SharpTS interop hover/completion/signatures and quick fixes.
`interop-only --diagnostics sharpts-only` leaves ordinary features to the other
TypeScript service. Feature mode is fixed during initialization; diagnostic mode
can change live. Explicit `diagnostics=all` requests full diagnostics in either
mode without enabling ordinary providers in coexistence mode. The raw CLI defaults
remain `full` and `sharpts-only`; standalone recipes explicitly opt into all
diagnostics. Neither mode supplies formatting, inlay hints, semantic tokens or
folding. Pinned external Prettier owns formatting.

The [aggregate contract command](../scripts/test-editor-contract.ps1) exercises six
stdio suites, formatter compatibility and an exact locally installed tool shim.
The full CI route runs it on Ubuntu and Windows and retains failure evidence.
Native [Neovim](../scripts/editor-smoke/neovim-editing/README.md) and
[Helix](../scripts/editor-smoke/helix-editing/README.md) checks exercise real editing,
native edit application and saves. The [shipping VS Code smoke](../scripts/editor-smoke/vscode-shipping/README.md)
activates the actual extension alongside built-in TypeScript and Prettier. Neovim's
two-server run verifies ordinary ownership and native combined CLR hover; Helix's
documented first-provider routing does not promise merged results. Older formatter
bridge runs establish formatter compatibility only.

Reproducible [workflow measurements](../benchmarks/editor-workflow/README.md)
separate startup, cache-cold and warm requests, changed versions/carets, payloads,
allocations, actual checker counts, cache estimates and managed-heap observations.
Existing-path comparisons use their exact historical baselines; newly available
features report absolute cost. Timing comparisons do not gate CI.

The supported/refused matrix and evidence links in the language-server guide are
the release contract. This work does not promise TypeScript language-service parity.
