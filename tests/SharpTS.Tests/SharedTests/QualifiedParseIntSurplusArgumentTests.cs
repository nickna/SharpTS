using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class QualifiedParseIntSurplusArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        var original0 = PristineParseIntSurplusArgumentTests.Cases().Single(row =>
            (string)row[0] == "all-arguments-before-coercion-and-ignored-objects-with-alias-controls");
        // Retain the old complete CRLF payload; only the historical EOF differs.
        yield return ["all-arguments-before-coercion-and-ignored-objects-with-alias-controls", (string)original0[1] + "\r\n", original0[2]];
        var original1 = PristineParseIntSurplusArgumentTests.Cases().Single(row =>
            (string)row[0] == "pending-extra-preserves-earlier-values-and-delays-conversions");
        // Retain the old complete CRLF payload; only the historical EOF differs.
        yield return ["pending-extra-preserves-earlier-values-and-delays-conversions", (string)original1[1] + "\r\n", original1[2]];
        yield return ["public-qualified-parse-int-surplus", """
            declare function parseInt(value: any, radix: number, extra: any): number;
            const events: string[] = [];
            function extra(): number { events.push("extra"); return 4; }
            console.log(globalThis.parseInt("7", 10, extra()), events.join(","));
            """ + "\n", "7 extra\n"];
        yield return ["qualified-extras-order-abrupt-and-snapshots", """
            declare function parseInt(value: any, radix: any, ...extras: any[]): number;
            const events: string[] = [], marker = {};
            const input: any = { toString() { events.push('input-convert'); return '7'; } };
            const radix: any = { valueOf() { events.push('radix-convert'); return 10; } };
            const ignored: any = { toString() { throw marker; }, valueOf() { throw marker; } };
            function first(): any { events.push('first'); return input; }
            function second(): any { events.push('second'); return radix; }
            function third(): any { events.push('third'); return ignored; }
            function fourth(): any { events.push('fourth'); return ignored; }
            console.log('order', globalThis.parseInt(first(), second(), third(), fourth()), events.join(','));
            events.length = 0;
            function boom(): any { events.push('boom'); throw marker; }
            try { globalThis.parseInt(input, radix, boom(), fourth()); }
            catch (error) { console.log('throw', error === marker, events.join(',')); }
            events.length = 0;
            let selectedInput: any = input, selectedRadix: any = radix;
            function change(): any { events.push('change'); selectedInput = '99'; selectedRadix = 2; return ignored; }
            console.log('snapshot', globalThis.parseInt(selectedInput, selectedRadix, change()), events.join(','));
            events.length = 0;
            function noValue(): void { events.push('void'); }
            console.log('void', globalThis.parseInt('7', 10, noValue()), events.join(','));
            events.length = 0;
            selectedInput = input; selectedRadix = radix;
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function extra() { events.push('extra'); return pending; }
                const task = (async () => globalThis.parseInt(selectedInput, selectedRadix, await extra(), fourth()))();
                console.log('pending', events.join(','));
                selectedInput = '99'; selectedRadix = 2; release(4);
                console.log('settled', await task, events.join(','));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """ + "\n", "order 7 first,second,third,fourth,input-convert,radix-convert\nthrow true boom\nsnapshot 7 change,input-convert,radix-convert\nvoid 7 void\npending extra\nsettled 7 extra,fourth,input-convert,radix-convert\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void QualifiedParseIntEvaluatesAllArgumentsBeforeConversion(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
