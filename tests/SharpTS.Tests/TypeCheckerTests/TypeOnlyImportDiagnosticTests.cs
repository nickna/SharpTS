using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class TypeOnlyImportDiagnosticTests
{
    [Theory]
    [InlineData("import type { value } from './named';", "value;")]
    [InlineData("import { type value } from './named';", "value;")]
    [InlineData("import type { value as alias } from './named';", "alias;")]
    [InlineData("import type value from './named';", "value;")]
    [InlineData("import type * as value from './named';", "value.member;")]
    [InlineData("import type { value } from './named';", "function read() { return value; }")]
    [InlineData("import type { value } from './named';", "value = 1;")]
    public void TypeOnlyValueUseReportsTS1361(string import, string source)
        => Check(import, source, "TS1361");

    [Theory]
    [InlineData("import type { value } from './named';", "declare const copy: typeof value;")]
    [InlineData("import type { value } from './named';", "function read(value: number): number { return value; }")]
    [InlineData("import type { value } from './named';", "function read() { const value = 1; return value; }")]
    [InlineData("import { value } from './named';", "value;")]
    public void TypeQueriesAndLocalValuesRemainValid(string import, string source)
        => Check(import, source, null);

    private static void Check(string import, string source, string? code)
    {
        string directory = Path.GetFullPath("type-only-import-diagnostics");
        string entryPath = Path.Combine(directory, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "named.d.ts")] = "export declare const value: any; export default value;",
            [entryPath] = import + "\n" + source,
        };
        var resolver = new ModuleResolver(entryPath, files,
            new TypeScriptProgramOptions { NoLib = true, PreferDeclarationFiles = true });
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        if (code is null)
            Assert.Empty(checker.GetDiagnostics());
        else
            Assert.Equal(code, Assert.Single(checker.GetDiagnostics()).TsCode);
    }
}
