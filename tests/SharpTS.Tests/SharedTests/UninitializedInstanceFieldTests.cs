using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class UninitializedInstanceFieldTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["expression", """
            const C = class { value; };
            const instance = new C();
            console.log(instance.value === undefined, typeof instance.value, instance.value === null);
            """.ReplaceLineEndings("\n") + "\n", "true undefined false\n"];
        yield return ["declaration", """
            class C { value; }
            const instance = new C();
            console.log(instance.value === undefined, typeof instance.value, instance.value === null);
            """.ReplaceLineEndings("\n") + "\n", "true undefined false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OmittedInitializersProduceGuestUndefined(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertDefaultSource(source);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }

    internal static void AssertDefaultSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "uninitialized-instance-field-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Default);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
