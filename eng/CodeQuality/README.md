# Code quality regression checks

Run from the repository root with the SDK in `global.json` and PowerShell 7:

```powershell
dotnet build SharpTS.sln -c Release
./scripts/test-code-quality.ps1
```

The full CI build runs both commands on Linux and Windows. The script compiles
controlled fixtures using the real repository props and editorconfig, tests the
syntax checker, and scans shipping C# under `src/`. Fixture build logs remain in
`artifacts/code-quality-<id>/`. A missing analyzer, unexpected build failure,
syntax error, unapproved duplicate, or unused emitter method fails the gate.
Only IDE0051/IDE0052 are promoted by `WarningsAsErrors`; dependency warnings keep
their existing severity. Projects outside the solution inherit the same policy
when built, but only projects actually built receive semantic analyzer coverage.

## Dead-code coverage and the September 2026 investigation

SDK 10.0.400 reports unused private methods (IDE0051) and fields written but never
read (IDE0052) in a small partial-class fixture. Cross-file references, delegate
registration, `nameof` reflection registration, generated files and justified
`SuppressMessage` exceptions pass. The test rejects builds that fail for an
unrelated diagnostic rather than mistaking a broken fixture for analyzer coverage.

However, an injected unused method in each of `RuntimeEmitter` and `ILCompiler`
produced **no diagnostic** in a successful Release build. A minimal reproduction is:

```csharp
class Example
{
    private void Dead() { }
    public string Name() => nameof(System.Console.WriteLine);
}
```

Removing the qualified `nameof` expression restores IDE0051. The SDK's operation
tree contains a nested `OperationKind.None` for the receiver of the qualified
name. Roslyn's [unused-member analyzer implementation](https://source.dot.net/Microsoft.CodeAnalysis.Features/src/roslyn/src/Analyzers/Core/Analyzers/RemoveUnusedMembers/AbstractRemoveUnusedMembersDiagnosticAnalyzer.cs.html)
bails out for unsupported operations across a containing type; its `nameof`
exception handles the argument itself, but not that nested receiver. The real
emitters contain examples at `RuntimeEmitter.BigInt.cs` (`nameof(string.StartsWith)`)
and `ILCompiler.Async.cs` (`nameof(Task.FromResult)`). This explains a concrete
coverage hole independently of the earlier audit's workspace warnings. The local
`dotnet format` probe also encountered a sandbox build-host pipe failure, so its
output was not used as evidence of a clean workspace. Build-based fixtures avoid
depending on a separate format workspace loader.

The cleanup in commit `d7cf3114` already removed the unused JSON and hosted-module
paths. `QLT001` now supplements the SDK for private methods in those two partial
emitter types. It parses all `Compilation/` C# files together and reports a method
whose name occurs only at its declaration and has no exact string-literal
registration. The real injected methods fail this check. It intentionally does
not pretend to be symbol-level reachability analysis: overloads, same-name symbols,
self-recursion, dead helper chains/cycles, and literal-name matches can hide dead
code. Fields and properties remain under SDK coverage, with the qualified-nameof
limitation above. An SDK upgrade should revisit the characterization fixture and
the need for the supplement.

For reflection, prefer `nameof(Member)` or an explicit delegate registration.
For external string-based entry points, use a member-scoped
`SuppressMessage("Style", "IDE0051", Justification = "...")` for the SDK, or put
`// QLT001: <specific external caller and reason>` immediately above the emitter
method. QLT001 requires a nontrivial explanation; it does not automatically accept
arbitrary attributes. Do not add fake references to silence a finding. Reflection
from outside `Compilation/` is not discovered by QLT001 and needs that documented
exception. Neither check proves runtime reachability, and removing an unused root
may expose further unused helpers on the next build.

## Duplicate-method baseline

`duplicates.json` records 28 existing groups after the initial cleanup batches.
Each entry records the SHA-256 of an identical method body, exact file/type/method
signatures and a reviewed reason. Some are intentional backend or AST-type
boundaries; others are existing extraction candidates explicitly deferred to a
focused change. Acceptance does not mean the code must remain duplicated.

The scanner includes block and expression-bodied methods of at least 60 C# tokens.
It ignores trivia, preserves identifiers and literal spelling, and scans files
regardless of Git tracking status. It detects whole-method copies, not renamed
clones or arbitrary repeated fragments inside otherwise different methods. It
parses the default preprocessor branch; inactive conditional branches are outside
this syntax inventory. Constructors, accessors, local functions and non-C# code
are outside this initial method-body gate.

Generated `.g.cs`/`.generated.cs` files, files starting with an auto-generated
comment, generated methods, build output and SDK `Templates/` are excluded.
Tests (including intentional fixtures), samples, external corpora and benchmarks
are outside `src/` and excluded from duplication checking. Ordinary runtime emitter
source is handwritten and is included, even though it emits generated IL.

To inspect the current candidates without accepting them:

```powershell
dotnet run --project eng/CodeQuality -c Release -- . --inventory
```

Prefer extracting shared code when the ownership and behavior permit it. Otherwise
review the listed peers and edit only the affected baseline entry with a concrete
reason. Inventory output deliberately has empty reasons and cannot pass as a new
baseline. Adding a third copy, changing an accepted body, or moving a member
requires review; stale entries fail and must be removed or updated. Line-only and
comment changes do not invalidate a baseline. The self-tests cover new groups,
third copies, trivia, changed literals, stale exceptions, generated exclusions,
and invalid syntax.
