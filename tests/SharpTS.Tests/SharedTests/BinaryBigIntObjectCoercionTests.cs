using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BinaryBigIntObjectCoercionTests
{
    public const string StateMachineSource = """
        const events: string[] = [], marker = {};
        const left: any = { valueOf() { events.push('left'); return 8n; } };
        const right: any = { valueOf() { events.push('right'); return 2n; } };
        const throwing: any = { valueOf() { events.push('throw'); throw marker; } };
        function lhs(): any { events.push('lhs'); return left; }
        function rhs(): any { events.push('rhs'); return right; }
        function failingRhs(): any { events.push('rhs'); return throwing; }
        function checkpoint(): string { events.push('yield'); return 'ready'; }
        function* bits(): Generator<any, any, any> { return lhs() & (yield checkpoint()); }
        async function run() {
            console.log(String(lhs() * await Promise.resolve(rhs())), events.join(','));
            events.length = 0;
            try { console.log(lhs() * await Promise.resolve(failingRhs())); }
            catch (error) { console.log(error === marker, events.join(',')); }
            events.length = 0;
            const iterator = bits();
            console.log(iterator.next().value, events.join(','));
            console.log(String(iterator.next(right).value), events.join(','));
            events.length = 0;
            const failing = bits();
            failing.next();
            try { failing.next(throwing); }
            catch (error) { console.log(error === marker, events.join(',')); }
        }
        run().catch((error: any) => console.log('unexpected', error.name, error.message));
        """;

    public const string LargeCountSource = """
        let trace = '', base = 8n, count = 2147483648n;
        const left: any = { valueOf() { trace += 'l'; return base; } };
        const right: any = { valueOf() { trace += 'r'; return count; } };
        function result(value: any) { console.log(String(value), trace); trace = ''; }
        result(left >> right);
        base = -8n; result(left >> right);
        base = 8n; count = -2147483648n; result(left << right);
        base = -8n; result(left << right);
        base = 0n; count = 2147483648n; result(left ** right);
        base = 1n; result(left ** right);
        base = -1n; result(left ** right);
        count = 2147483649n; result(left ** right);
        base = 8n; count = 2147483648n;
        try { result(left << right); }
        catch (error) { console.log(error instanceof RangeError, trace); trace = ''; }
        count = -2147483648n;
        try { result(left >> right); }
        catch (error) { console.log(error instanceof RangeError, trace); trace = ''; }
        base = 2n; count = 2147483648n;
        try { result(left ** right); }
        catch (error) { console.log(error instanceof RangeError, trace); }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            let trace = "";
            const operand: any = { valueOf() { trace += "v"; return 4n; } };
            try { console.log(operand * 3n, trace); }
            catch (error) { console.log(error instanceof TypeError, trace); }
            """, "12n v\n"];
        yield return ["operand-orders-and-two-objects", """
            let trace = "";
            const left: any = { valueOf() { trace += "l"; return 4n; } };
            const right: any = { valueOf() { trace += "r"; return 3n; } };
            console.log(String(left * 3n), trace);
            trace = "";
            console.log(String(4n * right), trace);
            trace = "";
            console.log(String(left * right), trace);
            """, "12 l\n12 r\n12 lr\n"];
        yield return ["arithmetic-power-bitwise-and-shifts", """
            let trace = "";
            const left: any = { valueOf() { trace += "l"; return 8n; } };
            const right: any = { valueOf() { trace += "r"; return 2n; } };
            trace = ""; console.log(String(left - right), trace);
            trace = ""; console.log(String(left * right), trace);
            trace = ""; console.log(String(left / right), trace);
            trace = ""; console.log(String(left % right), trace);
            trace = ""; console.log(String(left ** right), trace);
            trace = ""; console.log(String(left & right), trace);
            trace = ""; console.log(String(left | right), trace);
            trace = ""; console.log(String(left ^ right), trace);
            trace = ""; console.log(String(left << right), trace);
            trace = ""; console.log(String(left >> right), trace);
            """, "6 lr\n16 lr\n4 lr\n0 lr\n64 lr\n0 lr\n10 lr\n10 lr\n32 lr\n2 lr\n"];
        yield return ["addition-object-bigint-and-default-hint", """
            let trace = "";
            const left: any = { valueOf() { trace += "l"; return 4n; } };
            const right: any = { valueOf() { trace += "r"; return 3n; } };
            console.log(String(left + right), trace);
            trace = "";
            console.log(String(4n + right), trace);
            trace = "";
            console.log(String(left + 3n), trace);
            trace = "";
            const text: any = { [Symbol.toPrimitive](hint: string) { trace += hint; return 4n; } };
            console.log("x" + text, trace);
            """, "7 lr\n7 r\n7 l\nx4 default\n"];
        yield return ["mixed-number-bigint-converts-before-guest-error", """
            let trace = "";
            const bigLeft: any = { valueOf() { trace += "l"; return 4n; } };
            const numberRight: any = { valueOf() { trace += "r"; return 3; } };
            const numberLeft: any = { valueOf() { trace += "l"; return 4; } };
            const bigRight: any = { valueOf() { trace += "r"; return 3n; } };
            try { console.log(bigLeft * numberRight); }
            catch (error) { console.log(error instanceof TypeError, trace); }
            trace = "";
            try { console.log(numberLeft ** bigRight); }
            catch (error) { console.log(error instanceof TypeError, trace); }
            trace = "";
            try { console.log(4n & numberRight); }
            catch (error) { console.log(error instanceof TypeError, trace); }
            trace = "";
            try { console.log(numberLeft << 3n); }
            catch (error) { console.log(error instanceof TypeError, trace); }
            """, "true lr\ntrue lr\ntrue r\ntrue l\n"];
        yield return ["abrupt-hooks-preserve-identity-and-conversion-order", """
            const events: string[] = [];
            const token = {};
            let failLeft = true;
            const left: any = { valueOf() { events.push("left"); if (failLeft) throw token; return 4n; } };
            const right: any = { valueOf() { events.push("right"); throw token; } };
            function readLeft(): any { events.push("left-expression"); return left; }
            function readRight(): any { events.push("right-expression"); return right; }
            try { console.log(readLeft() * readRight()); }
            catch (error) { console.log(error === token, events.join(",")); }
            events.length = 0; failLeft = false;
            try { console.log(readLeft() * readRight()); }
            catch (error) { console.log(error === token, events.join(",")); }
            """, "true left-expression,right-expression,left\ntrue left-expression,right-expression,left,right\n"];
        yield return ["numeric-hint-and-ordinary-fallback", """
            let trace = "";
            const exotic: any = { [Symbol.toPrimitive](hint: string) { trace += "e:" + hint + ";"; return 4n; }, valueOf() { throw "wrong"; } };
            const right: any = { valueOf() { trace += "r"; return 3n; } };
            console.log(String(exotic * right), trace);
            trace = "";
            const fallback: any = { valueOf() { trace += "v"; return {}; }, toString(): any { trace += "s"; return 4n; } };
            console.log(String(fallback * right), trace);
            """, "12 e:number;r\n12 vsr\n"];
        yield return ["invalid-bigint-operations-convert-both-first", """
            let trace = "";
            const left: any = { valueOf() { trace += "l"; return 8n; } };
            const zero: any = { valueOf() { trace += "r"; return 0n; } };
            const negative: any = { valueOf() { trace += "r"; return -1n; } };
            const shift: any = { valueOf() { trace += "r"; return 2n; } };
            try { console.log(left / zero); }
            catch (error) { console.log(error instanceof RangeError, trace); }
            trace = "";
            try { console.log(left ** negative); }
            catch (error) { console.log(error instanceof RangeError, trace); }
            trace = "";
            try { console.log(left >>> shift); }
            catch (error) { console.log(error instanceof TypeError, trace); }
            """, "true lr\ntrue lr\ntrue lr\n"];
        yield return ["number-addition-comparison-and-unary-controls", """
            let trace = "";
            const numberLeft: any = { valueOf() { trace += "l"; return 4; } };
            const numberRight: any = { valueOf() { trace += "r"; return 3; } };
            console.log(numberLeft * numberRight, trace);
            trace = "";
            const big: any = { valueOf() { trace += "b"; return 4n; } };
            console.log(big + "x", trace);
            console.log(String(4n + 3n));
            const ten: any = 10, eight: any = 8, nine: any = 9;
            console.log(9n < ten, 9n > eight, 9n == nine, 9n === 9n);
            console.log(String(-4n), String(~4n));
            """, "12 lr\n4x b\n7\ntrue true true true\n-4 -5\n"];
        yield return ["native-date-addition-control", """
            const date: any = new Date(0), one: any = 1;
            const result = date + one;
            console.log(typeof result, result.endsWith("1"));
            """, "string true\n"];
    }

    public static IEnumerable<object[]> OperatorCases()
    {
        foreach (var (name, op, expected) in new[]
        {
            ("subtraction", "-", "6"), ("multiplication", "*", "16"), ("division", "/", "4"),
            ("remainder", "%", "0"), ("power", "**", "64"), ("and", "&", "0"),
            ("or", "|", "10"), ("xor", "^", "10"), ("left-shift", "<<", "32"), ("right-shift", ">>", "2")
        })
        {
            yield return [$"independent-{name}-converts-both-operands-once", $$"""
                let trace = '';
                const left: any = { valueOf() { trace += 'l'; return 8n; } };
                const right: any = { valueOf() { trace += 'r'; return 2n; } };
                function lhs(): any { trace += 'L'; return left; }
                function rhs(): any { trace += 'R'; return right; }
                console.log(String(lhs() {{op}} rhs()), trace);
                """, $"{expected} LRlr\n"];
        }
        yield return ["await-and-yield-rhs-retain-operands-conversion-order-and-abrupt-marker", StateMachineSource,
            "16 lhs,rhs,left,right\ntrue lhs,rhs,left,throw\nready lhs,yield\n0 lhs,yield,left,right\ntrue lhs,yield,left,throw\n"];
        yield return ["converted-object-bigint-counts-preserve-finite-results-and-range-errors", LargeCountSource,
            "0 lr\n-1 lr\n0 lr\n-1 lr\n0 lr\n1 lr\n1 lr\n-1 lr\ntrue lr\ntrue lr\ntrue lr\n"];
    }

    public static IEnumerable<object[]> AllCases() => Cases().Concat(OperatorCases());

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in AllCases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void NumericBinaryOperatorsConvertBothOperandsBeforeSelectingTheNumericKind(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
