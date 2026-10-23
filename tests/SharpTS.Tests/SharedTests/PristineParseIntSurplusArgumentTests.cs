using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PristineParseIntSurplusArgumentTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["pristine-parse-int-surplus-argument-issue", """
            declare function parseInt(value: any, radix: number, extra: any): number;
            const events: string[] = [];
            function extra(): number { events.push("extra"); return 4; }
            console.log(parseInt("7", 10, extra()), events.join(","));
            """, "7 extra\n"];
        yield return ["all-arguments-before-coercion-and-ignored-objects-with-alias-controls", """
            declare function parseInt(value: any, radix: any, ...extras: any[]): number;
            const events: string[] = [];
            const input: any = { toString() { events.push('input-convert'); return '7'; } };
            const radix: any = { valueOf() { events.push('radix-convert'); return 10; } };
            const ignored: any = {
                toString() { events.push('unexpected-string'); throw new Error('ignored'); },
                valueOf() { events.push('unexpected-number'); throw new Error('ignored'); }
            };
            function first(): any { events.push('input-expression'); return input; }
            function second(): any { events.push('radix-expression'); return radix; }
            function extra(name: string): any { events.push(name); return ignored; }
            console.log('direct', parseInt(first(), second(), extra('third'), extra('fourth')), events.join(','));
            events.length = 0;
            const held = parseInt;
            console.log('held', held(first(), second(), extra('third'), extra('fourth')), events.join(','));
            events.length = 0;
            console.log('qualified', globalThis.parseInt(first(), second(), extra('third'), extra('fourth')), events.join(','));
            """, "direct 7 input-expression,radix-expression,third,fourth,input-convert,radix-convert\nheld 7 input-expression,radix-expression,third,fourth,input-convert,radix-convert\nqualified 7 input-expression,radix-expression,third,fourth,input-convert,radix-convert\n"];
        yield return ["throwing-extra-suppresses-conversions-and-later-extras", """
            declare function parseInt(value: any, radix: any, ...extras: any[]): number;
            const events: string[] = [], marker = {};
            const input: any = { toString() { events.push('input-convert'); return '7'; } };
            const radix: any = { valueOf() { events.push('radix-convert'); return 10; } };
            function third(): any { events.push('third'); throw marker; }
            function fourth(): number { events.push('fourth'); return 4; }
            try { console.log('unexpected', parseInt(input, radix, third(), fourth())); }
            catch (error) { console.log(error === marker, events.join(',')); }
            """, "true third\n"];
        yield return ["early-input-and-radix-expression-errors-suppress-extras", """
            declare function parseInt(value: any, radix: any, ...extras: any[]): number;
            const events: string[] = [], marker = {};
            const input: any = { toString() { events.push('input-convert'); return '7'; } };
            const radix: any = { valueOf() { events.push('radix-convert'); return 10; } };
            function first(): any { events.push('input-expression'); return input; }
            function second(): any { events.push('radix-expression'); return radix; }
            function badInput(): any { events.push('input-throw'); throw marker; }
            function badRadix(): any { events.push('radix-throw'); throw marker; }
            function extra(): number { events.push('extra'); return 4; }
            try { console.log('unexpected', parseInt(badInput(), second(), extra())); }
            catch (error) { console.log(error === marker, events.join(',')); }
            events.length = 0;
            try { console.log('unexpected', parseInt(first(), badRadix(), extra())); }
            catch (error) { console.log(error === marker, events.join(',')); }
            """, "true input-throw\ntrue input-expression,radix-throw\n"];
        yield return ["pending-extra-preserves-earlier-values-and-delays-conversions", """
            declare function parseInt(value: any, radix: any, ...extras: any[]): number;
            const events: string[] = [];
            const input: any = { toString() { events.push('input-convert'); return '7'; } };
            const radix: any = { valueOf() { events.push('radix-convert'); return 10; } };
            let selectedInput: any = input, selectedRadix: any = radix;
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function extra() { events.push('extra'); return pending; }
                function last() { events.push('last'); return 5; }
                const task = (async () => parseInt(selectedInput, selectedRadix, await extra(), last()))();
                console.log('pending', events.join(','));
                selectedInput = '99'; selectedRadix = 2; release(4);
                console.log('settled', await task, events.join(','));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """, "pending extra\nsettled 7 extra,last,input-convert,radix-convert\n"];
        yield return ["pending-extra-with-numeric-radix-preserves-snapshots", """
            declare function parseInt(value: any, radix: any, ...extras: any[]): number;
            const events: string[] = [];
            const input: any = { toString() { events.push('input-convert'); return '7'; } };
            const radix: any = 10;
            let selectedInput: any = input, selectedRadix: any = radix;
            async function run() {
                let release: (value: number) => void = () => {};
                const pending = new Promise<number>(resolve => { release = resolve; });
                function extra() { events.push('extra'); return pending; }
                function last() { events.push('last'); return 5; }
                const task = (async () => parseInt(selectedInput, selectedRadix, await extra(), last()))();
                console.log('pending', events.join(','));
                selectedInput = '99'; selectedRadix = 2; release(4);
                console.log('settled', await task, events.join(','));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """, "pending extra\nsettled 7 extra,last,input-convert\n"];
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
    public void PristineParseIntEvaluatesSurplusArgumentsBeforeConvertingInputs(
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
