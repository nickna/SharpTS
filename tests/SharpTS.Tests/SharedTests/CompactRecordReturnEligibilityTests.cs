using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CompactRecordReturnEligibilityTests
{
    public static IEnumerable<object[]> Cases()
    {
        var original0 = PristineParseIntArgumentOrderTests.Cases().Single(row =>
            (string)row[0] == "expressions-before-input-and-radix-coercions-with-pristine-alias-controls");
        // The historical program uses CRLF; retain the existing LF provider payload.
        yield return ["expressions-before-input-and-radix-coercions-with-pristine-alias-controls", ((string)original0[1]).Replace("\n", "\r\n") + "\r\n", original0[2]];
        yield return ["literal-eligibility-and-ordinary-controls", """
            function method(): any { return { read() { return 7; } }; }
            function data(): any { return { value: 7 }; }
            const inline: any = { read() { return 7; } };
            console.log('method', method().read());
            console.log('data', data().value);
            console.log('inline', inline.read());
            function json(): any { return { toJSON() { return 9; } }; }
            console.log('json', json().toJSON());
            """ + "\n", "method 7\ndata 7\ninline 7\njson 9\n"];
        yield return ["public-to-string-return-any", """
            function make(): any {
                return { toString() { return '7'; } };
            }
            const value: any = make();
            console.log(value.toString());
            """ + "\n", "7\n"];
        var original3 = PristineParseIntArgumentOrderTests.Cases().Single(row =>
            (string)row[0] == "throwing-input-conversion-follows-radix-expression-and-skips-radix-coercion");
        // The historical program uses CRLF; retain the existing LF provider payload.
        yield return ["throwing-input-conversion-follows-radix-expression-and-skips-radix-coercion", ((string)original3[1]).Replace("\n", "\r\n") + "\r\n", original3[2]];
        yield return ["value-of-return-any", """
            function make(): any {
                return { valueOf() { return 7; } };
            }
            const value: any = make();
            console.log(value.valueOf());
            """ + "\n", "7\n"];
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
    public void ReturnSignaturesMatchActualLiteralRepresentation(
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
