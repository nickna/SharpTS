using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Modules;

public class LibraryInterfaceAugmentationTests
{
    [Theory]
    [InlineData("lib.es2024.d.ts")]
    [InlineData("lib.es2025.d.ts")]
    public void PromiseValueIncludesLaterLibraryAugmentations(string library)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "promise-augmentation", "main.ts"));
        var resolver = new ModuleResolver(path, new Dictionary<string, string>
        {
            [path] = """
                const capability = Promise.withResolvers<number>();
                capability.resolve(7);
                const promise: Promise<number> = capability.promise;
                """
        }, TypeScriptProgramOptions.Default with { Lib = [library] });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbientValueIncludesLaterDeclarationFileAugmentations(bool shadowType)
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "interface-augmentation"));
        var path = Path.Combine(directory, "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string>
        {
            [path] = """
                /// <reference path="base.d.ts" />
                /// <reference path="extension.d.ts" />
                """ + (shadowType
                    ? "\nnamespace Inner { interface Factory { unrelated(): string; } const result: number = factory.added(); }"
                    : "\nconst result: number = factory.added();"),
            [Path.Combine(directory, "base.d.ts")] = "interface Factory { original(): number; } declare const factory: Factory;",
            [Path.Combine(directory, "extension.d.ts")] = "interface Factory { added(): number; }"
        }, TypeScriptProgramOptions.Disabled);
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }

    [Theory]
    [InlineData("lib.es2023.d.ts", "const capability = Promise.withResolvers<number>();")]
    [InlineData("lib.es2024.d.ts", "const capability = Promise.withResolvers<number>(); capability.resolve('wrong');")]
    public void MergingPreservesLibrarySelectionAndMemberTypes(string library, string source)
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "promise-invalid", "main.ts"));
        var resolver = new ModuleResolver(path,
            new Dictionary<string, string> { [path] = source },
            TypeScriptProgramOptions.Default with { Lib = [library] });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(maxErrors: 50);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.NotEmpty(checker.GetDiagnostics());
    }
}
