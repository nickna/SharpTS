using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ErrorHandlingTests
{
    [Fact]
    public void TryCatch_CatchesThrow()
    {
        var source = """
            try {
                throw "error";
            } catch (e) {
                console.log("caught");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught\n", output);
    }

    [Fact]
    public void TryCatch_CatchesStringThrow()
    {
        var source = """
            try {
                throw "error message";
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("error message\n", output);
    }

    [Fact]
    public void TryCatch_CatchesNumberThrow()
    {
        var source = """
            try {
                throw 42;
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void TryFinally_FinallyAlwaysRuns()
    {
        var source = """
            try {
                console.log("try");
            } finally {
                console.log("finally");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("try\nfinally\n", output);
    }

    [Fact]
    public void TryCatchFinally_AllBlocksExecute()
    {
        var source = """
            try {
                console.log("try");
                throw "error";
            } catch (e) {
                console.log("catch");
            } finally {
                console.log("finally");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("try\ncatch\nfinally\n", output);
    }

    [Fact]
    public void TryCatch_FinallyRunsWithoutError()
    {
        var source = """
            try {
                console.log("no error");
            } catch (e) {
                console.log("should not print");
            } finally {
                console.log("finally runs");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("no error\nfinally runs\n", output);
    }

    [Fact]
    public void TryCatch_NestedBlocks()
    {
        var source = """
            try {
                try {
                    throw "inner";
                } catch (e) {
                    console.log(e);
                    throw "outer";
                }
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("inner\nouter\n", output);
    }

    [Fact]
    public void TryCatch_CatchFromFunctionCall()
    {
        var source = """
            function fail(): void {
                throw "function error";
            }
            try {
                fail();
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("function error\n", output);
    }

    [Fact]
    public void Finally_RunsWithReturn()
    {
        var source = """
            function test(): string {
                try {
                    return "returned";
                } finally {
                    console.log("finally runs");
                }
            }
            console.log(test());
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("finally runs\nreturned\n", output);
    }

    [Fact]
    public void TryCatch_CodeAfterCatchExecutes()
    {
        var source = """
            try {
                throw "error";
            } catch (e) {
                console.log("caught");
            }
            console.log("after");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught\nafter\n", output);
    }

    [Fact]
    public void TryCatch_OptionalCatchBinding_CatchesThrow()
    {
        var source = """
            try {
                throw "error";
            } catch {
                console.log("caught without param");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught without param\n", output);
    }

    [Fact]
    public void TryCatch_OptionalCatchBinding_WithFinally()
    {
        var source = """
            try {
                throw "error";
            } catch {
                console.log("caught");
            } finally {
                console.log("finally");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught\nfinally\n", output);
    }

    [Fact]
    public void TryCatch_OptionalCatchBinding_NoError()
    {
        var source = """
            try {
                console.log("no error");
            } catch {
                console.log("should not print");
            }
            console.log("after");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("no error\nafter\n", output);
    }

    [Fact]
    public void TryCatch_NestedOptionalCatchBinding()
    {
        var source = """
            try {
                try {
                    throw "inner";
                } catch {
                    console.log("inner caught");
                    throw "outer";
                }
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("inner caught\nouter\n", output);
    }
}
