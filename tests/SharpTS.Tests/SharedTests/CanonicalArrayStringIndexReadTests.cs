using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CanonicalArrayStringIndexReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["literal-zero-read", """
            const values: any[] = [4];
            console.log(values['0']);
            """ + "\n", "4\n"];
        yield return ["any-and-numeric-control", """
            const values: any[] = [4];
            const key: any = '0';
            console.log(values[key], values[0]);
            """ + "\n", "4 4\n"];
        yield return ["typed-readonly-union-canonical-reads", """
            const numbers: number[] = [4, 8];
            const first: number = numbers['0'];
            const words: readonly string[] = ['left', 'right'];
            const word: string = words['0'];
            function readUnion(values: number[] | string[]): number | string {
                return values['0'];
            }
            function readKey(values: number[], key: '0' | '1'): number {
                return values[key];
            }
            console.log(first, word, readUnion([2, 6]), readUnion(['a', 'b']),
                readKey(numbers, '0'), readKey(numbers, '1'));
            """ + "\n", "4 left 2 a 4 8\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CanonicalStringArrayReadsPreserveElementTypesAndValues(
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
