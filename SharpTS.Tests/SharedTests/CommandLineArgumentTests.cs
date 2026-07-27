using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Shared tests for process.argv command-line argument handling.
/// Uses [Collection] to prevent parallel execution with other tests that
/// mutate ProcessBuiltIns.SetScriptArguments static state.
/// Tests use TestHarness.RunWithArgs to pass arguments in both modes:
/// - Interpreted: via ProcessBuiltIns.SetScriptArguments
/// - Compiled: via actual command-line arguments to the spawned process
/// </summary>
[Collection("ScriptArgs")]
public class CommandLineArgumentTests
{
    #region process.argv.slice(2) — User Arguments

    [Theory, ModeData]
    public void ProcessArgv_SliceTwo_ReturnsUserArgs(ExecutionMode mode)
    {
        var source = """
            const args = process.argv.slice(2);
            console.log(args[0] === 'arg1');
            console.log(args[1] === 'arg2');
            """;

        var output = TestHarness.RunWithArgs(source, mode, ["arg1", "arg2"]);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ProcessArgv_SliceTwo_JoinsCorrectly(ExecutionMode mode)
    {
        var source = """
            const userArgs = process.argv.slice(2);
            console.log(userArgs.join(' '));
            """;

        var output = TestHarness.RunWithArgs(source, mode, ["hello", "world"]);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void ProcessArgv_EmptyArgs_SliceTwoIsEmpty(ExecutionMode mode)
    {
        var source = """
            console.log(process.argv.slice(2).length);
            """;

        var output = TestHarness.RunWithArgs(source, mode, []);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Argument Preservation

    [Theory, ModeData]
    public void ProcessArgv_ArgsWithSpaces_PreservedCorrectly(ExecutionMode mode)
    {
        var source = """
            const args = process.argv.slice(2);
            console.log(args[0]);
            console.log(args[1]);
            """;

        var output = TestHarness.RunWithArgs(source, mode, ["hello world", "foo bar"]);
        Assert.Equal("hello world\nfoo bar\n", output);
    }

    [Theory, ModeData]
    public void ProcessArgv_ManyArguments_AllPreserved(ExecutionMode mode)
    {
        var source = """
            const userArgs = process.argv.slice(2);
            console.log(userArgs.length);
            console.log(userArgs.join(' '));
            """;

        var args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5", "arg6", "arg7", "arg8", "arg9", "arg10" };
        var output = TestHarness.RunWithArgs(source, mode, args);
        Assert.Equal("10\narg1 arg2 arg3 arg4 arg5 arg6 arg7 arg8 arg9 arg10\n", output);
    }

    [Theory, ModeData]
    public void ProcessArgv_SpecialCharacters_PreservedCorrectly(ExecutionMode mode)
    {
        var source = """
            const args = process.argv.slice(2);
            console.log(args[0]);
            console.log(args[1]);
            console.log(args[2]);
            console.log(args[3]);
            """;

        var output = TestHarness.RunWithArgs(source, mode,
            ["--verbose", "--output=file.txt", "-n", "test@example.com"]);
        Assert.Equal("--verbose\n--output=file.txt\n-n\ntest@example.com\n", output);
    }

    #endregion

    #region argv Structure

    [Theory, ModeData]
    public void ProcessArgv_LengthIncludesRuntimeAndScript(ExecutionMode mode)
    {
        // argv = [runtime_path, script_or_dll_path, ...user_args]
        // So length should be user_args.length + 2
        var source = """
            console.log(process.argv.length);
            """;

        var output = TestHarness.RunWithArgs(source, mode, ["a", "b", "c"]);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void ProcessArgv_IndexOneIsNonEmptyString(ExecutionMode mode)
    {
        // argv[1] is the script path (interpreter) or DLL path (compiled)
        // Both should be a non-empty string
        var source = """
            console.log(typeof process.argv[1] === 'string');
            console.log(process.argv[1].length > 0);
            """;

        var output = TestHarness.RunWithArgs(source, mode, ["arg1"]);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion
}
