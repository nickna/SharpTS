using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AnyExportNamespaceImportTests
{
    [Theory]
    [InlineData("any", "const value: number = Dynamic.missing; const text: string = Dynamic.anything(1);", null)]
    [InlineData("{ known: number }", "const value: string = Dynamic.known;", "TS2322")]
    [InlineData("{ known: number }", "Dynamic.missing;", "TS2339")]
    public void ExplicitDynamicExportAssignmentsRemainAny(string exportedType, string source, string? code)
    {
        string directory = Path.GetFullPath("any-export-namespace");
        string entryPath = Path.Combine(directory, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "assignment.d.ts")] = $$"""
                declare module "assignment" {
                    const value: {{exportedType}};
                    interface Auxiliary { marker: number; }
                    export = value;
                }
                """,
            [entryPath] = "/// <reference path=\"./assignment.d.ts\" />\nimport * as Dynamic from 'assignment';\n" + source,
        };
        var resolver = new ModuleResolver(entryPath, files);
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        if (code is null)
            Assert.Empty(checker.GetDiagnostics());
        else
            Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.TsCode == code);
    }
}
