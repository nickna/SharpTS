using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompoundAssignmentCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (op, expected) in new[]
        {
            ("+=", "10"), ("-=", "6"), ("*=", "16"), ("/=", "4"), ("%=", "0"),
            ("&=", "0"), ("|=", "10"), ("^=", "10"),
            ("<<=", "32"), (">>=", "2"), (">>>=", "2")
        })
        {
            yield return [$"objects-{op}", $$"""
                let trace = "";
                const left: any = { valueOf() { trace += "l"; return 8; } };
                const right: any = { valueOf() { trace += "r"; return 2; } };
                let variable: any = left;
                console.log(variable {{op}} right, variable, trace);
                trace = "";
                const holder: any = { value: left };
                console.log(holder.value {{op}} right, holder.value, trace);
                trace = "";
                const values: any[] = [left];
                console.log(values[0] {{op}} right, values[0], trace);
                """, string.Concat(Enumerable.Repeat($"{expected} {expected} lr\n", 3))];
        }

        yield return ["original-and-single-evaluation", """
            let trace = "";
            let left: any = { valueOf() { trace += "l"; return 4; } };
            const right: any = { valueOf() { trace += "q"; return 3; } };
            console.log(left += right, trace);
            trace = "";
            const holder: any = { value: { valueOf() { trace += "l"; return 4; } } };
            function receiver(): any { trace += "o"; return holder; }
            function rhs(): any { trace += "r"; return right; }
            console.log(receiver().value += rhs(), holder.value, trace);
            trace = "";
            const values: any[] = [{ valueOf() { trace += "l"; return 4; } }];
            function array(): any[] { trace += "o"; return values; }
            function index(): number { trace += "i"; return 0; }
            console.log(array()[index()] += rhs(), values[0], trace);
            """, "7 lq\n7 7 orlq\n7 7 oirlq\n"];

        yield return ["primitive-coercion", """
            let variable: any = undefined;
            console.log(Number.isNaN(variable += 1), Number.isNaN(variable));
            const holder: any = { value: undefined };
            console.log(Number.isNaN(holder.value *= 2), Number.isNaN(holder.value));
            const values: any[] = [undefined];
            console.log(Number.isNaN(values[0] /= 2), Number.isNaN(values[0]));
            variable = null; console.log(variable += true, variable);
            const stringOperand: any = "2";
            variable = false; console.log(variable -= stringOperand, variable);
            variable = "12"; console.log(variable /= 3, variable);
            variable = 2; console.log(variable += "3", typeof variable);
            variable = undefined; console.log(variable |= 4, variable);
            """, "true true\ntrue true\ntrue true\n1 1\n-2 -2\n4 4\n23 string\n4 4\n"];

        yield return ["addition-default-hint-and-numeric-number-hint", """
            let trace = "";
            let value: any = "prefix:";
            const right: any = {
                valueOf() { trace += "v"; return 7; },
                toString() { trace += "s"; return "wrong"; }
            };
            console.log(value += right, trace);
            trace = "";
            value = { valueOf() { trace += "v"; return {}; }, toString() { trace += "s"; return "text"; } };
            console.log(value += 2, trace);
            trace = "";
            const left: any = { [Symbol.toPrimitive](hint: string) { trace += "l:" + hint + ";"; return 8; } };
            const other: any = { [Symbol.toPrimitive](hint: string) { trace += "r:" + hint + ";"; return 2; } };
            value = left;
            console.log(value += other, trace);
            trace = "";
            value = left;
            console.log(value *= other, trace);
            """, "prefix:7 v\ntext2 vs\n10 l:default;r:default;\n16 l:number;r:number;\n"];

        yield return ["class-ordinary-conversion", """
            let trace = "";
            class Ordinary {
                valueOf() { trace += "v"; return 8; }
                toString() { trace += "s"; return "wrong"; }
            }
            class Fallback {
                valueOf(): any { trace += "v"; return this; }
                toString() { trace += "s"; return "6"; }
            }
            let value: any = new Ordinary();
            console.log(value += 2, trace);
            trace = "";
            value = new Ordinary();
            const amount: any = 2;
            console.log(value *= amount, trace);
            trace = "";
            const holder: any = { value: new Fallback() };
            console.log(holder.value /= 2, holder.value, trace);
            trace = "";
            const values: any[] = [new Ordinary()];
            console.log(values[0] += 3, values[0], trace);
            """, "10 v\n16 v\n3 3 vs\n11 11 v\n"];

        foreach (string target in new[] { "variable", "holder.value", "values[0]" })
        {
            yield return [$"abrupt-{target}", $$"""
                let trace = "";
                const marker: any = {};
                const left: any = { valueOf() { trace += "l"; return 4; } };
                const right: any = { valueOf() { trace += "q"; throw marker; } };
                let variable: any = left;
                const holder: any = { value: left };
                const values: any[] = [left];
                function throwingRhs(): any { trace += "r"; throw marker; }
                try { {{target}} += throwingRhs(); }
                catch (error) { console.log(error === marker, {{target}} === left, trace); }
                trace = "";
                try { {{target}} += (() => { trace += "r"; return right; })(); }
                catch (error) { console.log(error === marker, {{target}} === left, trace); }
                trace = "";
                left.valueOf = () => { trace += "l"; throw marker; };
                try { {{target}} *= (() => { trace += "r"; return right; })(); }
                catch (error) { console.log(error === marker, {{target}} === left, trace); }
                """, "true true r\ntrue true rlq\ntrue true rl\n"];
        }

        yield return ["symbol-failure-conversion-order", """
            let trace = "";
            const symbol: any = Symbol("left");
            const right: any = { valueOf() { trace += "r"; return 2; } };
            let value: any = symbol;
            try { value *= right; }
            catch (error) { console.log(error instanceof TypeError, value === symbol, trace === ""); }
            try { value += right; }
            catch (error) { console.log(error instanceof TypeError, value === symbol, trace); }
            value = "prefix:";
            try { value += symbol; }
            catch (error) { console.log(error instanceof TypeError, value); }
            """, "true true true\ntrue true r\ntrue prefix:\n"];

        yield return ["bigint-numeric-and-string-results", """
            let trace = "";
            let value: any = { valueOf() { trace += "l"; return 8n; } };
            const right: any = { valueOf() { trace += "r"; return 2n; } };
            console.log(String(value += right), String(value), typeof value, trace);
            value += "x";
            console.log(value, typeof value);
            const amount: any = 2n;
            const holder: any = { value: 3n };
            console.log(String(holder.value += amount), String(holder.value), typeof holder.value);
            const values: any[] = [3n];
            console.log(String(values[0] += amount), String(values[0]), typeof values[0]);
            """, "10 10 bigint lr\n10x string\n5 5 bigint\n5 5 bigint\n"];

        yield return ["bigint-mixed-addition-keeps-target", """
            let value: any = 8n;
            try { value += 2; }
            catch (error) { console.log(error instanceof TypeError, value === 8n); }
            """, "true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryCompoundAssignmentsCoerceOperandsBeforeWriteback(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    // Compiled dynamic BigInt compound operators lose the BigInt kind (#2490).
    // Any-typed RHS values avoid the independent typed-BigInt checker rejection (#2491).
    public static IEnumerable<object[]> InterpretedBigIntCases()
    {
        foreach (var (op, expected) in new[]
        {
            ("-=", "6"), ("*=", "16"), ("/=", "4"), ("%=", "0"),
            ("&=", "0"), ("|=", "10"), ("^=", "10"), ("<<=", "32"), (">>=", "2")
        })
        {
            yield return [op, $$"""
                let trace = "";
                let value: any = { valueOf() { trace += "l"; return 8n; } };
                const right: any = { valueOf() { trace += "r"; return 2n; } };
                console.log(String(value {{op}} right), typeof value, trace);
                """, $"{expected} bigint lr\n"];
        }
        yield return ["invalid-operations", """
            let value: any = 8n;
            const shift: any = 2n, zero: any = 0n;
            try { value >>>= shift; }
            catch (error) { console.log(error instanceof TypeError, value === 8n); }
            try { value /= zero; }
            catch (error) { console.log(error instanceof RangeError, value === 8n); }
            """, "true true\ntrue true\n"];
    }

    [Theory, MemberData(nameof(InterpretedBigIntCases))]
    public void InterpreterCompoundNumericCoercionPreservesBigInt(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Compiled class coercion ignores Symbol.toPrimitive for both *= and += (#2494).
    [Fact]
    public void InterpreterClassCompoundCoercionUsesExoticHooks()
    {
        const string source = """
            let trace = "";
            class Exotic {
                [Symbol.toPrimitive](hint: string) { trace += hint; return 2; }
                valueOf() { throw "unexpected"; }
            }
            let value: any = 8;
            const right: any = new Exotic();
            console.log(value *= right, trace);
            trace = "";
            const values: any[] = [new Exotic()];
            console.log(values[0] += 3, values[0], trace);
            """;
        Assert.Equal("16 number\n5 5 default\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
