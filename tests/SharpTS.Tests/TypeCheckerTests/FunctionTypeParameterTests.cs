using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// The function-type vs grouped-type disambiguation must recognize a leading rest parameter
/// (<c>(...args: T) =&gt; R</c>) and a leading optional parameter (<c>(x?: T) =&gt; R</c>), not just
/// <c>()</c> and <c>(name:</c>. (Conformance gap: these forms previously parsed as grouped types.)
/// </summary>
public class FunctionTypeParameterTests
{
    [Fact]
    public void RestFirstParameter_Parses()
    {
        TestHarness.RunInterpreted("let a: (...args: number[]) => number;");
    }

    [Fact]
    public void OptionalFirstParameter_Parses()
    {
        TestHarness.RunInterpreted("let a: (x?: number) => number;");
    }

    [Fact]
    public void RestParameter_InNestedFunctionType_Parses()
    {
        TestHarness.RunInterpreted("let a: (cb: (...args: number[]) => void) => void;");
    }

    [Fact]
    public void GroupedType_StillParses()
    {
        // Regression guard: a genuine grouped/parenthesized type must not be mistaken for a function type.
        TestHarness.RunInterpreted("let a: (string | number)[] = [];");
    }
}
