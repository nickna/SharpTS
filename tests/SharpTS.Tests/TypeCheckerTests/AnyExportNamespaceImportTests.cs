using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AnyExportNamespaceImportTests
{
    [Theory]
    [InlineData(false, "Dynamic.missing;", "TS2304")]
    [InlineData(true, "Dynamic.missing;", "TS2304")]
    [InlineData(false, "declare const value: typeof Dynamic;", null)]
    [InlineData(true, "declare const value: typeof Dynamic;", null)]
    [InlineData(false, "function read(Dynamic: { value: number }): number { return Dynamic.value; }", null)]
    [InlineData(true, "function read(Dynamic: { value: number }): number { return Dynamic.value; }", null)]
    public void DynamicTypeOnlyNamespaceImportsRetainOnlyTheirTypeFacet(
        bool commonJs, string source, string? code)
    {
        string directory = Path.GetFullPath("any-export-type-only-namespace");
        string entryPath = Path.Combine(directory, "main.ts");
        string dependencyPath = Path.Combine(directory, commonJs ? "dynamic.cjs" : "assignment.d.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [dependencyPath] = commonJs ? "/** @type {any} */ const value = {}; module.exports = value;" : """
                declare module "assignment" {
                    const value: any;
                    export = value;
                }
                """,
            [entryPath] = (commonJs ? "" : "/// <reference path=\"./assignment.d.ts\" />\n")
                + $"import type * as Dynamic from '{(commonJs ? "./dynamic.cjs" : "assignment")}';\n" + source,
        };
        var resolver = new ModuleResolver(entryPath, files,
            new TypeScriptProgramOptions { NoLib = true, PreferDeclarationFiles = true });
        if (!commonJs)
            resolver.RegisterAmbientModuleDeclarations([resolver.LoadModule(dependencyPath)]);
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        if (code is null)
            Assert.Empty(checker.GetDiagnostics());
        else
            // Type-only value uses currently receive TS2304; tsc specializes this to TS1361.
            Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.TsCode == code);
    }

    [Theory]
    [InlineData("any", "const value: number = Dynamic.missing; const text: string = Dynamic.anything(1);", null)]
    [InlineData("namespace", "const value: string = Dynamic.known;", "TS2322")]
    [InlineData("namespace", "Dynamic.missing;", "TS2339")]
    public void ExplicitDynamicExportAssignmentsRemainAny(string exportedType, string source, string? code)
    {
        string directory = Path.GetFullPath("any-export-namespace");
        string entryPath = Path.Combine(directory, "main.ts");
        string valueDeclaration = exportedType == "any"
            ? "const value: any;"
            : "namespace value { const known: number; }";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "assignment.d.ts")] = $$"""
                declare module "assignment" {
                    {{valueDeclaration}}
                    interface Auxiliary { marker: number; }
                    export = value;
                }
                """,
            [entryPath] = "/// <reference path=\"./assignment.d.ts\" />\nimport * as Dynamic from 'assignment';\n" + source,
        };
        var resolver = new ModuleResolver(entryPath, files,
            new TypeScriptProgramOptions { NoLib = true, PreferDeclarationFiles = true });
        var declaration = resolver.LoadModule(Path.Combine(directory, "assignment.d.ts"));
        resolver.RegisterAmbientModuleDeclarations([declaration]);
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        if (code is null)
            Assert.Empty(checker.GetDiagnostics());
        else
            Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.TsCode == code);
    }
}
