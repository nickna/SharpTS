using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3ReflectAmbientTests
{
    [Fact]
    public void OptionalNewTargetAcceptsExplicitUndefined()
        => Assert.Empty(CheckProgram("""
            function Target() {}
            Reflect.construct(Target, [], undefined);
            """));

    internal static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> CheckProgram(string source)
    {
        string path = Path.Combine(Path.GetTempPath(), "reflect-ambient", "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { RespectLoadedLibraries = true });
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
