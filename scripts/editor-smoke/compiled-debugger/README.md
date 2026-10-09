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
`--compiler` (defaults to the Debug compiler), and `--artifacts`. Arguments use `--name value`
pairs. The runner requires Node.js and a real VS Code installation. The current verified host
is Windows ARM64; other hosts have not been certified by this check.

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

Verified on 2026-10-08 with VS Code 1.134.0, C# 2.140.9 and .NET SDK 10.0.401 on Windows
ARM64. All seven source breakpoints bound and hit, five step stops followed the expected source
lines, and Locals/Watch resolved `total = 0` and the source parameter `__this = 2`. The function is
invoked through `Reflect.apply`, so the check also distinguishes that legal user parameter name
from the runtime's hidden receiver convention. The mixed-encoding
files passed exact-source checks after `--ref-asm` rewriting; debug and non-debug output matched.
This C# debugger displays the suspended async/generator frames as `<Unknown function>` while
resolving their TypeScript files and lines correctly. Full hoisted-local reconstruction remains
outside the accepted v1 behavior described in [the debugger guide](../../../docs/debugging-typescript.md).
