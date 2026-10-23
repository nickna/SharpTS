using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PromiseCallbackOwnPropertyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-resolve-and-reject-define-property", """
            const callbacks: any[] = [];
            const pending = new Promise((resolve: any, reject: any) => { callbacks.push(resolve, reject); });
            for (const callback of callbacks) {
                try {
                    Object.defineProperty(callback, 'slot', { value: 7, configurable: true });
                    console.log(Object.getOwnPropertyDescriptor(callback, 'slot')!.value);
                } catch (error: any) {
                    console.log(error.name, error.message);
                }
            }
            """, "7\n7\n"];
        yield return ["resolving-callback-data-property-lifecycle", """
            const callbacks: any[] = [];
            const pending = new Promise((resolve: any, reject: any) => { callbacks.push(resolve, reject); });
            for (const callback of callbacks) {
                try {
                    const defined = Object.defineProperty(callback, "slot", {
                        value: 7, writable: true, enumerable: true, configurable: true
                    });
                    const descriptor = Object.getOwnPropertyDescriptor(callback, "slot")!;
                    console.log(callback.slot, descriptor.value, descriptor.enumerable,
                        Object.hasOwn(callback, "slot"), Object.keys(callback).join(","));
                    console.log(delete callback.slot, Object.hasOwn(callback, "slot"));
                } catch (error: any) {
                    console.log(error.name, error.message);
                }
            }
            """, "7 7 true true slot\ntrue false\n7 7 true true slot\ntrue false\n"];
        yield return ["resolving-callback-accessor-getter-identity", """
            const callbacks: any[] = [];
            const pending = new Promise((resolve: any, reject: any) => { callbacks.push(resolve, reject); });
            for (const callback of callbacks) {
                try {
                    let calls = 0;
                    const getter = () => { calls++; return 7; };
                    const defined = Object.defineProperty(callback, "slot", { get: getter, configurable: true });
                    const descriptor = Object.getOwnPropertyDescriptor(callback, "slot")!;
                    console.log(callback.slot, calls, descriptor.get === getter);
                } catch (error: any) {
                    console.log(error.name, error.message);
                }
            }
            """, "7 1 true\n7 1 true\n"];
        yield return ["resolving-callback-callability-after-property", """
            let resolveCallback: any;
            let rejectCallback: any;
            const fulfilled = new Promise<number>((resolve: any) => { resolveCallback = resolve; });
            const rejected = new Promise<number>((resolve: any, reject: any) => { rejectCallback = reject; });
            let fulfilledValue: any;
            let rejectedValue: any;
            let completed = 0;
            function report() {
                completed++;
                if (completed === 2) {
                    console.log("resolve", fulfilledValue);
                    console.log("reject", rejectedValue);
                }
            }
            const fulfilledHandler = fulfilled.then(value => { fulfilledValue = value; report(); });
            const rejectedHandler = rejected.then(undefined, (reason: any) => { rejectedValue = reason; report(); });
            for (const callback of [resolveCallback, rejectCallback]) {
                try {
                    const defined = Object.defineProperty(callback, "slot", { value: 7, configurable: true });
                } catch (error: any) {
                    console.log(error.name, error.message);
                }
            }
            resolveCallback(7);
            rejectCallback(8);
            """, "resolve 7\nreject 8\n"];
        yield return ["builtin-method-and-primitive-target-controls", """
            try {
                const defined = Object.defineProperty(Math.abs, "slot", { value: 7, configurable: true });
                console.log((Math.abs as any).slot);
            } catch (error: any) {
                console.log(error.name, error.message);
            }
            try {
                const defined = Object.defineProperty(42, "slot", { value: 7 });
            } catch (error: any) {
                console.log(error.name);
            }
            """, "7\nTypeError\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ResolvingCallbacksRetainOwnPropertyDefinitions(
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
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
