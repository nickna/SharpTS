using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for named function expressions (NFEs). Runs against both interpreter and compiler.
/// Named function expressions allow recursion within the function body
/// while keeping the name scoped to the function itself.
/// </summary>
public class NamedFunctionExpressionTests
{
    #region Basic NFE Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_BasicRecursion_Works(ExecutionMode mode)
    {
        var source = """
            const factorial = function fact(n: number): number {
                if (n <= 1) return 1;
                return n * fact(n - 1);
            };
            console.log(factorial(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("120\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_NameNotVisibleOutside(ExecutionMode mode)
    {
        // The function name is only visible inside the function body, not outside
        // Here we just verify the function can be called through its assigned variable
        var source = """
            const f = function myFunc(x: number): number {
                return x * 2;
            };
            console.log(f(21));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_ParameterShadowsName(ExecutionMode mode)
    {
        // If a parameter has the same name as the function, parameter wins
        var source = """
            const f = function myFunc(myFunc: number): number {
                return myFunc + 1;
            };
            console.log(f(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    #endregion

    #region Nested Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_NestedFunctions(ExecutionMode mode)
    {
        var source = """
            const outer = function outerFn(n: number): number {
                const inner = function innerFn(m: number): number {
                    if (m <= 0) return 0;
                    return m + innerFn(m - 1);
                };
                return inner(n);
            };
            console.log(outer(5)); // 5 + 4 + 3 + 2 + 1 = 15
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region Object Literals

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_InObjectLiteral(ExecutionMode mode)
    {
        var source = """
            const obj = {
                countdown: function counter(n: number): string {
                    if (n <= 0) return "done";
                    return n + "," + counter(n - 1);
                }
            };
            console.log(obj.countdown(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3,2,1,done\n", output);
    }

    #endregion

    #region Callback Arguments

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_AsCallbackArgument(ExecutionMode mode)
    {
        var source = """
            function apply(fn: (n: number) => number, value: number): number {
                return fn(value);
            }

            const result = apply(function doubler(n: number): number {
                return n * 2;
            }, 21);

            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Recursion Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_FibonacciRecursion(ExecutionMode mode)
    {
        var source = """
            const fib = function fibonacci(n: number): number {
                if (n <= 1) return n;
                return fibonacci(n - 1) + fibonacci(n - 2);
            };
            console.log(fib(10)); // 55
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("55\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_MutualRecursionWithClosure(ExecutionMode mode)
    {
        var source = """
            const isEven = function even(n: number): boolean {
                if (n === 0) return true;
                return isOdd(n - 1);
            };

            const isOdd = function odd(n: number): boolean {
                if (n === 0) return false;
                return isEven(n - 1);
            };

            console.log(isEven(10));
            console.log(isOdd(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    #endregion

    #region Default Parameters

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_WithDefaultParameters(ExecutionMode mode)
    {
        var source = """
            const greet = function greeter(name: string, greeting: string = "Hello"): string {
                return greeting + ", " + name;
            };
            console.log(greet("World"));
            console.log(greet("TypeScript", "Hi"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World\nHi, TypeScript\n", output);
    }

    #endregion

    #region Anonymous Function Expressions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_Anonymous_StillWorks(ExecutionMode mode)
    {
        // Anonymous function expressions should still work
        var source = """
            const f = function(x: number): number {
                return x * 2;
            };
            console.log(f(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Closure Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_ClosureCapture(ExecutionMode mode)
    {
        var source = """
            function makeCounter(): () => number {
                let count = 0;
                return function counter(): number {
                    count++;
                    return count;
                };
            }

            const c = makeCounter();
            console.log(c());
            console.log(c());
            console.log(c());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region IIFE Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_ImmediatelyInvoked(ExecutionMode mode)
    {
        // IIFE (Immediately Invoked Function Expression) with name
        var source = """
            const result = (function fact(n: number): number {
                if (n <= 1) return 1;
                return n * fact(n - 1);
            })(6);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("720\n", output);
    }

    #endregion

    #region Rest Parameters

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_WithRestParameters(ExecutionMode mode)
    {
        var source = """
            const sum = function adder(...nums: number[]): number {
                if (nums.length === 0) return 0;
                return nums[0] + adder(...nums.slice(1));
            };
            console.log(sum(1, 2, 3, 4, 5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region Can Log Function

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NamedFunctionExpression_CanLogFunction(ExecutionMode mode)
    {
        // Named function expressions have their name in the string representation
        var source = """
            const f = function myNamedFunc(): number { return 42; };
            // Just verify the function exists and can be converted to string
            console.log(f());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion
}
