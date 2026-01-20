using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests;

/// <summary>
/// Tests for ES2020 globalThis support.
/// </summary>
public class GlobalThisTests
{
    [Theory]
    [InlineData("""
        // globalThis.Math.PI works like Math.PI
        console.log(globalThis.Math.PI === Math.PI);
        """)]
    [InlineData("""
        // globalThis.Math.floor works like Math.floor
        console.log(globalThis.Math.floor(3.7));
        """)]
    public void GlobalThis_Math_MatchesDirect(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("""
        // globalThis.console.log works
        globalThis.console.log("Hello from globalThis");
        """)]
    public void GlobalThis_Console_Works(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        var compiled = TestHarness.RunCompiled(source);
        Assert.Equal(interpreted, compiled);
        Assert.Contains("Hello from globalThis", interpreted);
    }

    [Theory]
    [InlineData("""
        // Self-reference: globalThis.globalThis
        console.log(globalThis.globalThis === globalThis);
        """)]
    public void GlobalThis_SelfReference_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("true", interpreted);
    }

    [Theory]
    [InlineData("""
        // Assignment to globalThis
        globalThis.myValue = 42;
        console.log(globalThis.myValue);
        """)]
    public void GlobalThis_Assignment_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("42", interpreted);
    }

    [Theory]
    [InlineData("""
        // Index access with string literal
        globalThis["testProp"] = "hello";
        console.log(globalThis["testProp"]);
        """)]
    public void GlobalThis_IndexAccess_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("hello", interpreted);
    }

    [Theory]
    [InlineData("""
        // Dynamic index access
        const name: string = "Math";
        console.log(typeof globalThis[name]);
        """)]
    public void GlobalThis_DynamicIndex_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        // globalThis["Math"] should return an object (the Math singleton)
        Assert.Contains("object", interpreted);
    }

    [Theory]
    [InlineData("""
        // globalThis.undefined equals undefined
        console.log(globalThis.undefined === undefined);
        """)]
    [InlineData("""
        // globalThis.NaN is NaN
        console.log(Number.isNaN(globalThis.NaN));
        """)]
    [InlineData("""
        // globalThis.Infinity is Infinity
        console.log(globalThis.Infinity === Infinity);
        """)]
    public void GlobalThis_BuiltInConstants_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("true", interpreted);
    }

    [Theory]
    [InlineData("""
        // User-defined variables are NOT accessible via globalThis (module semantics)
        let x: number = 1;
        console.log(globalThis.x === undefined);
        """)]
    public void GlobalThis_ModuleScopedVarsNotAccessible_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("true", interpreted);
    }

    [Theory]
    [InlineData("""
        // Chained self-reference: globalThis.globalThis.Math.PI
        console.log(globalThis.globalThis.Math.PI === Math.PI);
        """)]
    public void GlobalThis_ChainedSelfReference_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("true", interpreted);
    }

    [Theory]
    [InlineData("""
        // Process access through globalThis
        console.log(typeof globalThis.process);
        """)]
    public void GlobalThis_Process_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("object", interpreted);
    }

    [Theory]
    [InlineData("""
        // globalThis.parseInt works
        console.log(globalThis.parseInt("42"));
        """)]
    public void GlobalThis_ParseInt_Interpreted(string source)
    {
        var interpreted = TestHarness.RunInterpreted(source);
        Assert.Contains("42", interpreted);
    }
}
