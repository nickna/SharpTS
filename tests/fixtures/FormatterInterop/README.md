# Formatter interoperability fixtures

This opt-in evidence project is intentionally outside the solution and ordinary
test discovery. The formatter smoke runner supplies formatted copies; this
project never formats or overwrites the originals.

`fixtures.json` records the decorator dialect and exact expected output for each
case. The corpus covers TypeScript, declaration files and TSX, namespace/generic syntax, private
members and brand checks, `dotnet:` imports, legacy `@DotNetType`, Stage 3 and legacy
class/method/parameter decorators, comments, a checked `@ts-expect-error`
directive, Unicode, and LF/CRLF inputs. The local `.gitattributes` preserves the
legacy decorator case as CRLF when checked out.

The CLR ambient mapping uses the legacy dialect exercised by the existing
compiler/interpreter `@DotNetType` tests. The separate Stage 3 fixture exercises a
class decorator with its `(value, context)` contract.

`syntax-private.ts` remains broad parse/typecheck-only coverage: its generic
namespace function call compiles but fails during baseline compiled execution
with `ContainsGenericParameters`. That failure occurs before formatting and is
not certified by the runtime comparison. The separate `runtime-private.ts`
keeps generic functions/classes, a namespace function, private fields/brand
checks and a checked directive in supported runtime call positions.

Build the harness separately:

```powershell
dotnet build tests/fixtures/FormatterInterop/SharpTS.FormatterInteropEvidence.csproj -c Release
```

Its arguments are `<source-root> <formatted-root> <report-path>`. Both source
roots contain the relative paths listed in `fixtures.json`; the original root
also contains the manifest. For a baseline-only check, pass the fixture directory
as both roots. For formatter evidence, pass the smoke runner's formatted output
directory as the second root.

For each original and formatted case the harness loads the real module graph
with default TypeScript declaration libraries, explicitly sets the decorator and
JSX options, rejects parse/type errors, and applies the canonical line-directive
policy to fixture diagnostics. Runnable cases use the interpreter's runtime
module graph. Compilation uses the established compiler-test APIs:
`ILCompiler.Compile` for a script-only entry, and `ILCompiler.CompileModules` for
an imported module graph. Declaration libraries are checked and excluded from
execution/emission. Compiled assemblies load through the default assembly load
context used by ordinary compiler tests. Each report records the selected
compiled pipeline. This is representative compiler API evidence, not a claim
of exact CLI compilation equivalence; the CLI currently also uses its module
driver for script entries. Outputs must equal the manifest baseline and each other. A
real implementation assembly reference supplies `InteropProbe`; its successful
invocation proves the custom CLR import was resolved. Marker comments are also
checked for preservation.

The JSON report contains source hashes, line-ending observations, selected
options, errors and outputs from both execution modes. Any failed case makes the
process exit nonzero. Run this trusted fixture harness under the smoke runner's
process timeout; it is an evidence collector, not an untrusted-code sandbox.

Script decorator runtime evidence applies to the compiler-test route above.
The CLI module driver and collectible embedding loader are outside this
formatter certification.

On October 6, 2026, the full smoke command passed with Prettier 3.9.9,
Node.js 25.5.0 and .NET SDK 10.0.401 on Windows ARM64. All eight cases formatted
idempotently and passed parsing/checking in both LF and CRLF. Six executable
cases matched their expected outputs in both interpreter and compiled modes,
before and after formatting. Both LSP feature modes advertised no formatting
providers, refused document/range/on-type formatting requests, and served CLR
decorator hover. No formatter syntax blocker required an alternate formatter.

For the initial generic alias input, keep whitespace between the closing `>`
and `=` (`Label<T> = {...}`). The unspaced `Label<T>={...}` control fails the
current SharpTS parser before formatting; the spaced form is checked here.
