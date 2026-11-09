using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedFunctionNameTests
{
    [Theory, ModeData]
    public void NestedDeclarationsExposeTheirSourceNames(ExecutionMode mode)
    {
        const string source = """
            function outer() {
                function inner(a, b = 2) { return a + b; }
                return inner;
            }
            function capturing(value) {
                function inner(a) { return a + value; }
                return inner;
            }
            for (const fn of [outer(), capturing(1)]) {
                console.log(fn.name, Object.getOwnPropertyDescriptor(fn, 'name').value, fn.length, fn(3));
            }
            console.log(outer() === outer(), capturing(1) === capturing(1));
            """;
        Assert.Equal("inner inner 1 5\ninner inner 1 4\nfalse false\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NestedDeclarationsInsideStateMachinesExposeTheirSourceNames(ExecutionMode mode)
    {
        const string source = """
            async function outer() {
                function inner() {}
                console.log(inner.name);
            }
            function* capturing(value) {
                function captured() { return value; }
                yield captured.name;
            }
            outer();
            console.log(capturing(1).next().value);
            """;
        Assert.Equal("inner\ncaptured\n", TestHarness.RunUnchecked(source, mode));
    }
}
