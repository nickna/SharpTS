using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class PristineParseIntArgumentOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["pristine-direct-parse-int-argument-order-issue", """
            const events: string[] = [];
            const input: any = { toString() { events.push("convert"); return "7"; } };
            function radix(): number { events.push("radix"); return 10; }
            console.log(parseInt(input, radix()), events.join(","));
            """, "7 radix,convert\n"];
        yield return ["expressions-before-input-and-radix-coercions-with-pristine-alias-controls", """
            const events: string[] = [];
            function input(): any {
                events.push('input-expression');
                return { toString() { events.push('input-convert'); return '21tail'; } };
            }
            function radix(): any {
                events.push('radix-expression');
                return { valueOf() { events.push('radix-convert'); return 10; } };
            }
            console.log('direct', parseInt(input(), radix()), events.join(','));
            events.length = 0;
            const held = parseInt;
            console.log('held', held(input(), radix()), events.join(','));
            events.length = 0;
            console.log('qualified', globalThis.parseInt(input(), radix()), events.join(','));
            """, "direct 21 input-expression,radix-expression,input-convert,radix-convert\nheld 21 input-expression,radix-expression,input-convert,radix-convert\nqualified 21 input-expression,radix-expression,input-convert,radix-convert\n"];
        yield return ["radix-expression-selects-the-later-input-coercer", """
            const events: string[] = [];
            const input: any = { toString() { events.push('old-convert'); return '7'; } };
            function radix(): number {
                events.push('radix');
                input.toString = () => { events.push('new-convert'); return '12'; };
                return 10;
            }
            console.log(parseInt(input, radix()), events.join(','));
            """, "12 radix,new-convert\n"];
        yield return ["throwing-radix-expression-suppresses-input-conversion", """
            const events: string[] = [], marker = {};
            const input: any = { toString() { events.push('convert'); return '7'; } };
            function radix(): number { events.push('radix'); throw marker; }
            try { console.log('unexpected', parseInt(input, radix())); }
            catch (error) { console.log(error === marker, events.join(',')); }
            """, "true radix\n"];
        yield return ["throwing-input-conversion-follows-radix-expression-and-skips-radix-coercion", """
            const events: string[] = [], marker = {};
            const input: any = { toString() { events.push('input-convert'); throw marker; } };
            function radix(): any {
                events.push('radix-expression');
                return { valueOf() { events.push('radix-convert'); return 10; } };
            }
            try { console.log('unexpected', parseInt(input, radix())); }
            catch (error) { console.log(error === marker, events.join(',')); }
            """, "true radix-expression,input-convert\n"];
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
    public void ArgumentsFinishEvaluationBeforePristineParseIntConvertsThem(
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
