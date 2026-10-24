using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ConcreteExportNamespaceImportTests
{
    [Theory]
    [InlineData("const result: number = Dynamic.known;", null)]
    [InlineData("const result: string = Dynamic.known;", "TS2322")]
    [InlineData("Dynamic.missing;", "TS2339")]
    [InlineData("Dynamic.Auxiliary;", "TS2339")]
    [InlineData("Dynamic();", "TS2349")]
    public void RecordExportAssignmentRetainsItsValueShape(string source, string? code)
        => Check("const value: { known: number };", source, code);

    [Theory]
    [InlineData("const result: number = Dynamic.read('ok');", null)]
    [InlineData("Dynamic.read(1);", "TS2345")]
    [InlineData("const result: string = Dynamic.read('ok');", "TS2322")]
    public void RecordExportAssignmentRetainsMemberCallSignatures(string source, string? code)
        => Check("const value: { read: (input: string) => number };", source, code);

    private static void Check(string declaration, string source, string? code)
    {
        string directory = Path.GetFullPath("concrete-export-namespace");
        string entryPath = Path.Combine(directory, "main.ts");
        string dependencyPath = Path.Combine(directory, "assignment.d.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [dependencyPath] = $$"""
                declare module "assignment" {
                    {{declaration}}
                    interface Auxiliary { marker: number; }
                    export = value;
                }
                """,
            [entryPath] = "/// <reference path=\"./assignment.d.ts\" />\nimport * as Dynamic from 'assignment';\n" + source,
        };
        var resolver = new ModuleResolver(entryPath, files,
            new TypeScriptProgramOptions { NoLib = true, PreferDeclarationFiles = true });
        resolver.RegisterAmbientModuleDeclarations([resolver.LoadModule(dependencyPath)]);
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        if (code is null)
            Assert.Empty(checker.GetDiagnostics());
        else
            Assert.Equal(code, Assert.Single(checker.GetDiagnostics()).TsCode);
    }
}
