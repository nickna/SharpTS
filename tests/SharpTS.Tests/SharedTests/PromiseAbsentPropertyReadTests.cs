using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PromiseAbsentPropertyReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["absent-intrinsic-promise-property-issue", """
            const promise: any = Promise.resolve(1);
            console.log(promise.missing === undefined, promise.missing === null,
                typeof promise.missing, Object.hasOwn(promise, "missing"));
            """, "true false undefined false\n"];
        yield return ["guest-subclass-and-ordinary-absent-property-controls", """
            class P extends Promise<number> {}
            const guest: any = new P(resolve => resolve(1)), ordinary: any = {};
            for (const target of [guest, ordinary] as any[]) {
                const key: string = 'missing';
                console.log(target.missing === undefined, target[key] === undefined, typeof target[key],
                    Object.hasOwn(target, key), Object.getOwnPropertyDescriptor(target, key) === undefined);
            }
            """, "true true undefined false true\ntrue true undefined false true\n"];
        yield return ["present-null-and-undefined-remain-distinct-from-deleted-promise-properties", """
            class P extends Promise<number> {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2))] as any[]) {
                Object.defineProperty(target, 'nil', { value: null, writable: true, configurable: true });
                Object.defineProperty(target, 'empty', { value: undefined, writable: true, configurable: true });
                console.log(target.nil === null, target.empty === undefined, Object.hasOwn(target, 'nil'),
                    Object.hasOwn(target, 'empty'), target.missing === undefined);
                target.slot = null;
                console.log(target.slot === null, Object.hasOwn(target, 'slot'));
                delete target.slot;
                console.log(target.slot === undefined, Object.hasOwn(target, 'slot'),
                    Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            }
            """, "true true true true true\ntrue true\ntrue false true\ntrue true true true true\ntrue true\ntrue false true\n"];
        yield return ["promise-getter-preserves-receiver-order-and-nullish-return-values", """
            class P extends Promise<number> {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2))] as any[]) {
                const events: string[] = [];
                let selected: any = null, reads = 0;
                Object.defineProperty(target, 'slot', {
                    configurable: true,
                    get() { reads++; events.push('get:' + (this === target)); return selected; }
                });
                function receiver(): any { events.push('receiver'); return target; }
                function key(): string { events.push('key'); return 'slot'; }
                const first = receiver()[key()];
                console.log(first === null, events.join(','), reads);
                events.length = 0; selected = undefined;
                const second = target.slot;
                console.log(second === undefined, Object.hasOwn(target, 'slot'), events.join(','), reads);
                delete target.slot; events.length = 0;
                console.log(target.slot === undefined, events.length, reads, Object.hasOwn(target, 'slot'));
            }
            """, "true receiver,key,get:true 1\ntrue true get:true 2\ntrue 0 2 false\ntrue receiver,key,get:true 1\ntrue true get:true 2\ntrue 0 2 false\n"];
        yield return ["descriptor-installed-nullish-values-and-promise-deletion-controls", """
            class P extends Promise<number> {}
            for (const target of [Promise.resolve(1), new P(resolve => resolve(2))] as any[]) {
                Object.defineProperty(target, 'nil', { value: null, writable: true, configurable: true });
                Object.defineProperty(target, 'empty', { value: undefined, writable: true, configurable: true });
                console.log(target.nil === null, target.empty === undefined, Object.hasOwn(target, 'nil'),
                    Object.hasOwn(target, 'empty'), target.missing === undefined);
                Object.defineProperty(target, 'slot', { value: null, writable: true, configurable: true });
                console.log(target.slot === null, Object.hasOwn(target, 'slot'));
                delete target.slot;
                console.log(target.slot === undefined, Object.hasOwn(target, 'slot'),
                    Object.getOwnPropertyDescriptor(target, 'slot') === undefined);
            }
            """, "true true true true true\ntrue true\ntrue false true\ntrue true true true true\ntrue true\ntrue false true\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
        foreach (var row in SavedCases())
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void AbsentPromisePropertiesAreUndefinedWhilePresentNullishValuesArePreserved(
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
