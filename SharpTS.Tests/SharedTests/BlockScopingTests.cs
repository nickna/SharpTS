using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES6 block scoping behavior with let/const declarations. Runs against both interpreter and compiler.
/// These tests verify that variables are correctly scoped within their blocks.
/// Note: Static type checking catches most out-of-scope access at compile time,
/// so these tests focus on positive cases (variables accessible within scope).
/// </summary>
public class BlockScopingTests
{
    #region For Loop Block Scoping

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForLoop_LetVariable_AccessibleWithinLoop(ExecutionMode mode)
    {
        var source = """
            let result = "";
            for (let i = 0; i < 3; i++) {
                result += i;
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("012\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForLoop_LetVariable_ShadowsOuterVariable(ExecutionMode mode)
    {
        var source = """
            let i = 100;
            for (let i = 0; i < 3; i++) {
                // inner i shadows outer
            }
            console.log(i); // Should be 100, not modified by loop
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForLoop_NestedLoops_IndependentScopes(ExecutionMode mode)
    {
        var source = """
            let result = "";
            for (let i = 0; i < 2; i++) {
                for (let j = 0; j < 2; j++) {
                    result += i + "" + j + ",";
                }
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("00,01,10,11,\n", output);
    }

    #endregion

    #region Block Statement Scoping

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BlockStatement_LetVariable_AccessibleWithinBlock(ExecutionMode mode)
    {
        var source = """
            let result = "";
            {
                let inner = "inside";
                result += inner;
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("inside\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BlockStatement_LetVariable_ShadowsOuterVariable(ExecutionMode mode)
    {
        var source = """
            let x = "outer";
            {
                let x = "inner";
                console.log(x);
            }
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner\nouter\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedBlocks_EachHasOwnScope(ExecutionMode mode)
    {
        var source = """
            let result = "";
            {
                let a = "A";
                {
                    let b = "B";
                    result += a + b;
                }
                result += a;
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ABA\n", output);
    }

    #endregion

    #region If/Else Block Scoping

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IfElse_SeparateScopes(ExecutionMode mode)
    {
        var source = """
            let result = "";
            if (true) {
                let x = "then";
                result += x;
            } else {
                let x = "else";
                result += x;
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    #endregion

    #region While Loop Block Scoping

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void WhileLoop_LetVariable_AccessibleWithinLoop(ExecutionMode mode)
    {
        var source = """
            let result = "";
            let count = 0;
            while (count < 3) {
                let item = count * 2;
                result += item + ",";
                count++;
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,2,4,\n", output);
    }

    #endregion

    #region Const Block Scoping

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForLoop_ConstVariable_NewBindingEachIteration(ExecutionMode mode)
    {
        var source = """
            let result = "";
            for (let i = 0; i < 3; i++) {
                const val = i * 10;
                result += val + ",";
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,10,20,\n", output);
    }

    #endregion

    #region Multiple Variables Same Name Different Scopes

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SameVariableName_DifferentScopes_Independent(ExecutionMode mode)
    {
        var source = """
            let result = "";
            for (let i = 0; i < 2; i++) {
                result += "first" + i + ",";
            }
            for (let i = 10; i < 12; i++) {
                result += "second" + i + ",";
            }
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("first0,first1,second10,second11,\n", output);
    }

    #endregion
}
