using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for try/catch/finally error handling. Runs against both interpreter and compiler.
/// </summary>
public class ErrorHandlingTests
{
    #region Basic Try/Catch

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_CatchesThrow(ExecutionMode mode)
    {
        var source = """
            try {
                throw "error";
            } catch (e) {
                console.log("caught");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_CatchesStringThrow(ExecutionMode mode)
    {
        var source = """
            try {
                throw "error message";
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("error message\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_CatchesNumberThrow(ExecutionMode mode)
    {
        var source = """
            try {
                throw 42;
            } catch (e) {
                console.log(e);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Finally Clause

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryFinally_FinallyAlwaysRuns(ExecutionMode mode)
    {
        var source = """
            try {
                console.log("try");
            } finally {
                console.log("finally");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("try\nfinally\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatchFinally_AllBlocksExecute(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("try\ncatch\nfinally\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_FinallyRunsWithoutError(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("no error\nfinally runs\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Finally_RunsWithReturn(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("finally runs\nreturned\n", output);
    }

    #endregion

    #region Nested Try/Catch

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_NestedBlocks(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner\nouter\n", output);
    }

    #endregion

    #region Function Call Exceptions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_CatchFromFunctionCall(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function error\n", output);
    }

    #endregion

    #region Code After Catch

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_CodeAfterCatchExecutes(ExecutionMode mode)
    {
        var source = """
            try {
                throw "error";
            } catch (e) {
                console.log("caught");
            }
            console.log("after");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\nafter\n", output);
    }

    #endregion

    #region Optional Catch Binding

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_OptionalCatchBinding_CatchesThrow(ExecutionMode mode)
    {
        var source = """
            try {
                throw "error";
            } catch {
                console.log("caught without param");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught without param\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_OptionalCatchBinding_WithFinally(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\nfinally\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_OptionalCatchBinding_NoError(ExecutionMode mode)
    {
        var source = """
            try {
                console.log("no error");
            } catch {
                console.log("should not print");
            }
            console.log("after");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("no error\nafter\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TryCatch_NestedOptionalCatchBinding(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner caught\nouter\n", output);
    }

    #endregion
}
