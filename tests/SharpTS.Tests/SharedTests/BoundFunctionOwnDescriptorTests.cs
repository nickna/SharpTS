using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BoundFunctionOwnDescriptorTests
{
    // The existing BoundFunctionPrototypeTests sealed-deletion source stays in its original provider.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-bound-own-descriptors", """
            function f() {}
            const bound: any = f.bind(null);
            console.log(JSON.stringify(Object.getOwnPropertyDescriptor(bound, "length")));
            console.log(JSON.stringify(Object.getOwnPropertyDescriptor(bound, "name")));
            """.ReplaceLineEndings("\n") + "\n", "{\"value\":0,\"writable\":false,\"enumerable\":false,\"configurable\":true}\n{\"value\":\"bound f\",\"writable\":false,\"enumerable\":false,\"configurable\":true}\n"];
        yield return ["nested-bound-own-descriptors", """
            function add(first: number, second: number) { return first + second; }
            const firstBound: any = add.bind(null, 2);
            const secondBound: any = firstBound.bind(null, 3);
            const lengthDescriptor: any = Object.getOwnPropertyDescriptor(secondBound, "length");
            const nameDescriptor: any = Object.getOwnPropertyDescriptor(secondBound, "name");
            console.log(lengthDescriptor.value, lengthDescriptor.writable, lengthDescriptor.enumerable, lengthDescriptor.configurable);
            console.log(nameDescriptor.value, nameDescriptor.writable, nameDescriptor.enumerable, nameDescriptor.configurable);
            console.log(secondBound(), firstBound(5), Object.hasOwn(secondBound, "prototype"));
            """.ReplaceLineEndings("\n") + "\n", "0 false false true\nbound bound add false false true\n5 7 false\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void BoundWrappersExposeOwnNameAndLengthDescriptors(
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
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "bound-own-descriptor-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
