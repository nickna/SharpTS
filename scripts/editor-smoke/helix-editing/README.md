# Native Helix standalone editing smoke

This disposable workspace uses actual Helix LSP requests, native completion and
rename application, dirty buffers, and its external format-on-save command. It
does not implement an LSP client or add an editor provider. The server launches
with `--language-features full --diagnostics all`; pinned Prettier owns formatting.

Build the Release language server and install the formatter lockfile first. Run
`node scripts/editor-smoke/helix-editing/prepare.mjs /absolute/path/to/hx` with the
repository-pinned Node version. The command prints a fresh workspace and saves
exact server/editor/module paths, versions, source commit, server hash, initial
source and dirty stages in `test-config.json`. In that workspace launch:

```powershell
$env:HELIX_RUNTIME = '/absolute/path/to/helix/runtime'
& '/absolute/path/to/hx' -c config.toml --log helix.log -vv main.ts
```

The fixture maps insert-mode Ctrl-K to `signature_help` and disables automatic
completion/pairs. `<n>gg` and `<n>g|` select a one-based line and column. Use
Space-K for hover, `gd` for definition, `gr` for references and Space-R for rename.
Enter normal mode before each sequence; dismiss a hover/picker with Escape and
allow each asynchronous response to arrive. Follow the [official keymap](https://docs.helix-editor.com/keymap.html).

1. Hover `result` at `3gg7g|`. Select `add` at `3gg24g|` and use `gd`; Helix opens
   `dependency.ts`. Rename `#secret` at `2gg3g|` to `#renamedSecret` with Space-R,
   Ctrl-U, the new name and Enter. The native capability advertises versioned
   document changes; verify both declaration and use change in the dirty buffer.
2. In normal mode select the whole dependency with `%`, then run
   `:pipe node stage.cjs dependency-dirty`. Do not save. The helper supplies exact
   stdout to Helix's native pipe command; Helix sends its own versioned didChange.
   It adds `extra` while preserving the private rename. This avoids terminal paste
   and auto-indent differences and never communicates with the language server.
3. `:open main.ts`, `%`, `:pipe node stage.cjs main-completion`. At `6gg8g|` use
   `a`, Ctrl-X, wait for completion, then Ctrl-N to select `add`, Enter to accept,
   and type `(`. Ctrl-K requests the unfinished-call signature. Completion must
   contain `add`, the unsaved dependency's `extra`, and `value`.
4. Escape, `%`, `:pipe node stage.cjs main-bad`; wait for ordinary type diagnostics.
   Replace with `%` and `:pipe node stage.cjs main-valid`; wait for them to clear.
   At `4gg7g|` rename `lexical` to `renamedLocal` with the native rename prompt.
   Hover `result`, dismiss, `:write`, wait for current-version diagnostics, and
   hover `result` again. Prettier must format the actual dirty save.
5. `:open view.tsx`, `%`, `:pipe node stage.cjs view-dirty`. Hover `title` at
   `3gg7g|`, dismiss, `:write`, wait for current-version diagnostics, then hover
   again. For another dirty cycle, add trailing spaces to the title line with
   `3ggA`, two spaces and Escape, then repeat hover/save/hover.
6. Open the still-dirty dependency. At `5gg3g|`, `gr` must include the declaration
   and the main-file use. Dismiss the picker; hover the renamed private field at
   `2gg3g|`, save the dependency, and obtain a fresh hover. `:quit-all` shuts down.
7. Run `node scripts/editor-smoke/helix-editing/verify.mjs /absolute/workspace`.
   It correlates Helix's trace, reconstructs UTF-16 incremental document versions,
   proves applied edits, dirty dependency lookup, formal unfinished signatures,
   navigation, cleared ordinary diagnostics and exact pinned formatter bytes for
   every final saved file. The result requires fresh post-save hovers and native
   shutdown. It rejects LSP formatting and unsupported advertised providers.

`trace.mjs /absolute/workspace` prints reconstructed buffers and recent requests
for a failed manual run. Keep the native log, stage files, configuration and
result together. Timing is not an acceptance criterion; a response may correctly
be unavailable during a save/watch invalidation. Wait for the diagnostic for the
current document version and make a fresh request before recording success.

Recorded Windows ARM64 run: Helix 25.07.1 (`a05c151b`, x64), preparation/formatter
Node 22.23.2 ARM64, Prettier 3.9.9, SharpTS source `a20b07d7`. The successful
workspace is `artifacts/editor-contract/helix/run-ZAkEIw`; see `last-verified.json`.
The final TS, TSX and dependency diagnostics were empty.
This tests the sole-server recipe. Actual two-server ownership is covered by the
[Neovim smoke](../neovim-editing/README.md); Helix chooses the first eligible server
for hover/signatures and does not merge their results.
