using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Modules;

public sealed class GlobalAugmentationDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAugmentationIsPublishedOncePerSourceDeclaration(bool suppressEntry)
    {
        string directory = Path.Combine(Path.GetTempPath(), "global-augmentation-diagnostics");
        string entryPath = Path.Combine(directory, "main.ts");
        string augmentationPath = Path.Combine(directory, "augmentation.ts");
        var sources = new Dictionary<string, string>
        {
            [entryPath] = (suppressEntry ? "// @ts-nocheck\n" : "") + """
                /// <reference path='./base.ts' />
                import './augmentation';
                """,
            [Path.Combine(directory, "base.ts")] = "enum Conflict { Value }",
            [augmentationPath] = """
                export {};
                declare global { interface Conflict { value: number; } }
                declare global { interface Conflict { other: string; } }
                """
        };
        var resolver = new ModuleResolver(entryPath, sources, TypeScriptProgramOptions.Disabled);
        var entry = resolver.LoadProgram(entryPath);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        var errors = checker.GetDiagnostics().Where(diagnostic => diagnostic.Message.Contains("Cannot augment 'Conflict'")).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error => Assert.Equal(augmentationPath, error.Location!.FilePath));
        Assert.Equal(new[] { 2, 3 }, errors.Select(error => error.Location!.Line).Order().ToArray());
    }
}
