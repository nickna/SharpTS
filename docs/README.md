# SharpTS documentation

This is the task-oriented entry point for SharpTS documentation. For a concise product overview
and first program, start at the [root README](../README.md). For a feature-by-feature snapshot and
known gaps, use [STATUS.md](../STATUS.md).

## Get started

- [Execution modes and CLI](execution-modes.md) — interpret, compile, type-check projects, emit
  declarations, debug, inspect timings, and choose deployment output
- [Runnable examples](../samples/README.md) — the canonical cookbook, including modules, npm,
  interop, GUI, and hosting examples
- [npm compatibility](npm-compatibility.md) — the currently tested real-package matrix and how to
  run it

## Build projects and integrate with .NET

- [MSBuild SDK guide](msbuild-sdk.md) — canonical `SharpTS.Sdk` setup, properties, precedence, and
  build behavior
- [.NET integration](dotnet-integration.md) — compile a TypeScript library and consume it from C#
- [Use .NET types from TypeScript](dotnet-types.md) — `dotnet:` imports, external references,
  delegates, events, exceptions, and deployment boundaries
- [Embedding SharpTS](embedding.md) — managed compilation and bounded source-execution APIs
- [Native AOT](native-aot.md) — native binaries, the closed interop catalog, and custom hosts
- [`SharpTS.Hosting`](../src/SharpTS.Hosting/README.md) — package-level Native AOT host setup

## Node and JavaScript APIs

- [Node built-in API reference](node-modules-api.md) — supported user-facing module exports
- [Node capability status](../STATUS.md#4-nodejs-built-in-modules) — implementation breadth,
  deviations, and current gaps
- [Embedded standard-library contribution guide](../src/SharpTS/stdlib/CONTRIBUTING.md) — maintaining the
  TypeScript implementations shipped inside SharpTS

## Desktop GUI

- [GUI overview](gui/README.md) — platform status, supported public APIs, and documentation map
- [SDK development workflow](gui/sdk-development.md)
- [TSX API reference](gui/tsx-api.md)
- [Testing and developer tools](gui/testing-and-devtools.md)
- [Performance and retention](gui/performance.md)
- [Windows distribution](gui/windows-distribution.md)
- [macOS distribution](gui/macos-distribution.md)
- [Compatibility and support policy](gui/support-policy.md)

## Tooling and debugging

- [Debug interpreted TypeScript](debugging-interpreter.md)
- [Debug compiled TypeScript](debugging-typescript.md)
- [Language server](language-server.md)
- [External benchmark harness](../benchmarks/cross-runtime/README.md)
- [Managed microbenchmarks](../benchmarks/micro/SharpTS.Microbenchmarks/README.md)
- [Public performance snapshots](../benchmarks/snapshots/README.md)

## Contributor and maintainer material

- [Contributing](../CONTRIBUTING.md)
- [Architecture](../ARCHITECTURE.md)
- [Claude Code operating notes](../CLAUDE.md)
- [Release operations](releasing.md) and [release incident history](release-incidents.md)
- [Test262 runner](../tests/conformance/SharpTS.Test262/README.md)
- [TypeScript conformance runner](../tests/conformance/SharpTS.TypeScriptConformance/README.md)
- [TypeScript declaration resources](../src/SharpTS/Modules/TypeScriptLibResources/README.md)
- [GUI SDK package README](../src/SharpTS.Gui.Sdk/readme.md)
- [Core MSBuild SDK package README](../src/SharpTS.Sdk/readme.md)

### Design and planning records

- [Compact-record specialization and shaped-object outcome](plans/archive/shaped-objects-representation.md)
- [Number-array representation decision](design/number-array-unboxing.md)
- [Runtime tree-shaking outcome](plans/archive/runtime-tree-shaking-outcome.md)
- [Duplicate-logic consolidation](plans/archive/duplicate-logic-consolidation.md)
- [IL verifier resolution](plans/archive/issue-189-ilverifier-resolution.md)
- [String accumulator optimization](plans/archive/issue-857-string-accumulator-stringbuilder.md)
- [Typed array-method pipeline](plans/archive/issue-861-array-methods-typed-pipeline.md)
- [Sparse arrays in compiled mode](plans/archive/sparse-arrays-compile-mode.md)
