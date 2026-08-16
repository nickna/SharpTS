using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the comma (sequence) operator. Runs against both interpreter and compiler.
/// </summary>
public class CommaOperatorTests
{
    [Theory, ModeData]
    public void Comma_ReturnsLastValue(ExecutionMode mode)
    {
        var source = """
            let x: any = (1, 2, 3);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Comma_EvaluatesAllExpressions(ExecutionMode mode)
    {
        var source = """
            let a: number = 0;
            let b: number = 0;
            let c: number = (a = 1, b = 2, a + b);
            console.log(a);
            console.log(b);
            console.log(c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Comma_SideEffectsExecuteLeftToRight(ExecutionMode mode)
    {
        var source = """
            let log: string = "";
            function append(s: string): string {
                log = log + s;
                return s;
            }
            let result: any = (append("a"), append("b"), append("c"));
            console.log(log);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("abc\nc\n", output);
    }

    [Theory, ModeData]
    public void Comma_InForLoopIncrement(ExecutionMode mode)
    {
        var source = """
            let results: string[] = [];
            let j: number = 10;
            for (let i = 0; i < 3; i++, j--) {
                results.push(i + ":" + j);
            }
            console.log(results.join(" "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:10 1:9 2:8\n", output);
    }

    [Theory, ModeData]
    public void Comma_InForLoopInitializer(ExecutionMode mode)
    {
        var source = """
            let i: number = 0;
            let j: number = 0;
            for (i = 0, j = 10; i < 3; i++) {
                // just loop
            }
            console.log(i);
            console.log(j);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n10\n", output);
    }

    [Theory, ModeData]
    public void Comma_InExpressionStatement(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            let y: number = 0;
            x = 5, y = 10;
            console.log(x);
            console.log(y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n10\n", output);
    }

    [Theory, ModeData]
    public void Comma_TwoOperands(ExecutionMode mode)
    {
        var source = """
            let x: any = (1, 2);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void Comma_WithDifferentTypes(ExecutionMode mode)
    {
        var source = """
            let x: any = ("hello", 42, true);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Comma_DoesNotAffectFunctionArguments(ExecutionMode mode)
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            console.log(sum(1, 2, 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void Comma_DoesNotAffectArrayLiterals(ExecutionMode mode)
    {
        var source = """
            let arr = [1, 2, 3];
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory, ModeData]
    public void Comma_NestedInParens(ExecutionMode mode)
    {
        var source = """
            let x: any = ((1, 2), (3, 4));
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory, ModeData]
    public void Comma_WithFunctionCalls(ExecutionMode mode)
    {
        var source = """
            let count: number = 0;
            function inc(): number {
                count++;
                return count;
            }
            let result: any = (inc(), inc(), inc());
            console.log(result);
            console.log(count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n3\n", output);
    }

    [Theory, ModeData]
    public void Comma_ComplexForLoop(ExecutionMode mode)
    {
        var source = """
            let sum: number = 0;
            let j: number = 4;
            for (let i = 0; i < j; i++, j--) {
                sum += i + j;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        // i=0,j=4: sum=4; i=1,j=3: sum=4+4=8
        Assert.Equal("8\n", output);
    }
}
