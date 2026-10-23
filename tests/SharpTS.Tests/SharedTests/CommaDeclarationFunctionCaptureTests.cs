using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CommaDeclarationFunctionCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["comma-loop-block-function-capture", """
            for (let i = 0; i < 1; i++) {
                const unused = 0, events: string[] = [];
                function selected(): number {
                    events.push('selected');
                    return events.length;
                }
                console.log(selected(), events.join(','));
            }
            """ + "\n", "1 selected\n"];
        yield return ["comma-declaration-capture-and-scope-controls", """
            for (let i = 0; i < 1; i++) {
                const left = 2, right = 3;
                function readConst(): number { return left + right; }
                console.log('const', readConst());
                let first = 4, second = 5;
                function readLet(): number { return first + second; }
                console.log('let', readLet());
            }
            for (let turn = 0, header = 7; turn < 1; turn++) {
                function readHeader(): number { return header; }
                console.log('header', readHeader());
            }
            for (let i = 0; i < 1; i++) {
                const events: string[] = [];
                function selected(): number {
                    events.push('selected');
                    return events.length;
                }
                console.log(selected(), events.join(','));
            }
            { const base = 5; function add(n: number) { return n + base; } console.log(add(1)); }
            {
                const outer = 11;
                {
                    const inner = 13;
                    function readInner(): number { return inner; }
                    console.log('nested', readInner());
                }
                function readOuter(): number { return outer; }
                console.log('outer', readOuter());
            }
            { function plain(): number { return 23; } console.log('plain', plain()); }
            """ + "\n", "const 5\nlet 9\nheader 7\n1 selected\n6\nnested 13\nouter 11\nplain 23\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LiftedFunctionsRetainTransparentDeclarationBindings(
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
