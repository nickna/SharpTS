using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ReflectNamespaceValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["bare-reflect-namespace-value", """
            const value: any = Reflect;
            console.log(value === Reflect);
            """, "true\n"];
        yield return ["direct-and-detached-reflect-control", """
            const target: any = {};
            console.log(Reflect.defineProperty(target, 'slot', { value: 7 }), target.slot);
            const get: any = Reflect.get;
            console.log(get(target, 'slot'));
            """, "true 7\n7\n"];
        yield return ["reflect-namespace-identity-dispatch-and-shadowing", """
            const namespace: any = Reflect;
            console.log('identity', namespace === Reflect, namespace === globalThis.Reflect, typeof namespace);
            const target: any = {};
            console.log('direct', Reflect.defineProperty(target, 'value', { value: 7, configurable: true }), namespace.get(target, 'value'));
            const define: any = namespace.defineProperty, get: any = Reflect.get;
            console.log('detached', define(target, 'value', { value: 9, configurable: true }), get(target, 'value'));
            const receiver: any = { value: 12 };
            let reads = 0, sameReceiver = false;
            define(target, 'selected', {
                get: function(this: any) { reads++; sameReceiver = this === receiver; return this.value; },
                configurable: true
            });
            console.log('receiver', get(target, 'selected', receiver), sameReceiver, reads);
            function shadow(Reflect: any) { return Reflect.get({}, 'value'); }
            const local: any = { get: (_target: any, _key: any) => 21 };
            {
                const Reflect: any = { value: 31 };
                console.log('shadow', shadow(local), Reflect.value, namespace === globalThis.Reflect);
            }
            console.log('stable', namespace === Reflect, namespace === globalThis.Reflect);
            """ + "\n", "identity true true object\ndirect true 7\ndetached true 9\nreceiver 12 true 1\nshadow 21 31 true\nstable true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReflectNamespaceValuesRetainCanonicalIdentityAndDispatch(
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
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
