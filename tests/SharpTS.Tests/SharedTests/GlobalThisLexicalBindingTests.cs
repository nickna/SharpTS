using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GlobalThisLexicalBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["globalthis-parameter-property-read", """
            function read(globalThis: any) { return globalThis.value; }
            console.log(read({ value: 11 }));
            """ + "\n", "11\n"];
        yield return ["globalthis-lexical-property-routing", """
            const real: any = globalThis;
            function parameter(globalThis: any) {
                const alias: any = globalThis, key = 'value';
                console.log('parameter', globalThis.value, globalThis[key], alias === globalThis,
                    globalThis.Math.PI, globalThis.Math.floor(4), globalThis.Reflect.tag,
                    globalThis.Reflect.get({}, 'value'), globalThis.globalThis.value);
            }
            parameter({ value: 11, Math: { PI: 12, floor: (value: number) => value + 10 },
                Reflect: { tag: 13, get: (_target: any, _key: string) => 15 }, globalThis: { value: 16 } });
            {
                const globalThis: any = { value: 21, Math: { PI: 22, floor: (_value: number) => 24 },
                    Reflect: { tag: 23, get: (_target: any, _key: string) => 25 }, globalThis: { value: 26 } };
                const alias: any = globalThis;
                console.log('block', globalThis.value, globalThis['value'], alias === globalThis,
                    globalThis.Math.PI, globalThis.Math.floor(4), globalThis.Reflect.tag,
                    globalThis.Reflect.get({}, 'value'), globalThis.globalThis.value);
            }
            console.log('real', real.globalThis === real, globalThis === real, typeof real.Math);
            """ + "\n", "parameter 11 11 true 12 14 13 15 16\nblock 21 21 true 22 24 23 25 26\nreal true true object\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LexicalBindingsOverrideGlobalThisPropertyRouting(
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
