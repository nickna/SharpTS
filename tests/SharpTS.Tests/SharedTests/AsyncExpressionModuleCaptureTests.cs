using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncExpressionModuleCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact", """
            const target: any = async function() { return later; };
            const later = 7;
            target().then((value: any) => console.log(value));
            """ + "\r\n", "7\n"];
        yield return ["earlier-const-positive", """
            const later = 7;
            const target: any = async function() { return later; };
            target().then((value: any) => console.log(value));
            """ + "\r\n", "7\n"];
        yield return ["ordinary-later-positive", """
            const target: any = function() { return later; };
            const later = 7;
            console.log(target());
            """ + "\r\n", "7\n"];
        yield return ["tdz-live-module-and-local-capture-preservation", """
            const readLater: any = async function() { return later; };
            const early: any = readLater();
            const later = 7;
            let value: any = 1;
            const readModule: any = async function() { return value; };
            function make(initial: any): any {
                let value: any = initial;
                return async function() { value = value + 1; return value; };
            }
            const firstLocal: any = make(1), secondLocal: any = make(10);
            async function observe() {
                try { await early; console.log("tdz", "unexpected"); }
                catch (error: any) { console.log("tdz", error.name); }
                console.log("later", await readLater());
                const before: any = await readModule();
                value = 7;
                console.log("module", before, await readModule());
                console.log("local", await firstLocal(), await firstLocal(), await secondLocal(), value);
            }
            const observation: any = observe();
            """ + "\n", "tdz ReferenceError\nlater 7\nmodule 1 7\nlocal 2 3 11 7\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(new[]
    {
        new object[] { "full-original-private-self-receiver-views",
            AsyncFunctionExpressionCaptureTests.ReceiverViewsSource,
            AsyncFunctionExpressionCaptureTests.ReceiverViewsExpected }
    });

    [Theory, MemberData(nameof(ModeCases))]
    public void CapturesRetainTheLiveModuleEnvironment(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
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
}
