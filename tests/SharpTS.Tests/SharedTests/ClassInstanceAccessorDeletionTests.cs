using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassInstanceAccessorDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["configurable-class-instance-accessor", """
            class C { stable = 10; }
            const target: any = new C();
            let reads = 0;
            Object.defineProperty(target, 'accessor', { configurable: true, get() { reads++; return 8; } });
            console.log(delete target.accessor, Object.hasOwn(target, 'accessor'), reads);
            """.ReplaceLineEndings("\n") + "\n", "true false 0\n"];
        yield return ["class-instance-accessor-deletion-controls", """
            class C { stable = 10; }
            const target: any = new C();
            let reads = 0, writes = 0;
            Object.defineProperty(target, 'slot', { configurable: true, get() { reads++; return 1; }, set(value: any) { writes++; } });
            console.log('unary', delete target.slot, Object.hasOwn(target, 'slot'), Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            target.slot = 7;
            console.log('recreated', target.slot, reads, writes);
            Object.defineProperty(target, 'reflected', { configurable: true, get() { reads++; return 2; }, set(value: any) { writes++; } });
            console.log('reflect', Reflect.deleteProperty(target, 'reflected'), Object.hasOwn(target, 'reflected'), Object.getOwnPropertyDescriptor(target, 'reflected') === undefined);
            target.reflected = 9;
            console.log('reflect-recreated', target.reflected, reads, writes);
            Object.defineProperty(target, 'locked', { configurable: false, get() { reads++; return 3; }, set(value: any) { writes++; } });
            console.log('locked', delete target.locked, Reflect.deleteProperty(target, 'locked'), Object.hasOwn(target, 'locked'), reads, writes);
            class StrictDeleter { remove(value: any) { return delete value.locked; } }
            try { console.log('unexpected', new StrictDeleter().remove(target)); }
            catch (error) { console.log('strict', error instanceof TypeError, reads, writes); }
            """.ReplaceLineEndings("\n") + "\n", "unary true false true\nrecreated 7 0 0\nreflect true false true\nreflect-recreated 9 0 0\nlocked false false true 0 0\nstrict true 0 0\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InterpreterRemovesConfigurableAccessorsWithoutInvokingThem(
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
