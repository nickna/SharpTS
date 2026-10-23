using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArgumentsIndexGrowthTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            function check(first: number, second: number) {
                const args: any = arguments;
                const reflected = Reflect.set(args, "2", 30);
                console.log("before", reflected, args[2], args.length);
                args[3] = 40;
                console.log("after", args[2], args[3], args.length);
            }
            check(1, 2);
            """.ReplaceLineEndings("\n") + "\n", "before true 30 2\nafter 30 40 2\n"];
        yield return ["mixed-defined-reflected-growth-descriptors", """
            function check(first: number, second: number) {
                const args: any = arguments;
                Object.defineProperty(args, "3", { value: 30, writable: true, enumerable: true, configurable: true });
                const reflected = Reflect.set(args, "4", 40);
                args[5] = 50;
                const firstDescriptor = Object.getOwnPropertyDescriptor(args, "3")!;
                const secondDescriptor = Object.getOwnPropertyDescriptor(args, "4")!;
                console.log(reflected, args[3], args[4], args[5], args.length, Object.hasOwn(args, "2"));
                console.log(firstDescriptor.value, secondDescriptor.value,
                    firstDescriptor.writable, firstDescriptor.enumerable, firstDescriptor.configurable,
                    secondDescriptor.writable, secondDescriptor.enumerable, secondDescriptor.configurable);
            }
            check(1, 2);
            """.ReplaceLineEndings("\n") + "\n", "true 30 40 50 2 false\n30 40 true true true true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DefinedAndReflectedIndicesSurviveBackingGrowth(
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
        var path = Path.Combine(Path.GetTempPath(), "arguments-index-growth-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
