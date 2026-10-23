using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class MathAccessorPropertyReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct-own-accessor-issue", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, "max")!;
            let calls = 0, receiver = false, kind = "";
            try {
                Object.defineProperty(target, "max", {
                    configurable: true,
                    get() {
                        calls++;
                        receiver = this === target;
                        return () => 17;
                    }
                });
                kind = typeof Math.max;
            } finally { Object.defineProperty(target, "max", original); }
            console.log(kind, calls, receiver);
            """, "function 1 true\n"];
        yield return ["math-own-live-values-getterless-delete-restore", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, 'max')!;
            let reads = 0, receiver = false;
            try {
                Object.defineProperty(target, 'max', {
                    configurable: true,
                    get() { reads++; receiver = this === target; return 17; }
                });
                const direct: any = Math.max, alias = target.max, indexed = target['max'];
                console.log(direct, alias, indexed, reads, receiver);
                Object.defineProperty(target, 'max', { configurable: true, get: undefined, set(value: any) {} });
                const missingGetter: any = Math.max;
                console.log(missingGetter === undefined, reads);
                console.log(delete target.max, Object.hasOwn(target, 'max'));
                const deleted: any = Math.max;
                console.log(deleted === undefined, reads);
            } finally { Object.defineProperty(target, 'max', original); }
            console.log(Math.max === original.value, Math.max(1, 2));
            """, "17 17 17 3 true\ntrue 3\ntrue false\ntrue 3\ntrue 2\n"];
        yield return ["math-inherited-receiver-and-nearest-shadow", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, 'max')!;
            const child: any = Object.create(target), alternate: any = {};
            let reads = 0;
            try {
                Object.defineProperty(target, 'max', {
                    configurable: true,
                    get() { reads++; return this; }
                });
                console.log(child.max === child, Reflect.get(target, 'max', alternate) === alternate, target.max === target, reads);
                Object.defineProperty(child, 'max', { value: 23, configurable: true });
                console.log(child.max, reads);
                Object.defineProperty(child, 'max', { configurable: true, get: undefined, set(value: any) {} });
                console.log(child.max === undefined, reads);
                delete child.max;
                console.log(child.max === child, reads);
            } finally { Object.defineProperty(target, 'max', original); }
            console.log(Math.max(1, 2));
            """, "true true true 3\n23 3\ntrue 3\ntrue 4\n2\n"];
        yield return ["math-throwing-getter-preserves-identity-and-receiver", """
            const target: any = Math;
            const original = Object.getOwnPropertyDescriptor(target, 'max')!;
            const child: any = Object.create(target), marker: any = {};
            const events: string[] = [];
            try {
                Object.defineProperty(target, 'max', {
                    configurable: true,
                    get() { events.push(this === target ? 'target' : this === child ? 'child' : 'other'); throw marker; }
                });
                try { const value: any = Math.max; console.log('unexpected', value); }
                catch (error: any) { console.log(error === marker); }
                try { const value: any = child.max; console.log('unexpected', value); }
                catch (error: any) { console.log(error === marker); }
                console.log(events.join(','));
            } finally { Object.defineProperty(target, 'max', original); }
            console.log(Math.max(1, 2));
            """, "true\ntrue\ntarget,child\n2\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ReadsInvokeMathAccessorsWithTheOriginalReceiver(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
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
