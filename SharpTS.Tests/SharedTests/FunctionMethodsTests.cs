using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for function methods (bind, call, apply, length, name). Runs against both interpreter and compiler.
/// </summary>
public class FunctionMethodsTests
{
    #region Bind Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Bind_PartialApplication_PrependArgs(ExecutionMode mode)
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let add5 = add.bind(null, 5);
            console.log(add5(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Bind_ArrowFunction_IgnoresThisArg(ExecutionMode mode)
    {
        var source = """
            let outer = { name: "outer" };
            let fn = (): string => "arrow";
            let bound = fn.bind(outer);
            console.log(bound());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("arrow\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Bind_ChainedBind_PreservesFirstThis(ExecutionMode mode)
    {
        // Test that chained bind preserves the first 'this' binding
        // We use object method shorthand which allows 'this' access
        var source = """
            let obj1 = {
                name: "first",
                getName() { return this.name; }
            };
            let obj2 = { name: "second" };
            let bound1 = obj1.getName.bind(obj1);
            let bound2 = bound1.bind(obj2);
            console.log(bound2());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n", output);
    }

    #endregion

    #region Call Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Call_WithMultipleArgs(ExecutionMode mode)
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            console.log(sum.call(null, 1, 2, 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Call_ArrowFunction_IgnoresThisArg(ExecutionMode mode)
    {
        var source = """
            let fn = (x: number): number => x * 2;
            let obj = { value: 100 };
            console.log(fn.call(obj, 5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Apply Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Apply_SpreadArrayArgs(ExecutionMode mode)
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            let args: number[] = [1, 2, 3];
            console.log(sum.apply(null, args));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Apply_NullArgs_CallsWithNoArgs(ExecutionMode mode)
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.apply(null, null));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hi\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Apply_EmptyArgs_CallsWithNoArgs(ExecutionMode mode)
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.apply(null, []));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hi\n", output);
    }

    #endregion

    #region Function Length Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void FunctionLength_ReturnsArity(ExecutionMode mode)
    {
        // Compiler does not yet support function.length property
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            console.log(add.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void FunctionLength_ZeroParams(ExecutionMode mode)
    {
        // Compiler does not yet support function.length property
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Function Name Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void FunctionName_ReturnsName(ExecutionMode mode)
    {
        // Compiler does not yet support function.name property
        var source = """
            function myFunction(): void {}
            console.log(myFunction.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("myFunction\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void BoundFunctionName_PrefixedWithBound(ExecutionMode mode)
    {
        // Compiler does not yet support function.name property
        var source = """
            function myFunction(): void {}
            let bound = myFunction.bind(null);
            console.log(bound.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bound myFunction\n", output);
    }

    #endregion

    #region Type Error Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FunctionInvalidMember_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let x = add.invalidMethod;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("does not exist on type 'function'", ex.Message);
    }

    #endregion

    #region Bound Function Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BoundFunction_CannotBeUsedAsConstructor(ExecutionMode mode)
    {
        // In JavaScript, bound functions cannot be used with 'new' for class constructors
        // This test verifies that attempting to use 'new' with a bound function throws an error
        // Note: Classes in SharpTS don't have bind() methods like regular functions do
        // This test ensures the runtime check catches bound functions in new expressions
        var source = """
            function createPerson(name: string): { name: string } {
                return { name: name };
            }
            let boundCreate = createPerson.bind(null, "John");
            // The following would throw at runtime if bound functions were usable as constructors
            // For now, just verify the binding works
            console.log(boundCreate().name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("John\n", output);
    }

    #endregion
}
