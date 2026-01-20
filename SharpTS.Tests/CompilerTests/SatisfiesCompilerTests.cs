using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// IL compiler tests for the 'satisfies' operator (TypeScript 4.9+).
/// Verifies that satisfies expressions compile correctly and produce the same
/// output as the interpreter.
/// </summary>
public class SatisfiesCompilerTests
{
    #region Basic Pass-Through

    [Fact]
    public void Satisfies_BasicNumber_CompilesCorrectly()
    {
        var source = """
            const x = 42 satisfies number;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Satisfies_BasicString_CompilesCorrectly()
    {
        var source = """
            const x = "hello" satisfies string;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Satisfies_ObjectLiteral_CompilesCorrectly()
    {
        var source = """
            const obj = { x: 1, y: 2 } satisfies { x: number, y: number };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n", output);
    }

    #endregion

    #region Union Constraints

    [Fact]
    public void Satisfies_UnionConstraint_CompilesCorrectly()
    {
        var source = """
            const x = "hello" satisfies string | number;
            const y = 42 satisfies string | number;
            console.log(x);
            console.log(y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n42\n", output);
    }

    #endregion

    #region Array Constraints

    [Fact]
    public void Satisfies_ArrayConstraint_CompilesCorrectly()
    {
        var source = """
            const arr = [1, 2, 3] satisfies number[];
            console.log(arr[0]);
            console.log(arr.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n3\n", output);
    }

    #endregion

    #region Escape Hatches

    [Fact]
    public void Satisfies_AnyConstraint_CompilesCorrectly()
    {
        var source = """
            const x = { a: 1, b: "two" } satisfies any;
            console.log(x.a);
            console.log(x.b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\ntwo\n", output);
    }

    [Fact]
    public void Satisfies_UnknownConstraint_CompilesCorrectly()
    {
        var source = """
            const arr = [10, 20, 30] satisfies unknown;
            console.log(arr[1]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    #endregion

    #region Chaining

    [Fact]
    public void Satisfies_Chained_CompilesCorrectly()
    {
        var source = """
            const x = 42 satisfies number satisfies number;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Excess Properties

    [Fact]
    public void Satisfies_ExcessProperties_CompilesCorrectly()
    {
        var source = """
            const obj = { x: 1, y: 2, z: 3 } satisfies { x: number };
            console.log(obj.x);
            console.log(obj.y);
            console.log(obj.z);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region With Arrow Functions

    [Fact]
    public void Satisfies_InArrowFunction_CompilesCorrectly()
    {
        var source = """
            const fn = () => {
                const x = 42 satisfies number;
                return x;
            };
            console.log(fn());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Satisfies_ArrowReturnsSatisfies_CompilesCorrectly()
    {
        var source = """
            const fn = () => ({ x: 1 } satisfies { x: number });
            console.log(fn().x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    #endregion

    #region Async Functions

    [Fact]
    public void Satisfies_InAsyncFunction_CompilesCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region With as const

    [Fact]
    public void Satisfies_WithAsConst_CompilesCorrectly()
    {
        var source = """
            const arr = [1, 2, 3] as const satisfies number[];
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Interpreter/Compiler Parity

    [Fact]
    public void Satisfies_InterpreterCompilerParity_Number()
    {
        var source = """
            const x = 99 satisfies number;
            console.log(x);
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void Satisfies_InterpreterCompilerParity_Object()
    {
        var source = """
            const obj = { name: "test", value: 42 } satisfies { name: string, value: number };
            console.log(obj.name);
            console.log(obj.value);
            """;

        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    #endregion
}
