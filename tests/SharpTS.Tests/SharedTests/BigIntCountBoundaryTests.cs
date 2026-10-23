using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class BigIntCountBoundaryTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["primitive-count-boundary", """
            function take(value: bigint): bigint { return value; }
            try { console.log('right-positive', String(take(8n) >> take(2147483648n))); }
            catch (error) { console.log('right-positive', error instanceof RangeError); }
            try { console.log('right-negative', String(take(-8n) >> take(2147483648n))); }
            catch (error) { console.log('right-negative', error instanceof RangeError); }
            try { console.log('left-negative-count', String(take(8n) << take(-2147483648n))); }
            catch (error) { console.log('left-negative-count', error instanceof RangeError); }
            try { console.log('power-one', String(take(1n) ** take(2147483648n))); }
            catch (error) { console.log('power-one', error instanceof RangeError); }
            try { console.log('power-zero', String(take(0n) ** take(2147483648n))); }
            catch (error) { console.log('power-zero', error instanceof RangeError); }
            try { console.log('power-two', String(take(2n) ** take(2147483648n))); }
            catch (error) { console.log('power-two', error instanceof RangeError); }
            """ + "\r\n", "right-positive 0\nright-negative -1\nleft-negative-count 0\npower-one 1\npower-zero 0\npower-two true\n"];
        yield return ["bounded-primitive-count-controls", """
            function take(value: bigint): bigint { return value; }
            console.log('ordinary-shifts', String(take(8n) << take(2n)), String(take(8n) >> take(2n)),
                String(take(8n) << take(-2n)), String(take(8n) >> take(-2n)), String(take(-8n) >> take(2n)));
            console.log('ordinary-powers', String(take(0n) ** take(0n)), String(take(2n) ** take(3n)));
            try { console.log('left-negative-count-negative-value', String(take(-8n) << take(-2147483648n))); }
            catch (error) { console.log('left-negative-count-negative-value', error instanceof RangeError); }
            try { console.log('zero-left-huge', String(take(0n) << take(2147483648n))); }
            catch (error) { console.log('zero-left-huge', error instanceof RangeError); }
            try { console.log('zero-right-huge', String(take(0n) >> take(2147483648n))); }
            catch (error) { console.log('zero-right-huge', error instanceof RangeError); }
            try { console.log('zero-right-negative-count', String(take(0n) >> take(-2147483648n))); }
            catch (error) { console.log('zero-right-negative-count', error instanceof RangeError); }
            try { console.log('minus-one-even-power', String(take(-1n) ** take(2147483648n))); }
            catch (error) { console.log('minus-one-even-power', error instanceof RangeError); }
            try { console.log('minus-one-odd-power', String(take(-1n) ** take(2147483649n))); }
            catch (error) { console.log('minus-one-odd-power', error instanceof RangeError); }
            try { console.log('negative-power', String(take(1n) ** take(-1n))); }
            catch (error) { console.log('negative-power', error instanceof RangeError); }
            try { console.log('left-materializing-count', String(take(1n) << take(2147483648n))); }
            catch (error) { console.log('left-materializing-count', error instanceof RangeError); }
            try { console.log('right-negative-materializing-count', String(take(1n) >> take(-2147483649n))); }
            catch (error) { console.log('right-negative-materializing-count', error instanceof RangeError); }
            """ + "\n", "ordinary-shifts 32 2 2 32 -2\nordinary-powers 1 8\nleft-negative-count-negative-value -1\nzero-left-huge 0\nzero-right-huge 0\nzero-right-negative-count 0\nminus-one-even-power 1\nminus-one-odd-power -1\nnegative-power true\nleft-materializing-count true\nright-negative-materializing-count true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void BigIntCountsPreserveFiniteResultsAndGuestRangeErrors(
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
