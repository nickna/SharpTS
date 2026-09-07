using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ObjectSpreadCopyTests
{
    [Theory, ModeData]
    public void PlainCopy_PreservesOverwriteOrderAndIndependentIdentity(ExecutionMode mode)
    {
        const string source = """
            function copy(first: any, second: any): any {
                return { before: 0, ...first, b: 8, ...second, after: 9 };
            }
            const first: any = { a: 1, b: 2, "": 3 };
            const second: any = { b: 4, c: 5 };
            const result: any = copy(first, second);
            console.log(Object.keys(result).join("|"));
            console.log(result.a + result.b + result.c + result[""]);
            result.a = 10;
            first.b = 20;
            console.log(first.a);
            console.log(result.b);
            console.log(result === first);
            """;
        Assert.Equal("before|a|b||c|after\n13\n1\n4\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericKeys_FallbackPreservesOrdering(ExecutionMode mode)
    {
        const string source = """
            function copy(value: any): any { return { prefix: 0, ...value, suffix: 1 }; }
            const value: any = { a: 1, "10": 10, "2": 2, "01": 1, "4294967295": 5 };
            const result: any = copy(value);
            console.log(Object.keys(result).join(","));
            console.log(result["10"] + result["2"] + result["01"]);
            """;
        Assert.Equal("2,10,prefix,a,01,4294967295,suffix\n13\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DescriptorFallback_PreservesGetterEffectsAndKeySnapshot(ExecutionMode mode)
    {
        const string source = """
            function copy(value: any): any { return { ...value }; }
            const value: any = { a: 1, b: 2 };
            let calls: number = 0;
            Object.defineProperty(value, "a", {
                enumerable: true,
                get: function(): number {
                    calls = calls + 1;
                    value.b = 20;
                    value.late = 30;
                    return 10;
                }
            });
            Object.defineProperty(value, "hidden", { value: 99, enumerable: false });
            const result: any = copy(value);
            console.log(Object.keys(result).join(","));
            console.log(result.a + result.b);
            console.log(calls);
            console.log(result.late === undefined);
            console.log(result.hidden === undefined);
            result.a = 40;
            console.log(result.a);
            """;
        Assert.Equal("a,b\n30\n1\ntrue\ntrue\n40\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SymbolReadsRemainFreshAndLaterWritesAreVisible(ExecutionMode mode)
    {
        const string source = """
            function copy(value: any): any { return { ...value }; }
            const value: any = { a: 1 };
            const before: any = Object.getOwnPropertySymbols(value);
            const again: any = Object.getOwnPropertySymbols(value);
            console.log(before === again);
            console.log(Object.keys(copy(value)).join(","));
            const visible: symbol = Symbol("visible");
            const hidden: symbol = Symbol("hidden");
            value[visible] = 7;
            Object.defineProperty(value, hidden, { value: 8, enumerable: false });
            const result: any = copy(value);
            console.log(before.length);
            console.log(Object.getOwnPropertySymbols(value).length);
            console.log(Object.getOwnPropertySymbols(result).length);
            console.log(result[visible]);
            console.log(result[hidden] === undefined);
            """;
        Assert.Equal("false\na\n0\n2\n1\n7\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapacityHint_DoesNotRestrictStructurallyWiderSource(ExecutionMode mode)
    {
        const string source = """
            function copy(value: { a: number, b: number, c: number }): any {
                return { ...value, d: 4 };
            }
            const wider = { a: 1, b: 2, c: 3, extra: 5 };
            const result: any = copy(wider);
            console.log(Object.keys(result).join(","));
            console.log(result.extra);
            """;
        Assert.Equal("a,b,c,extra,d\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConsecutiveSpreads_ObserveInterveningSourceMutation(ExecutionMode mode)
    {
        const string source = """
            function mutate(value: any): number { value.a = 7; value.c = 8; return 9; }
            function copy(value: any): any { return { ...value, middle: mutate(value), ...value }; }
            const value: any = { a: 1, b: 2 };
            const result: any = copy(value);
            console.log(Object.keys(result).join(","));
            console.log(result.a + result.b + result.middle + result.c);
            """;
        Assert.Equal("a,b,middle,c\n26\n", TestHarness.Run(source, mode));
    }
}
