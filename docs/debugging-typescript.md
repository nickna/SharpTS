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

Locals show under the names you wrote, over the range they are actually in scope — a `for` binding
is offered inside its loop and not outside it, and a shadowing inner `let` resolves ahead of the
outer one. Temporaries the compiler introduces (destructuring scratch slots and similar) are marked
hidden and stay out of the locals window.

### Where variables do not appear as locals

Two categories are visible to a debugger, but not as ordinary locals. This is accepted behavior for
now, not an oversight.

*Module and script top-level bindings* are emitted as static fields of `$Program`, because they
outlive the initializer that assigns them and may be captured by other modules. Inspect them under
the static fields of `$Program` rather than in the locals window.

*Locals of `async` functions and generators* are hoisted into the state machine's fields so they
survive suspension, so they appear as fields of the `<name>d__N` frame instead of as locals of
`MoveNext`. The generated state-machine and display-class names are stable, and their fields retain
the source binding names. Reconstructing every hoisted field as an ordinary locals-window entry
would require additional debugger-specific hoisted-local metadata and remains outside the accepted
v1 behavior.

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

Interpreter debugging remains out of scope.

## Editor setup

None of this needs a SharpTS-specific debug adapter — the assembly is an ordinary .NET one.

**VS Code** — install the C# extension, which supplies the `coreclr` adapter. With the SharpTS
extension installed, **SharpTS: Debug Current File** does the whole round trip: it saves the file
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

`SharpTS.Tests/Compilation/DebugSymbolsTests.cs` asserts the symbol *metadata* thoroughly —
documents and checksums, sequence points and the lines they land on, named locals, lexical scope
nesting, state-machine mappings, async suspension/resume records, generated-code attributes, and a
CodeView identity that still matches after the reference rewriter. What no automated test covers is
a debugger actually stopping, so run this by hand when changing statement emission, the span model,
or the symbol pipeline.

Scripting `vsdbg` for this is not an option: it is licensed for use only with Visual Studio and
VS Code, and enforces that with a handshake its own clients answer. Automating this would mean
`netcoredbg`, which is separately installed; until that is set up, the check below is manual.

Use a program with a function, a loop, a conditional, a `try`/`catch`, a class method, an `async`
function, a generator, and an `import`.

1. `sharpts --compile main.ts -g`, then launch under the debugger.
2. Set a breakpoint on a top-level statement — it binds, and hits with the expected call stack.
3. Set one inside a function body and one inside a class method — both bind and hit.
4. Step over a `{`-only line: the debugger moves to the first statement inside, never onto the brace.
5. Step through a loop: the header and the body alternate rather than sticking on one line.
6. Break inside a `catch` and confirm the frame is the catch body, not the `try` line.
7. Break inside an `async` function after an `await`, and inside a generator after a `yield`.
8. Break in an imported module's function and confirm the debugger opens that file, not the entry file.
9. Rebuild without `-g`, confirm no `.pdb` is produced and the program still runs.
