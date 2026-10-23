using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class IntrinsicStaticLexicalBindingTests
{
    public const string CapturedIssueSource = """
        function outer() {
            const Math = { max(left: number, right: number) { return 17; } };
            function read() { return Math.max(1, 2); }
            return read();
        }
        console.log(outer());
        """;

    public const string AsyncParameterIssueSource = """
        async function forward(Number: any, Math: any) {
            await Promise.resolve(0);
            console.log(Number.parseInt("7", 10), Math.min(7, 8));
        }
        forward({ parseInt: () => 17 }, { min: () => 21 })
            .catch((error: any) => console.log("unexpected", error && error.name));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["captured-sync-math-shadow-issue", CapturedIssueSource, "17\n"];
        yield return ["async-number-and-math-parameters-shadow-intrinsics-issue", AsyncParameterIssueSource, "17 21\n"];
        yield return ["async-parameter-receivers-local-aliases-and-unshadowed-globals", """
            const numberGlobal: any = Number, mathGlobal: any = Math;
            async function forward(Number: any, Math: any) {
                await Promise.resolve(0);
                const numberAlias = Number, mathAlias = Math;
                console.log(Number.parseInt('7', 10), Math.max(1, 2),
                    numberAlias.parseInt('8', 10), mathAlias.max(1, 3));
            }
            forward({
                tag: 'number',
                parseInt(value: string, radix: number) { return this.tag + value; }
            }, {
                tag: 'math',
                max(left: number, right: number) { return this.tag + right; }
            }).then(() => {
                console.log(Number.parseInt('12', 10), Math.max(1, 2),
                    numberGlobal.parseInt('13', 10), mathGlobal.min(3, 4));
            }).catch((error: any) => console.log('unexpected', error && error.name));
            """, "number7 math2 number8 math3\n12 2 13 3\n"];
        yield return ["literal-bracket-calls-respect-captured-and-async-parameter-receivers", """
            function outer() {
                const Number = {
                    tag: 'number',
                    parseInt(value: string, radix: number) { return this.tag + value; }
                };
                const Math = {
                    tag: 'math',
                    max(left: number, right: number) { return this.tag + right; }
                };
                function read() { return Number['parseInt']('7', 10) + ':' + Math['max'](1, 2); }
                console.log(read());
                const numberAlias = Number, mathAlias = Math;
                console.log(numberAlias['parseInt']('8', 10), mathAlias['max'](1, 3));
            }
            outer();
            async function forward(Number: any, Math: any) {
                await Promise.resolve(0);
                console.log(Number['parseInt']('9', 10), Math['max'](1, 4));
            }
            forward({
                tag: 'parameter',
                parseInt(value: string, radix: number) { return this.tag + value; }
            }, {
                tag: 'async',
                max(left: number, right: number) { return this.tag + right; }
            }).catch((error: any) => console.log('unexpected', error && error.name));
            """, "number7:math2\nnumber8 math3\nparameter9 async4\n"];
    }

    public static IEnumerable<object[]> SavedCases()
    {
        foreach (var row in Cases()) yield return row;
        foreach (var row in IntrinsicStaticCallBindingTests.Cases().Where(row =>
            (string)row[0] is "lexical-block-capture-and-object-alias" or "async-shadow-and-captured-alias"))
            yield return row;
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CapturedAndAsyncBindingsOverrideIntrinsicStaticCallSelection(
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
