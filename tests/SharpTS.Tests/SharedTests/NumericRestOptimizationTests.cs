using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NumericRestOptimizationTests
{
    [Theory, ModeData]
    public void UsingBindingsShadowRestCallees(ExecutionMode mode)
    {
        const string source = """
            function add(...values: number[]): number { return values[0] + values[1]; }
            function run(): void {
                { using add: any = null;
                  try { console.log(add(1, 2)); } catch { console.log("shadow"); } }
            }
            run();
            """;
        Assert.Equal("shadow\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RestStorageIsFreshAndIndependent(ExecutionMode mode)
    {
        const string source = """
            function collect(...values: number[]): number[] { return values; }
            const input = [1, 2, 3];
            const first = collect(...input);
            const second = collect(...input);
            first[0] = 9;
            first.push(4);
            console.log(first.length, second.length, input[0], second[0]);
            console.log(collect() === collect());
            console.log(collect(7).length, collect(7)[0]);
            """;
        Assert.Equal("4 3 1 1\nfalse\n1 7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AliasesRespectShadowingAndCapturedFunctionValue(ExecutionMode mode)
    {
        const string source = """
            function add(...values: number[]): number { return values[0] + values[1]; }
            function mul(...values: number[]): number { return values[0] * values[1]; }
            function run(): number {
                const alias = add;
                const chain = alias;
                let result = chain(2, 3);
                { const alias = mul; result = result + alias(2, 3); }
                return result + alias(2, 3);
            }
            function shadow(add: (...values: number[]) => number): number {
                const alias = add;
                return alias(2, 3);
            }
            console.log(run(), shadow(mul));
            export function changing(...values: number[]): number { return values[0] + values[1]; }
            const original = changing;
            changing = mul;
            console.log(original(2, 3));
            """;
        Assert.Equal("16 6\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstantAndVaryingRestIndicesPreserveOrdinaryReads(ExecutionMode mode)
    {
        const string source = """
            function pick(start: number, ...values: number[]): number {
                return values[start] + values[start + 1];
            }
            function changed(start: number, ...values: number[]): number {
                start++;
                return values[start];
            }
            console.log(pick(0, 2, 3), pick(1, 2, 3, 4));
            for (let i = 0; i < 2; i++) console.log(pick(i, 2, 3, 4));
            console.log(pick(-1, 2, 3), pick(0.5, 2, 3), pick(9, 2, 3));
            console.log(changed(0, 2, 3));
            """;
        Assert.Equal("5 7\n5\n7\nNaN NaN NaN\n3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedRegularArgumentsSurviveSuspension(ExecutionMode mode)
    {
        const string source = """
            function pick(prefix: number, ...values: number[]): number {
                values[0] = values[0] + 1;
                return prefix + values[0] + values.length;
            }
            async function run(): Promise<void> {
                console.log(pick(10, await Promise.resolve(20), 30));
            }
            function* gen(): Generator<number> {
                yield pick(10, yield 20, 30);
            }
            const g = gen();
            console.log(g.next().value);
            console.log(g.next(20).value);
            run();
            """;
        Assert.Equal("20\n33\n33\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpreadIsConsumedBeforeLaterArgumentsAndCanSupplyRegularParameters(ExecutionMode mode)
    {
        const string source = """
            let trace = "";
            function* items(): Generator<number> {
                trace = trace + "s"; yield 1;
                trace = trace + "t"; yield 2;
                trace = trace + "u";
            }
            function mark(value: number): number { trace = trace + value; return value; }
            function pack(prefix: number = 10, ...values: number[]): string {
                return prefix + ":" + values.join(",");
            }
            console.log(pack(...items(), mark(3), ...[4, 5]), trace);
            console.log(pack(...[]));
            const tail = [1, 2];
            function mutate(): number { tail[0] = 9; return 3; }
            console.log(pack(0, ...tail, mutate()), tail[0]);
            """;
        Assert.Equal("1:2,3,4,5 stu3\n10:\n0:1,2,3 9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpreadPreservesHolesAndCustomIterators(ExecutionMode mode)
    {
        const string source = """
            function pack(...values: any[]): string { return values.length + ":" + values.join(","); }
            const sparse: any[] = [];
            sparse.length = 3;
            sparse[2] = 9;
            console.log(pack(...sparse));
            const custom: any = [1, 2];
            custom[Symbol.iterator] = function* (): Generator<number> { yield 7; yield 8; };
            console.log(pack(...custom));
            console.log(pack(..."abc"));
            """;
        Assert.Equal("3:,,9\n2:7,8\n3:a,b,c\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ThrowingSpreadSkipsLaterArguments(ExecutionMode mode)
    {
        const string source = """
            let later = 0;
            function mark(): number { later++; return 3; }
            function* bad(): Generator<number> { yield 1; throw new Error("stop"); }
            function pack(...values: number[]): number { return values.length; }
            try { pack(...bad(), mark()); } catch (e) { console.log("caught"); }
            console.log(later);
            """;
        Assert.Equal("caught\n0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExpandedStorageSurvivesSuspension(ExecutionMode mode)
    {
        const string source = """
            function pack(prefix: number, ...values: number[]): string {
                return prefix + ":" + values.join(",");
            }
            async function run(): Promise<void> {
                console.log(pack(...[1, 2], await Promise.resolve(3), ...[4]));
            }
            run();
            """;
        Assert.Equal("1:2,3,4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpreadMaterializesHolesAsOwnUndefinedElements(ExecutionMode mode)
    {
        const string source = """
            function collect(...values: any[]): any[] { return values; }
            const source: any[] = [1, 2];
            delete source[0];
            const copy = collect(...source);
            console.log(0 in source, 0 in copy, copy[0] === undefined, copy.length);
            """;
        Assert.Equal("false true true 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RestFallbacks_PreserveFreshArraysAndMissingArguments(ExecutionMode mode)
    {
        const string source = """
            function collect(...values: number[]): number[] { return values; }
            const first = collect(1, 2);
            const second = collect(1, 2);
            first[0] = 9;
            console.log(first === second, second[0]);
            function read(...values: number[]): any { return values[1]; }
            function run(): void { const alias = read; console.log(alias(1)); }
            run();
            const tail = [2, 3];
            const spread = collect(1, ...tail);
            spread[1] = 8;
            console.log(tail[0], spread.length);
            """;
        Assert.Equal("false 1\nundefined\n2 3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RestAlias_ShadowedTargetAndEarlyClosureKeepLexicalSemantics(ExecutionMode mode)
    {
        const string source = """
            function add(...values: number[]): number { return values[0] + values[1]; }
            function shadow(add: (...values: number[]) => number): number {
                const localAlias = add;
                return localAlias(3, 4);
            }
            console.log(shadow((...values: number[]): number => values[0] * values[1]));
            function early(): void {
                function read(): number { return later(3, 4); }
                try { read(); } catch { console.log("tdz"); }
                const later = add;
                console.log(read());
            }
            early();
            """;
        Assert.Equal("12\ntdz\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpecializedRestCalls_EvaluateArgumentsOnceInOrder(ExecutionMode mode)
    {
        const string source = """
            function pair(start: number, ...values: number[]): number {
                return values[start] * 10 + values[start + 1];
            }
            function run(): void {
                const orderedAlias = pair;
                let counter = 1;
                console.log(orderedAlias(0, counter++, counter++), counter);
                console.log(pair(0, counter++, counter++), counter);
            }
            run();
            """;
        Assert.Equal("12 3\n34 5\n", TestHarness.Run(source, mode));
    }
}
