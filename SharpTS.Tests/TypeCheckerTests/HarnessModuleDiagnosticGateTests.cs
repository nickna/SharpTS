using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// The module runners (<see cref="TestHarness.RunModules"/> and friends) must fail on
/// error-severity type-checker diagnostics after <c>CheckModules</c>, mirroring the CLI
/// (<c>Program.RunModuleFile</c>) — #1226. Before this gate, <c>CheckModules</c> recorded
/// type errors with recovery, the interpreter/compiler ran the ill-typed program anyway, and
/// a type-check regression in a stdlib facade (e.g. #1218's <c>url</c> import) stayed invisible
/// to the whole suite. These tests pin the gate itself so it can't silently regress.
/// </summary>
public class HarnessModuleDiagnosticGateTests
{
    private static Dictionary<string, string> IllTyped => new()
    {
        // `x: number = "str"` is a plain type error the checker records and recovers from;
        // the program still "runs" (prints 1) under both the interpreter and compiler.
        ["main.ts"] = "export {};\nconst x: number = \"not a number\";\nconsole.log(1);\n",
    };

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RunModules_ThrowsOnTypeError(ExecutionMode mode)
    {
        var ex = Assert.Throws<TypeCheckDiagnosticException>(
            () => TestHarness.RunModules(IllTyped, "main.ts", mode));
        Assert.NotEmpty(ex.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RunModules_AllowTypeErrors_RunsIllTypedProgram(ExecutionMode mode)
    {
        // Opt-out escape hatch for tests that intentionally exercise ill-typed programs.
        var output = TestHarness.RunModules(IllTyped, "main.ts", mode, allowTypeErrors: true);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void RunModules_WellTypedProgram_DoesNotThrow()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = "export {};\nconst x: number = 42;\nconsole.log(x);\n",
        };
        Assert.Equal("42\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted));
    }
}
