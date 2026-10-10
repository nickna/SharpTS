# Debugging compiled TypeScript

`sharpts --compile app.ts -g` (or `--debug`) emits `app.dll` plus a portable `app.pdb` whose
documents and sequence points refer to the original `.ts` files. Any debugger that understands
portable PDBs — the C# extension for VS Code, Rider, Visual Studio, `netcoredbg` — can then bind
breakpoints in TypeScript source and step through it.

```bash
sharpts --compile app.ts -g       # app.dll + app.pdb
sharpts --compile app.ts          # app.dll only; no debug directory, no PDB cost
```

Symbols are attached after assembly-reference rewriting, so `--ref-asm` builds and programs that
reach into the SharpTS runtime carry working symbols too. Keep the `.pdb` beside the `.dll`.

## What you can expect today

Breakpoints bind and stepping follows executable TypeScript statements, in ordinary functions,
class members, module top level, `async` functions, and generators — the state machines go through
the same emission path, so their `MoveNext` bodies carry line information for the statements you
wrote. Portable-PDB state-machine mappings and async suspension/resume records let managed
debuggers present kickoff methods and step across `await` without exposing raw plumbing. Imported
modules resolve to their own files.

Statements the compiler synthesized are marked hidden and stepped over rather than attributed to a
nearby line: the `var` declarations hoisting moves to the top of a body, the aliases left where a
nested function was relocated, and the declarations generator-arrow lifting creates.

A brace never takes a stop on its own. `{ … }` blocks, the sequences a lowering returns, and `try`
emit no instructions of their own, so the first real statement inside them owns that position.
Conditions do execute, so `if`, `while`, `for`, and `switch` headers keep their own points.

Locals show under the names you wrote, over the range they are actually in scope — a `let` or
`const` loop binding is offered inside its loop and not outside it, while `var` retains its function
scope. A shadowing inner `let` resolves ahead of the outer one. Temporaries the compiler introduces
(destructuring scratch slots and similar) are marked hidden and stay out of the locals window.

This also covers variables that survive `await` or `yield` in async functions, generators,
async generators, and async arrows. Debug builds describe each hoisted source binding and its
lexical lifetime, including the shared storage used by closures. The VS Code C# debugger presents
those bindings in Locals and resolves source names in Watch before and after suspension. Inner
shadows resolve to the inner binding; leaving a block or catch restores the outer binding or makes
the name unavailable. Compiler state, awaiters, spill fields, and hidden scratch slots stay out of
that presentation.

Watch uses the C# expression evaluator. Escape a TypeScript name that is a C# keyword with `@`:
for a binding named `int`, evaluate `@int` (also its Locals display name); bare `int` fails to
parse. The tested source name `$dollar` evaluates directly in Watch.

The #1985 hoisted-local acceptance was verified at 55 stops with VS Code 1.140.0 and C# 2.140.9 on Windows
x64: all 123 expected Locals values matched, with exactly one entry for each binding. The separate
`netcoredbg` 3.2.0-1092 comparison passed the same 55 stops on the same assembly: all 123 first Locals
values, expected Watch values, and lexical visibility matched, and no empty-name entries or compiler
scaffolding appeared. Its Locals list still duplicates names at seven shadowing stops, including
three loop iterations; the first Locals entry and Watch select the correct binding. Boxed numbers
may require expanding `m_value`. Earlier empty-name
entries came from parsing wrapped generated-field prefixes, which the compiler now normalizes.
Visual Studio and Rider have not been checked for this projection. See the
[acceptance evidence](../scripts/editor-smoke/compiled-debugger/hoisted-last-verified.json)
and [debugger investigation](plans/issue-1399-hoisted-locals.md) for the tested cases and limits.

The numeric loop capture check now also runs after a timer suspension. Its closures retain
independent values `0`, `1`, and `2`, their sum is `3`, and leaving the loop restores outer `i = 7`.
The fresh C# debugger run passes all 55 stops and 123 expected Locals/Watch values, with matching
debug and nondebug output. The earlier numeric control now passes IL verification in both modes.
The fresh `netcoredbg` comparison on the same assembly also passes all 123 first Locals/Watch
values, 29 scope-absence checks, and two hidden-field checks, with the same seven duplicate-name
Locals cases as #1985.
The [numeric-timer follow-up record](../scripts/editor-smoke/compiled-debugger/numeric-timer-last-verified.json)
preserves this bounded validation separately from #1985's original full-suite evidence.

### Top-level bindings

*Module and script top-level bindings* are emitted as static fields of `$Program`, because they
outlive the initializer that assigns them and may be captured by other modules. Inspect them under
the static fields of `$Program` rather than in the locals window. Captured top-level bindings retain
their static storage in async arrows, and stale state-machine snapshots stay hidden.

### Stepping and the bundled stdlib

Importing a Node-compatible module (`path`, `fs`, …) compiles that module's TypeScript into your
program, so it would otherwise be exactly as steppable as your own files. Those methods are marked
non-user code: with Just My Code on — the default in VS Code and Visual Studio — stepping runs
through them and stops on your next line. Their line information is still emitted, so a stack trace
through the stdlib stays readable, and turning Just My Code off lets you step in.

The emitted runtime helpers need no such marking: they carry no debug information at all, which
already puts them outside user code.

State-machine and closure types are marked compiler-generated. Their infrastructure methods
(`MoveNext`, enumerator plumbing, and `SetStateMachine`) are non-user code, while their portable
PDB mapping still takes a debugger back to the source-level async or generator function.

One consequence worth knowing: because the stdlib travels in the PDB with its source embedded,
importing a large module makes the `.pdb` noticeably bigger. It costs nothing at run time and
nothing in the assembly; only the symbol file grows.

For a no-build workflow that executes the syntax tree directly, use the separate
[interpreter debugger](debugging-interpreter.md).

## Editor setup

None of this needs a SharpTS-specific debug adapter — the assembly is an ordinary .NET one.

**VS Code** — install the C# extension, which supplies the `coreclr` adapter. With the SharpTS
extension installed, **SharpTS: Debug Compiled Current File** does the whole round trip: it saves the file
if it is dirty, compiles that saved source with `-g`, and starts a debug session on the result. It
checks for the C# extension first and offers to install it rather than failing obscurely.

To drive it from `launch.json` instead, compile with `-g` and point the configuration at the
assembly:

```json
{
  "type": "coreclr",
  "request": "launch",
  "name": "Debug app.ts",
  "program": "${workspaceFolder}/app.dll",
  "cwd": "${workspaceFolder}",
  "console": "internalConsole"
}
```

The config names the `.dll`, not the `.ts` — the PDB is what takes the debugger back to source.
Output is written beside the source file, which is where the runtimeconfig, co-located
dependencies, and imported modules resolve from, and which avoids leaving build output to
accumulate in a temporary directory.

**Rider / Visual Studio** — open or attach to the compiled assembly as you would any .NET program;
both locate `app.pdb` beside `app.dll` and open the `.ts` files it names.

**netcoredbg** — `netcoredbg --interpreter=cli -- dotnet app.dll`, then
`break app.ts:12` and `run`.

## Manual smoke checklist

`tests/SharpTS.Tests/Compilation/DebugSymbolsTests.cs` and `DebugSymbolsHoistedLocalsTests.cs`
assert the symbol *metadata* thoroughly — documents and checksums, sequence points and the lines
they land on, named locals, lexical scope
nesting, state-machine mappings, async suspension/resume records, hoisted-binding lifetimes,
authoritative closure storage, generated-code attributes, and a CodeView identity that still matches
after the reference rewriter. The hoisted tests also execute debug and non-debug assemblies across
real suspension. These unit tests do not launch a debugger. When changing statement emission,
the span model, or the symbol pipeline, also run the
[real VS Code debugger acceptance check](../scripts/editor-smoke/compiled-debugger/README.md) or the
manual checklist below. The optional acceptance check records actual source breakpoint hits,
stepping, locals/Watch, imported frames, and debug/non-debug execution through the installed C#
extension. It also checks BOM-encoded source files with exact-source validation enabled.

The VS Code check runs the adapter inside its supported client using public editor debug APIs.
It does not script `vsdbg` as a standalone adapter. A client-free automated scenario would require
a separately installed debugger such as `netcoredbg`.

Use a program with a function, a loop, a conditional, a `try`/`catch`, a class method, an `async`
function, a generator, and an `import`.

1. `sharpts --compile main.ts -g`, then launch under the debugger.
2. Set a breakpoint on a top-level statement — it binds, and hits with the expected call stack.
3. Set one inside a function body and one inside a class method — both bind and hit.
4. Step over a `{`-only line: the debugger moves to the first statement inside, never onto the brace.
5. Step through a loop: the header and the body alternate rather than sticking on one line.
6. Break inside a `catch` and confirm the frame is the catch body, not the `try` line.
7. Break before and after suspension in an async function, generator, async generator, and async
   arrow. Confirm the source frame and Locals/Watch values, including a captured variable changed
   by a closure. Repeat inside a shadowing block, loop, and suspending catch; confirm inner values
   while inside and outer values or unavailable names after leaving the scope.
8. Break in an imported module's function and confirm the debugger opens that file, not the entry file.
9. Rebuild without `-g`, confirm no `.pdb` is produced and the program still runs.
