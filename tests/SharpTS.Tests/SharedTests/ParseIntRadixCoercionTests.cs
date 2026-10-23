using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ParseIntRadixCoercionTests
{
    public static IEnumerable<object[]> Cases()
    {
        // The existing provider retains the exact historical CRLF program.
        yield return CompactRecordReturnEligibilityTests.Cases().Single(row =>
            (string)row[0] == "expressions-before-input-and-radix-coercions-with-pristine-alias-controls");
        yield return ["public-supplied-string-radix", """
            const radix: any = "10";
            console.log(parseInt("0x10", radix), Number.parseInt("0x10", radix));
            """ + "\n", "0 0\n"];
        yield return ["radix-coercion-int32-order-and-abrupt", """
            function values(label: string, radix: any) {
                console.log(label, parseInt('0x10', radix), Number.parseInt('0x10', radix), globalThis.parseInt('0x10', radix));
            }
            values('string-ten', '10');
            values('string-sixteen', '16');
            values('true', true);
            values('false', false);
            values('null', null);
            values('undefined', undefined);
            values('fraction', 16.9);
            values('wrap', 4294967306);
            values('negative-wrap', -4294967280);
            values('infinity', Infinity);
            values('nan', NaN);
            values('array', [16]);
            const events: string[] = [], marker = {};
            const input: any = { toString() { events.push('input'); return '0x10'; } };
            const radix: any = { valueOf() { events.push('radix'); return 16; } };
            console.log('order', parseInt(input, radix), events.join(','));
            events.length = 0;
            const empty: any = { toString() { events.push('empty'); return ''; } };
            console.log('empty', Number.isNaN(parseInt(empty, radix)), events.join(','));
            events.length = 0;
            const inputThrow: any = { toString() { events.push('input'); throw marker; } };
            try { parseInt(inputThrow, radix); }
            catch (error) { console.log('input-throw', error === marker, events.join(',')); }
            events.length = 0;
            const radixThrow: any = { valueOf() { events.push('radix'); throw marker; } };
            try { Number.parseInt(input, radixThrow); }
            catch (error) { console.log('radix-throw', error === marker, events.join(',')); }
            events.length = 0;
            try { globalThis.parseInt(empty, radixThrow); }
            catch (error) { console.log('empty-throw', error === marker, events.join(',')); }
            events.length = 0;
            const fallback: any = {
                valueOf() { events.push('valueOf'); return {}; },
                toString() { events.push('toString'); return '16'; }
            };
            console.log('fallback', parseInt(input, fallback), events.join(','));
            for (const bad of [Symbol('radix'), 16n]) {
                try { parseInt('10', bad as any); }
                catch (error) { console.log('bad-radix', error instanceof TypeError); }
            }
            """ + "\n", "string-ten 0 0 0\nstring-sixteen 16 16 16\ntrue NaN NaN NaN\nfalse 16 16 16\nnull 16 16 16\nundefined 16 16 16\nfraction 16 16 16\nwrap 0 0 0\nnegative-wrap 16 16 16\ninfinity 16 16 16\nnan 16 16 16\narray 16 16 16\norder 16 input,radix\nempty true empty,radix\ninput-throw true input\nradix-throw true input,radix\nempty-throw true empty,radix\nfallback 16 input,valueOf,toString\nbad-radix true\nbad-radix true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SuppliedRadixUsesInt32CoercionAfterInputStringConversion(
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
