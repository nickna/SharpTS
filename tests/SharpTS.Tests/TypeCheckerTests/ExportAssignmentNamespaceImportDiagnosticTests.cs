using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ExportAssignmentNamespaceImportDiagnosticTests
{
    [Theory]
    [InlineData("function value(input: string): number; export = value;")]
    [InlineData("class value { static known: number; } export = value;")]
    [InlineData("namespace N { function value(): number; } import alias = N.value; export = alias;")]
    public void BareDeclarationTargetsReportOneImportDiagnostic(string declaration)
    {
        var diagnostics = Check("declare module 'assignment' { " + declaration + " }", "import * as Imported from 'assignment';\nImported;");
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TS2497", diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }

    [Fact]
    public void BareEnumTargetInAnExternalDeclarationFileReportsTS2497()
    {
        var diagnostics = Check("declare enum value { Known }\nexport = value;",
            "import * as Imported from './assignment';\nImported;");
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TS2497", diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }

    [Theory]
    [InlineData("function value(): number;", "TS2497")]
    [InlineData("const value: () => number;", null)]
    [InlineData("namespace value { const known: number; }", null)]
    public void GlobalAssignedSymbolsUseTheirOriginalDeclarationKind(string declaration, string? code)
        => AssertCode(Check("declare " + declaration + "\ndeclare module 'assignment' { export = value; }",
            "import * as Imported from 'assignment';\nImported;"), code);

    [Theory]
    [InlineData("function value(): number;", "const value: () => number;", null)]
    [InlineData("const value: () => number;", "function value(): number;", "TS2497")]
    public void LocalAssignedDeclarationsShadowGlobalNames(string global, string local, string? code)
        => AssertCode(Check("declare " + global + "\ndeclare module 'assignment' { " + local + " export = value; }",
            "import * as Imported from 'assignment';\nImported;"), code);

    [Theory]
    [InlineData("const value: { known: number }; export = value;", "const result: number = Imported.known;")]
    [InlineData("const value: (input: string) => number; export = value;", "Imported;")]
    [InlineData("function value(): number; namespace value { const known: number; } export = value;", "const result: number = Imported.known;")]
    [InlineData("class value {} namespace value { const known: number; } export = value;", "const result: number = Imported.known;")]
    [InlineData("namespace N { const value: () => number; } import alias = N.value; export = alias;", "Imported;")]
    public void VariableAndNamespaceDeclarationTargetsRemainValid(string declaration, string body)
        => Assert.Empty(Check("declare module 'assignment' { " + declaration + " }",
            "import * as Imported from 'assignment';\n" + body));

    [Theory]
    [InlineData("function value(input: string): number;", "TS2497")]
    [InlineData("const value: (input: string) => number;", null)]
    public void ImportEqualsAliasesPreserveTheAssignedDeclarationKind(string declaration, string? code)
    {
        string declarations = $$"""
            declare module 'origin' { {{declaration}} export = value; }
            declare module 'assignment' { import alias = require('origin'); export = alias; }
            """;
        AssertCode(Check(declarations, "import * as Imported from 'assignment';\nImported;"), code);
    }

    [Theory]
    [InlineData("function value(input: string): number;", "TS2497")]
    [InlineData("const value: (input: string) => number;", null)]
    public void NamedReExportAliasesPreserveTheAssignedDeclarationKind(string declaration, string? code)
    {
        string declarations = $$"""
            declare module 'origin' { export {{declaration}} }
            declare module 'bridge' { export { value as renamed } from 'origin'; }
            declare module 'assignment' { import { renamed as alias } from 'bridge'; export = alias; }
            """;
        AssertCode(Check(declarations, "import * as Imported from 'assignment';\nImported;"), code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportEqualsAndSyntheticDefaultFormsRemainValid(bool syntheticDefault)
    {
        string source = syntheticDefault
            ? "import Imported from 'assignment'; const result: number = Imported('ok');"
            : "import Imported = require('assignment'); const result: number = Imported('ok');";
        Assert.Empty(Check("declare module 'assignment' { function value(input: string): number; export = value; }",
            source, syntheticDefault));
    }

    private static void AssertCode(IReadOnlyList<SharpTS.Diagnostics.Diagnostic> diagnostics, string? code)
    {
        if (code is null) Assert.Empty(diagnostics);
        else Assert.Equal(code, Assert.Single(diagnostics).TsCode);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string declarations, string source, bool syntheticDefault = false)
    {
        string directory = Path.GetFullPath("export-assignment-import-diagnostics");
        string entryPath = Path.Combine(directory, "main.ts");
        string dependencyPath = Path.Combine(directory, "assignment.d.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [dependencyPath] = declarations,
            [entryPath] = "/// <reference path=\"./assignment.d.ts\" />\n" + source,
        };
        var resolver = new ModuleResolver(entryPath, files,
            new TypeScriptProgramOptions { NoLib = true, PreferDeclarationFiles = true });
        resolver.RegisterAmbientModuleDeclarations([resolver.LoadModule(dependencyPath)]);
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { AllowSyntheticDefaultImports = syntheticDefault, MaxErrors = 20 });
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
