using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the 'satisfies' operator (TypeScript 4.9+).
/// Migrated from CompilerTests to run against both interpreter and compiler.
/// </summary>
public class SatisfiesTests
{
    #region Basic Pass-Through

    [Theory, ModeData]
    public void Satisfies_BasicNumber(ExecutionMode mode)
    {
        var source = """
            const x = 42 satisfies number;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Satisfies_BasicString(ExecutionMode mode)
    {
        var source = """
            const x = "hello" satisfies string;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void Satisfies_ObjectLiteral(ExecutionMode mode)
    {
        var source = """
            const obj = { x: 1, y: 2 } satisfies { x: number, y: number };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    #endregion

    #region Union Constraints

    [Theory, ModeData]
    public void Satisfies_UnionConstraint(ExecutionMode mode)
    {
        var source = """
            const x = "hello" satisfies string | number;
            const y = 42 satisfies string | number;
            console.log(x);
            console.log(y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n42\n", output);
    }

    #endregion

    #region Array Constraints

    [Theory, ModeData]
    public void Satisfies_ArrayConstraint(ExecutionMode mode)
    {
        var source = """
            const arr = [1, 2, 3] satisfies number[];
            console.log(arr[0]);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n", output);
    }

    #endregion

    #region Escape Hatches

    [Theory, ModeData]
    public void Satisfies_AnyConstraint(ExecutionMode mode)
    {
        var source = """
            const x = { a: 1, b: "two" } satisfies any;
            console.log(x.a);
            console.log(x.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntwo\n", output);
    }

    [Theory, ModeData]
    public void Satisfies_UnknownConstraint(ExecutionMode mode)
    {
        var source = """
            const arr = [10, 20, 30] satisfies unknown;
            console.log(arr[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    #endregion

    #region Chaining

    [Theory, ModeData]
    public void Satisfies_Chained(ExecutionMode mode)
    {
        var source = """
            const x = 42 satisfies number satisfies number;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Excess Properties

    [Theory, ModeData]
    public void Satisfies_ExcessProperties(ExecutionMode mode)
    {
        var source = """
            const obj = { x: 1, y: 2, z: 3 } satisfies { x: number };
            console.log(obj.x);
            console.log(obj.y);
            console.log(obj.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region With Arrow Functions

    [Theory, ModeData]
    public void Satisfies_InArrowFunction(ExecutionMode mode)
    {
        var source = """
            const fn = () => {
                const x = 42 satisfies number;
                return x;
            };
            console.log(fn());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Satisfies_ArrowReturnsSatisfies(ExecutionMode mode)
    {
        var source = """
            const fn = () => ({ x: 1 } satisfies { x: number });
            console.log(fn().x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    #endregion

    #region Async Functions

    [Theory, ModeData]
    public void Satisfies_InAsyncFunction(ExecutionMode mode)
    {
        var source = """
            async function test(): Promise<number> {
                const x = 100 satisfies number;
                return x;
            }
            async function main(): Promise<void> {
                const v = await test();
                console.log(v);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region With as const

    [Theory, ModeData]
    public void Satisfies_WithAsConst(ExecutionMode mode)
    {
        var source = """
            const arr = [1, 2, 3] as const satisfies number[];
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion
}
