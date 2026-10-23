using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericFunctionAliasTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            let calls = 0;
            function identity<T>(value: T): T { calls++; return value; }
            const numberIdentity = identity;
            console.log(numberIdentity === identity, calls);
            console.log(numberIdentity(7), calls);
            """.ReplaceLineEndings("\n") + "\n", "true 0\n7 1\n"];
        yield return ["multi-type-and-class-constraint", """
            let calls = 0;
            function identity<T>(value: T): T { calls++; return value; }
            const alias = identity;
            console.log(alias(7), alias("text"), alias(true), alias === identity, calls);
            class Box { value: number; constructor(value: number) { this.value = value; } }
            function read<T extends Box>(value: T): number { return value.value; }
            const readAlias = read;
            console.log(readAlias(new Box(9)), readAlias === read);
            """.ReplaceLineEndings("\n") + "\n", "7 text true true 3\n9 true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AliasesRemainCallableWithoutChangingIdentity(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertStrictSource(source);
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

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "generic-function-alias-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
