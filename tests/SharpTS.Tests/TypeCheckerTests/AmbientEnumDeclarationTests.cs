using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AmbientEnumDeclarationTests
{
    [Theory]
    [InlineData("enum", false, false)]
    [InlineData("const enum", false, true)]
    [InlineData("export enum", true, false)]
    [InlineData("export const enum", true, true)]
    public void ParsesAndChecksAmbientModuleEnum(string prefix, bool exported, bool constant)
    {
        var tokens = new Lexer($$"""
            declare module "fixture" {
                {{prefix}} Value { Known, "ready-now" = 4 }
                const member: Value;
            }
            """).ScanTokens();
        var result = new Parser(tokens, DecoratorMode.None).Parse();
        Assert.Empty(result.Errors);
        var module = Assert.IsType<Stmt.DeclareModule>(Assert.Single(result.Statements));
        var member = module.Members[0];
        if (exported)
            member = Assert.IsType<Stmt.Export>(member).Declaration!;

        var declaration = Assert.IsType<Stmt.Enum>(member);
        Assert.Equal("Value", declaration.Name.Lexeme);
        Assert.Equal(constant, declaration.IsConst);
        Assert.Equal(new[] { "Known", "ready-now" }, declaration.Members.Select(item => item.Name.Lexeme));
        var checker = new TypeChecker(strictNullChecks: true);
        checker.Check(result.Statements);
        Assert.Empty(checker.GetDiagnostics());
    }

    [Fact]
    public void AmbientModuleEnumDoesNotReplaceAnEnclosingEnum()
    {
        const string source = """
            enum Status { Local }
            declare module "fixture" {
                enum Status { Remote }
                const ready: Status;
            }
            const value = Status.Local;
            """;
        var result = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.None).Parse();
        Assert.Empty(result.Errors);
        var checker = new TypeChecker(strictNullChecks: true);
        checker.Check(result.Statements);
        Assert.Empty(checker.GetDiagnostics());
    }

    [Fact]
    public void ImportsExposeTheDeclaredEnumMembers()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_ambient_enum_{Guid.NewGuid():N}");
        string library = Path.Combine(directory, "library.d.ts");
        string entry = Path.Combine(directory, "main.ts");
        var resolver = new ModuleResolver(entry, new Dictionary<string, string>
        {
            [library] = """
                declare module "fixture" {
                    export enum Value { "ready" = 4 }
                    const enum Other { Known }
                }
                """,
            [entry] = """
                import { Value, Other } from "fixture";
                const first: Value = Value.ready;
                const second: Other = Other.Known;
                Value.Missing;
                """
        }, new TypeScriptProgramOptions { NoLib = true, PreferDeclarationFiles = true });
        resolver.RegisterAmbientModuleDeclarations([resolver.LoadModule(library)]);
        var modules = resolver.GetModulesInOrder(resolver.LoadProgram(entry));
        var checker = new TypeChecker(strictNullChecks: true);
        checker.CheckModules(modules, resolver);
        var fixture = Assert.Single(modules, module => module.IsAmbientModule);
        Assert.Equal(4d, Assert.IsType<TypeInfo.Enum>(fixture.ExportedTypes["Value"]).Members["ready"]);
        Assert.Equal(0d, Assert.IsType<TypeInfo.Enum>(fixture.ExportedTypes["Other"]).Members["Known"]);
        var diagnostic = Assert.Single(checker.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("TS2339", diagnostic.TsCode);
        Assert.Contains("Missing", diagnostic.Message);
    }
}
