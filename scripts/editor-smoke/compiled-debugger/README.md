# Compiled TypeScript debugger acceptance

This optional local check runs the real C# debugger through a supported VS Code extension-test
host. It uses public VS Code debug APIs; it never invokes the licensed adapter outside VS Code.
The test stages existing C# and .NET runtime extensions in fresh extension/profile directories,
disables updates there, and leaves the normal editor profile untouched.

Build SharpTS, then pass the directories of those two installed extensions:

```powershell
dotnet build tests/SharpTS.Tests/SharpTS.Tests.csproj -m:1
node scripts/editor-smoke/compiled-debugger/run.mjs `
  --csharp-extension /absolute/path/to/ms-dotnettools.csharp-version-platform `
  --dotnet-runtime-extension /absolute/path/to/ms-dotnettools.vscode-dotnet-runtime-version `
  --dotnet /absolute/path/to/dotnet
```

Optional arguments are `--code`, `--code-cli` (for an ambiguous Windows installation),
`--compiler` (defaults to the Debug compiler), `--artifacts`, `--fixture`, and `--tests`.
Arguments use `--name value` pairs. The runner requires Node.js and a real VS Code installation.
The ordinary source/stepping check has a historical Windows ARM64 record; the hoisted-local
check below has current Windows x64 evidence. Other hosts have not been certified by these checks.

## Source breakpoints and stepping

The tracked [fixture](../../../tests/fixtures/CompiledDebuggerAcceptance/main.ts) has ordinary
functions, a loop, a conditional, a catch, a class method, an async function, a generator and an
import. The runner copies its entry as UTF-8 with a BOM and its imported file as UTF-16 LE with a
BOM, so exact-source validation also exercises the original-byte checksum fix.

The test requires verified source breakpoints and actual stops in all those executable domains,
steps past brace-only lines, observes alternating loop-header/body stops, inspects a source-named
local and evaluates it in Watch, and verifies the imported function's TypeScript caller frame.
Debug and non-debug assemblies must both run with the same output, and the fresh non-debug output
directory must contain no PDB. Both builds use `--ref-asm` to exercise the real assembly-reference
rewriter. The async fixture suspends on a timer; its post-await breakpoint must occur after the
top-level completion output. The debugger must report a successful process exit. It uses Just
My Code and exact-source checks.

Each run writes `result.json`, a full DAP transcript, compilation output, editor logs, compiler/
assembly/PDB hashes, and environment versions under `artifacts/compiled-debugger/run-*`. The editor
process has a 180-second deadline; timeout cleanup targets only that isolated editor process tree
on Windows. The POSIX fallback stops its launched process, and descendant cleanup has not been
verified there. [Compact verification](last-verified.json) records the accepted run.

The historical v1 run was verified on 2026-10-08 with VS Code 1.134.0, C# 2.140.9 and .NET SDK
10.0.401 on Windows ARM64. All seven source breakpoints bound and hit, five step stops followed the expected source
lines, and Locals/Watch resolved `total = 0` and the source parameter `__this = 2`. The function is
invoked through `Reflect.apply`, so the check also distinguishes that legal user parameter name
from the runtime's hidden receiver convention. The mixed-encoding
files passed exact-source checks after `--ref-asm` rewriting; debug and non-debug output matched.
That run displayed suspended async/generator frames as `<Unknown function>` while resolving
their TypeScript files and lines correctly. It did not verify hoisted variables and predates the
projection described below; [last-verified.json](last-verified.json) preserves that original record.

## Hoisted locals and lexical scope

Run the dedicated [hoisted fixture](../../../tests/fixtures/HoistedDebuggerAcceptance/main.ts)
with its strict Locals/Watch assertions:

```powershell
node scripts/editor-smoke/compiled-debugger/run.mjs `
  --csharp-extension /absolute/path/to/ms-dotnettools.csharp-version-platform `
  --dotnet-runtime-extension /absolute/path/to/ms-dotnettools.vscode-dotnet-runtime-version `
  --dotnet /absolute/path/to/dotnet `
  --fixture tests/fixtures/HoistedDebuggerAcceptance `
  --tests hoisted.test.cjs `
  --artifacts artifacts/hoisted-debugger
```

The fixture suspends on timers and yields, then checks source parameters and locals in async
functions, generators, async generators, and async arrows, including instance/static class methods
and namespace functions. It checks mutable captured storage, nested and sibling shadows, block and
catch scope expiry, loop declarations and existing-variable assignment, destructuring, `var`, and
legal source names that resemble compiler temporaries. Every expected value must appear in Locals
and evaluate in Watch; out-of-scope names must be absent and fail Watch evaluation. State-machine
frames must be recognized, and compiler scaffolding must not appear as user variables. Debug and
non-debug output must match after reference rewriting, with no PDB in the fresh non-debug output.

The #1985 primary acceptance passed 55 stops with VS Code 1.140.0, C# 2.140.9 and .NET SDK 10.0.401
on Windows x64. All 123 expected Locals values matched, with exactly one entry for each binding.
The [hoisted verification record](hoisted-last-verified.json) identifies the accepted compiler,
assembly/PDB hashes, environment, and observations. The
[investigation record](../../../docs/plans/issue-1399-hoisted-locals.md) includes the Roslyn control,
the earlier expression-evaluator failure, and the debugger comparison.

The separate `netcoredbg` 3.2.0-1092 comparison passed all 55 stops in the same order on the same
assembly, including all 123 first Locals values, expected Watch values, 29 out-of-scope checks, and
two hidden-field checks. No empty-name entries or compiler scaffolding remain. Seven Locals lists
still duplicate a shadowed
name (`async-shadow/local`, `async-parameter-shadow/parameter`, `async-captured-shadow/local`, and
`iterator-shadow/local`, plus `numeric-capture-loop/i` at three iterations); the first Locals entry
and Watch resolve the correct binding in each case. The loop shows `i = 0`, `1`, and `2`, then
restores outer `i = 7` with `result = 3`. Boxed doubles may need
expansion of `m_value`. Earlier empty-name entries came from parsing wrapped generated-field
prefixes, which the compiler now normalizes. The primary acceptance also requires exactly one Locals
entry for each expected binding.

The numeric-timer follow-up replaces the numeric loop's initial resolved promise with a timer
delay. The fresh primary run passes the same 55 stops and all 123 expected Locals/Watch values,
including independent iteration captures `0`, `1`, and `2`, restored outer `i = 7`, and sum `3`.
Debug and nondebug output match, and the earlier failing numeric control now passes IL verification
in both modes. The fresh secondary comparison on the same assembly passes all 123 first Locals
values, Watches, 29 scope-absence checks, and two hidden-field checks; its seven duplicate-name
Locals cases exactly match the original record. [Follow-up verification](numeric-timer-last-verified.json)
records the new inputs and bounded checks. The original record above retains #1985's prior source
hashes, debugger comparison, and full local and conformance validation.

The global async-arrow case keeps a module binding in its authoritative static field. Its stale
state-machine copy is hidden, and bare `moduleShared` Watch lookup is unavailable; inspect the
static field as described in the debugger guide.

The primary C# debugger's Watch evaluator requires C# keyword escaping: a source binding `int = 3`
appears as `@int` in Locals and evaluates as `@int`; bare `int` fails with CS1525. The legal source
name `$dollar` evaluates directly (verified value `4`). Visual Studio and Rider have not been
verified for hoisted-local projection. See
[the debugger guide](../../../docs/debugging-typescript.md) for the supported compiled workflow.

## Roslyn comparison control

The [C# control](../../../tests/fixtures/HoistedDebuggerAcceptance/roslyn/Program.cs) has eight
async, iterator, and async-iterator stops with parameters, shared captures, and block scope.
Build it independently and use the same public VS Code debug API check:

```powershell
dotnet build tests/fixtures/HoistedDebuggerAcceptance/roslyn/RoslynControl.csproj -c Debug
node scripts/editor-smoke/compiled-debugger/run.mjs `
  --csharp-extension /absolute/path/to/ms-dotnettools.csharp-version-platform `
  --dotnet-runtime-extension /absolute/path/to/ms-dotnettools.vscode-dotnet-runtime-version `
  --dotnet /absolute/path/to/dotnet `
  --fixture tests/fixtures/HoistedDebuggerAcceptance/roslyn `
  --assembly tests/fixtures/HoistedDebuggerAcceptance/roslyn/bin/Debug/net10.0/RoslynControl.dll `
  --tests hoisted.test.cjs `
  --artifacts artifacts/roslyn-debugger
```

`--assembly` uses that existing PE/PDB and its original source paths. The runner records the
control's values and scopes; SharpTS compilation, reference rewriting, mixed-source encoding,
and debug/non-debug parity checks apply to the TypeScript fixtures above.
