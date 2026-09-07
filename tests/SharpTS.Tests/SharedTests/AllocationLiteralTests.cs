using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AllocationLiteralTests
{
    [Theory, ModeData]
    public void PackedWritesPreserveNumericPropertyKeys(ExecutionMode mode)
    {
        const string source = """
            function read(values: any, key: number): any { return values["" + key]; }
            function write(values: number[], key: number): void {
                values[key] = 7;
                console.log(read(values, key), values.length, values[1]);
                console.log(values[key] = 9, read(values, key));
            }
            write([1, 2, 3], 1.5);
            write([1, 2, 3], -1);
            write([1, 2, 3], NaN);
            write([1, 2, 3], Infinity);
            write([1, 2, 3], 4294967296);
            const record = { values: [1, 2, 3] };
            console.log(record.values[-1] = 5, read(record.values, -1), record.values.length);
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("7 3 2\n9 9\n", 5)) + "5 5 3\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericLiteralsKeepDynamicElementsAndObjectArrayAliases(ExecutionMode mode)
    {
        const string source = """
            function make(value: any): number[] { return [1, value as number, 3]; }
            const values = make("text");
            console.log(typeof values[1], values[1]);
            const numbers: number[] = [1, 2, 3];
            const alias: any[] = numbers;
            let sum: number = 0;
            for (let i = 0; i < alias.length; i++) sum += alias[i];
            console.log(sum, numbers.join(","));
            """;
        Assert.Equal("string text\n6 1,2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RangeAppendRetainsEmptyFractionalAndNonemptyCases(ExecutionMode mode)
    {
        const string source = """
            function collect(start: number, end: number): string {
                const items: any[] = ["prefix"];
                for (let i = start; i < end; i++) items.push({ value: i });
                let result: string = "" + items.length;
                for (let j = 1; j < items.length; j++) result += ":" + items[j].value;
                return result;
            }
            console.log(collect(3, 6), collect(6, 3), collect(-2, 1));
            console.log(collect(0.5, 2), collect(2, 3.5), collect(NaN, 3));
            """;
        Assert.Equal("4:3:4:5 1 4:-2:-1:0\n3:0.5:1.5 3:2:3 1\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumberConcatenationPreservesFormattingAndEvaluationOrder(ExecutionMode mode)
    {
        const string source = """
            function label(value: number): string { return "item-" + (value % 100); }
            function suffix(value: number): string { return (value / 2) + "!"; }
            console.log(label(123), label(-0), label(NaN), suffix(Infinity), suffix(1e22));
            let calls: string = "";
            function text(): string { calls += "s"; return "x"; }
            function number(): number { calls += "n"; return 3; }
            console.log(text() + number(), number() + text(), calls);
            function dynamic(value: any): string { return "x" + (value as number); }
            const object: any = { valueOf: () => 7, toString: () => "wrong" };
            console.log(dynamic(object));
            """;
        Assert.Equal("item-23 item-0 item-NaN Infinity! 5e+21!\nx3 3x snns\nx7\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LiteralsPreserveOrderHolesAndIndependentStorage(ExecutionMode mode)
    {
        const string source = """
            let calls: number = 0;
            function next(): number { calls++; return calls; }
            const first: number[] = [next(), next(), next()];
            const second: number[] = [...first];
            first[0] = 99;
            const sparse: any[] = [, undefined, 3];
            console.log(first.join(","), second.join(","), calls);
            console.log(sparse.length, 0 in sparse, 1 in sparse);
            console.log([3].length, [3][0], new Array(3).length, 0 in new Array(3));
            """;
        Assert.Equal("99,2,3 1,2,3 3\n3 false true\n1 3 3 false\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedNumericLiteralsPreserveSpecialValuesAndAliasedWrites(ExecutionMode mode)
    {
        const string source = """
            const record = { values: [1, -0, NaN, Infinity] };
            const alias: any = record.values;
            console.log(record.values.length, 1 / record.values[1],
                Number.isNaN(record.values[2]), record.values[3]);
            alias[0] = "changed";
            alias.push("tail");
            console.log(record.values[0], record.values.length, alias[4]);
            delete alias[1];
            console.log(1 in record.values, record.values[1]);
            Object.defineProperty(alias, "2", { get: () => 7, configurable: true });
            console.log(record.values[2]);
            """;
        Assert.Equal("4 -Infinity true Infinity\nchanged 5 tail\nfalse undefined\n7\n",
            TestHarness.Run(source, mode));
    }
}
