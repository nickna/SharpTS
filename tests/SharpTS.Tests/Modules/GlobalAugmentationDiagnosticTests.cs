using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.Modules;

public sealed class GlobalAugmentationDiagnosticTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FailedAugmentationIsPublishedOncePerSourceDeclaration(bool suppressEntry, bool sameBlock)
    {
        string directory = Path.Combine(Path.GetTempPath(), "global-augmentation-diagnostics");
        string entryPath = Path.Combine(directory, "main.ts");
        string augmentationPath = Path.Combine(directory, "augmentation.ts");
        var sources = new Dictionary<string, string>
        {
            [entryPath] = (suppressEntry ? "// @ts-nocheck\n" : "") + """
                /// <reference path='./base.ts' />
                import './augmentation';
                const retained: AfterFailure = { value: 7 };
                const observed: number = retained.value;
                """,
            [Path.Combine(directory, "base.ts")] = "enum Conflict { Value }",
            [augmentationPath] = sameBlock ? """
                export {};
                declare global {
                    interface Conflict { value: number; }
                    interface Conflict { other: string; }
                    interface AfterFailure { value: number; }
                }
                """ : """
                export {};
                declare global { interface Conflict { value: number; } }
                declare global { interface Conflict { other: string; } }
                declare global { interface AfterFailure { value: number; } }
                """
        };
        var resolver = new ModuleResolver(entryPath, sources, TypeScriptProgramOptions.Disabled);
        var entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        var errors = checker.GetDiagnostics().Where(diagnostic => diagnostic.Message.Contains("Cannot augment 'Conflict'")).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Equal(2, checker.GetDiagnostics().Count);
        Assert.All(errors, error => Assert.Equal(augmentationPath, error.Location!.FilePath));
        Assert.Equal(sameBlock ? new[] { 3, 4 } : new[] { 2, 3 },
            errors.Select(error => error.Location!.Line).Order().ToArray());
    }

    [Fact]
    public void StandaloneStrictCheckingStillThrowsAtTheFirstInvalidGlobalMember()
    {
        const string source = """
            enum Conflict { Value }
            declare global {
                interface Conflict { value: number; }
                interface Conflict { other: string; }
            }
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var error = Assert.Throws<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Contains("Cannot augment 'Conflict'", error.Message);
        Assert.Equal(3, error.Diagnostic.Location!.Line);
    }
}
