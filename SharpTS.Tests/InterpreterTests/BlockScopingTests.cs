using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for ES6 block scoping behavior with let/const declarations.
/// These tests verify that variables are correctly scoped within their blocks.
/// Note: Static type checking catches most out-of-scope access at compile time,
/// so these tests focus on positive cases (variables accessible within scope).
/// </summary>
public class BlockScopingTests
{
    // ========== For Loop Block Scoping - Positive Cases ==========

    [Fact]
    public void ForLoop_LetVariable_AccessibleWithinLoop()
    {
        var source = """
            let result = "";
            for (let i = 0; i < 3; i++) {
                result += i;
            }
            console.log(result);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("012\n", output);
    }

    [Fact]
    public void ForLoop_LetVariable_ShadowsOuterVariable()
    {
        var source = """
            let i = 100;
            for (let i = 0; i < 3; i++) {
                // inner i shadows outer
            }
            console.log(i); // Should be 100, not modified by loop
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void ForLoop_NestedLoops_IndependentScopes()
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("00,01,10,11,\n", output);
    }

    // ========== Block Statement Scoping - Positive Cases ==========

    [Fact]
    public void BlockStatement_LetVariable_AccessibleWithinBlock()
    {
        var source = """
            let result = "";
            {
                let inner = "inside";
                result += inner;
            }
            console.log(result);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("inside\n", output);
    }

    [Fact]
    public void BlockStatement_LetVariable_ShadowsOuterVariable()
    {
        var source = """
            let x = "outer";
            {
                let x = "inner";
                console.log(x);
            }
            console.log(x);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("inner\nouter\n", output);
    }

    [Fact]
    public void NestedBlocks_EachHasOwnScope()
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ABA\n", output);
    }

    // ========== If/Else Block Scoping ==========

    [Fact]
    public void IfElse_SeparateScopes()
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("then\n", output);
    }

    // ========== While Loop Block Scoping ==========

    [Fact]
    public void WhileLoop_LetVariable_AccessibleWithinLoop()
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0,2,4,\n", output);
    }

    // ========== Const Block Scoping ==========

    [Fact]
    public void ForLoop_ConstVariable_NewBindingEachIteration()
    {
        var source = """
            let result = "";
            for (let i = 0; i < 3; i++) {
                const val = i * 10;
                result += val + ",";
            }
            console.log(result);
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0,10,20,\n", output);
    }

    // ========== Multiple Variables Same Name Different Scopes ==========

    [Fact]
    public void SameVariableName_DifferentScopes_Independent()
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
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("first0,first1,second10,second11,\n", output);
    }

}
