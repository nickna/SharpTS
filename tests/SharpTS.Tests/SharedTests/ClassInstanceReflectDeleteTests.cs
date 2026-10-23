using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstanceReflectDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reflect-delete-nonconfigurable-class-property", """
            class C { stable = 10; }
            const target: any = new C();
            Object.defineProperty(target, 'locked', { value: 5, writable: true, configurable: false });
            console.log(Reflect.deleteProperty(target, 'locked'), Object.hasOwn(target, 'locked'), target.locked);
            """.ReplaceLineEndings("\n") + "\n", "false true 5\n"];
        yield return ["reflect-delete-configurable-and-absent-class-properties", """
            class C { stable = 10; }
            const target: any = new C();
            Object.defineProperty(target, 'slot', { value: 7, writable: true, configurable: true });
            console.log(Reflect.deleteProperty(target, 'slot'), Object.hasOwn(target, 'slot'), target.stable);
            console.log(Reflect.deleteProperty(target, 'missing'), Object.hasOwn(target, 'missing'));
            Object.seal(target);
            console.log(Reflect.deleteProperty(target, 'missing'), Object.hasOwn(target, 'missing'), target.stable);
            """.ReplaceLineEndings("\n") + "\n", "true false 10\ntrue false\ntrue false 10\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReflectDeletionReportsTheActualClassInstanceResult(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        var result = SourceExecutionService.Interpret(source);
        Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
        Assert.Empty(result.Errors);
        Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
    }
}
