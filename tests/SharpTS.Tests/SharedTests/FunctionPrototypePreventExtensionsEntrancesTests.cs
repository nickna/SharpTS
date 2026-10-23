using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionPrototypePreventExtensionsEntrancesTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-direct", """
            const prototype: any = Function.prototype;
            Reflect.preventExtensions(prototype);
            prototype.fresh = 1;
            console.log(Object.isExtensible(prototype), prototype.fresh === undefined,
                Object.hasOwn(prototype, 'fresh'));
            """.ReplaceLineEndings("\n") + "\n", "false true false\n"];
        yield return ["reflect-proxy", """
            const prototype: any = Function.prototype;
            Reflect.preventExtensions(new Proxy(prototype, {}));
            prototype.fresh = 1;
            console.log(Object.isExtensible(prototype), prototype.fresh === undefined,
                Object.hasOwn(prototype, 'fresh'));
            """.ReplaceLineEndings("\n") + "\n", "false true false\n"];
        yield return ["object-proxy", """
            const prototype: any = Function.prototype;
            Object.preventExtensions(new Proxy(prototype, {}));
            prototype.fresh = 1;
            console.log(Object.isExtensible(prototype), prototype.fresh === undefined,
                Object.hasOwn(prototype, 'fresh'));
            """.ReplaceLineEndings("\n") + "\n", "false true false\n"];
    }

    public static IEnumerable<object[]> ModeCases()
        => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AllEntrancesRejectFreshOwnProperties(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
