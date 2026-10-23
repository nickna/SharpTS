using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class TypeofPropertyEvaluationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public-typeof-getter", """
            let calls = 0;
            const record = { get method() { calls++; return () => 17; } };
            console.log(typeof record.method, calls);
            """ + "\n", "function 1\n"];
        yield return ["typed-getter-evaluation-and-preservation", """
            let calls = 0;
            let received: any;
            let actual: any = () => 17;
            const record = {
                get method(): () => number {
                    calls++;
                    received = this;
                    return actual as any;
                }
            };
            console.log("function", typeof record.method, calls, received === record);
            actual = 19;
            console.log("number", typeof record.method, calls, received === record);
            actual = null;
            console.log("null", typeof record.method, calls, received === record);
            actual = undefined;
            console.log("undefined", typeof record.method, calls, received === record);
            actual = () => 23;
            const held = record.method;
            console.log("held", typeof held, calls);
            console.log("computed", typeof record["method"], calls, received === record);

            const token: any = { tag: "getter" };
            let throws = 0;
            const throwing = {
                get method(): () => number {
                    throws++;
                    throw token;
                }
            };
            try {
                console.log("unreachable", typeof throwing.method);
            } catch (error: any) {
                console.log("throw", error === token, throws);
            }

            {
                let shadowCalls = 0;
                const Math = {
                    get floor(): () => number {
                        shadowCalls++;
                        return 11 as any;
                    }
                };
                console.log("shadow", typeof Math.floor, shadowCalls);
            }
            console.log("native", typeof Math.floor, typeof JSON.parse, typeof Number.isFinite);

            async function run() {
                await Promise.resolve(0);
                actual = 7;
                console.log("async", typeof record.method, calls, received === record);
            }
            const task = run();
            const observed = task.catch((error: any) => console.log("unexpected", error.name, error.message));
            """ + "\n", "function function 1 true\nnumber number 2 true\nnull object 3 true\nundefined undefined 4 true\nheld function 5\ncomputed function 6 true\nthrow true 1\nshadow number 1\nnative function function function\nasync number 7 true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void TypeofReadsObserveActualPropertyValues(
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
