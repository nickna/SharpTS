using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncDeclarationFactoryIdentityTests
{
    public const string IssueSource = """
        function make(): any { async function nested() {} return nested; }
        const first = make(), second = make();
        first.tag = 7;
        console.log(first === second, second.tag);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["nested-async-factory-identity-issue", IssueSource, "false undefined\n"];
        yield return ["independent-own-descriptors-symbols-and-prototypes", """
            function make(): any { async function nested(first: any, second: any) {} return nested; }
            const first = make(), second = make(), symbol = Symbol('slot');
            const shared = Object.getPrototypeOf(second), custom = Object.create(shared);
            Object.defineProperty(first, 'slot', { value: 7, writable: false, enumerable: true, configurable: true });
            Object.defineProperty(first, 'name', { value: 'renamed', configurable: true });
            first[symbol] = 11;
            Object.setPrototypeOf(first, custom);
            const slot = Object.getOwnPropertyDescriptor(first, 'slot')!;
            console.log(first !== second, slot.value, slot.writable, slot.enumerable, slot.configurable);
            console.log(Object.hasOwn(second, 'slot'), Object.getOwnPropertyDescriptor(second, 'slot') === undefined,
                Object.getOwnPropertyDescriptor(second, 'name')!.value === 'nested', second.length);
            console.log(Object.getPrototypeOf(first) === custom, Object.getPrototypeOf(second) === shared,
                Object.getPrototypeOf(first) !== Object.getPrototypeOf(second));
            console.log(Object.getOwnPropertySymbols(first)[0] === symbol,
                Object.getOwnPropertySymbols(second).length, Reflect.get(first, symbol), Reflect.get(second, symbol));
            """, "true 7 false true true\nfalse true true 2\ntrue true true\ntrue 0 11 undefined\n"];
        yield return ["integrity-state-is-independent-between-factory-calls", """
            function make(): any { async function nested() {} return nested; }
            const first = make(), second = make(), third = make();
            Object.preventExtensions(first);
            Object.defineProperty(second, 'slot', { value: 7, writable: true, configurable: true });
            console.log(first !== second, Object.isExtensible(first), Object.isExtensible(second),
                second.slot, Object.hasOwn(first, 'slot'));
            Object.freeze(second);
            third.tag = 9;
            console.log(Object.isFrozen(second), Object.isExtensible(third), third.tag, Object.hasOwn(second, 'tag'));
            Object.seal(third);
            const fourth = make();
            Object.defineProperty(fourth, 'slot', { value: 8, configurable: true });
            console.log(Object.isSealed(third), Object.isSealed(fourth), Object.isExtensible(fourth), fourth.slot);
            """, "true false true 7 false\ntrue true 9 false\ntrue false true 8\n"];
        yield return ["hoisted-and-repeated-reads-share-one-activation-value", """
            function make(): any {
                const early: any = nested;
                async function nested() {}
                const later: any = nested;
                return [early, later, nested];
            }
            const first = make(), second = make();
            console.log(first[0] === first[1], first[1] === first[2], first[0] !== second[0]);
            first[0].tag = 7;
            console.log(first[1].tag, first[2].tag, second[0].tag);
            Object.defineProperty(first[1], 'slot', { value: 9, configurable: true });
            console.log(Object.getOwnPropertyDescriptor(first[0], 'slot')!.value,
                Object.getOwnPropertyDescriptor(first[2], 'slot')!.value, Object.hasOwn(second[0], 'slot'));
            """, "true true true\n7 7 undefined\n9 9 false\n"];
        yield return ["captured-declarations-keep-fresh-values-and-capture-environments", """
            function make(seed: number): any {
                async function nested() { return seed; }
                return [nested, nested];
            }
            const first = make(3), second = make(9);
            console.log(first[0] === first[1], second[0] === second[1], first[0] !== second[0]);
            first[0].tag = 7;
            console.log(first[1].tag, second[0].tag);
            first[0]().then((value: any) => { console.log(value); return second[0](); })
                .then((value: any) => console.log(value))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true true true\n7 undefined\n3\n9\n"];
        yield return ["recursive-declaration-returns-its-activation-function-value", """
            function make(): any {
                async function nested(depth: number): Promise<any> {
                    if (depth === 0) return nested;
                    return nested(depth - 1);
                }
                return nested;
            }
            const first = make(), second = make();
            first(2).then((self: any) => console.log(self === first, self !== second))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true true\n"];
        yield return ["nested-async-declaration-keeps-own-receiver-and-raw-arguments", """
            async function outer(this: any): Promise<void> {
                async function nested(this: any, first: any, second: any): Promise<any> {
                    return [this.label, arguments.length, arguments[0], arguments[1], arguments[2], first, second];
                }
                const target: any = nested;
                console.log(this.label, target.length);
                return target.call({ label: 'call' }, 3, 4).then((result: any) => {
                    console.log(result.join('|'));
                    return target.apply({ label: 'apply' }, [5, 6, 7]);
                }).then((result: any) => console.log(result.join('|')));
            }
            outer.call({ label: 'outer' }).catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "outer 2\ncall|2|3|4||3|4\napply|3|5|6|7|5|6\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(
        AsyncCallableOwnPropertyDeletionTests.Cases().Where(row => (string)row[0] is
            "absent-and-integrity-deletion" or "strict-absent-and-frozen-string-deletion"));

    [Theory, MemberData(nameof(ModeCases))]
    public void AsyncDeclarationEvaluationCreatesFreshOwnStatePerActivation(
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
