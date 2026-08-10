# Claude Code repository guidance

Use this file only for Claude Code-specific operating notes. Canonical subsystem boundaries and
invariants are in [ARCHITECTURE.md](ARCHITECTURE.md); build, test, and contribution conventions are
in [CONTRIBUTING.md](CONTRIBUTING.md); task-oriented user documentation starts at
[docs/README.md](docs/README.md).

## Common commands

```bash
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~SomeTest"
dotnet run -- script.ts
dotnet run -- --compile script.ts
dotnet run -- --compile script.ts --verify
```

The Test262 and TypeScript-conformance projects are not part of the solution-level test run. Follow
[`SharpTS.Test262/README.md`](SharpTS.Test262/README.md) and
[`SharpTS.TypeScriptConformance/README.md`](SharpTS.TypeScriptConformance/README.md) when a change
affects their domains.

## Working rules

- Add normal language/library behavior tests to `SharpTS.Tests/SharedTests` so interpreter and
  compiled modes both run. Use backend-only data only for a documented backend boundary.
- When adding an AST node, update the explicit node catalog and every applicable parser/checker/
  interpreter/emitter dispatch site; registry tests name omissions.
- Keep `TypeEnvironment` and `RuntimeEnvironment` separate.
- Do not emit a metadata token that references a SharpTS implementation type into compiled guest
  output. Follow the emitted-runtime dependency rules in
  [ARCHITECTURE.md](ARCHITECTURE.md#emitted-runtime-constraint).
- Record real soft runtime requirements through the existing capability mechanism. Do not mark
  pure-BCL helpers or graceful-fallback-only probes as required.
- Preserve structured diagnostics in core services; console formatting and exit codes belong in
  the CLI.
- Keep embedded Node module declarations, interpreter values, compiled emitters, and dual-mode
  tests synchronized. Follow [`stdlib/CONTRIBUTING.md`](stdlib/CONTRIBUTING.md).
- Benchmark changes with the harness appropriate to the question; see
  [`benchmarks/README.md`](benchmarks/README.md) and
  [`SharpTS.Microbenchmarks/README.md`](SharpTS.Microbenchmarks/README.md).

Before handing off a change, run the narrow relevant tests, `git diff --check`, and inspect the
worktree for generated or unrelated edits.
