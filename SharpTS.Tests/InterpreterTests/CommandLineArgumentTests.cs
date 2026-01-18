using SharpTS.Runtime.BuiltIns;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for command-line argument passing to scripts via process.argv.
/// </summary>
public class CommandLineArgumentTests
{
    [Fact]
    public void ProcessArgv_WithScriptArgs_ReturnsCorrectArray()
    {
        ProcessBuiltIns.SetScriptArguments("/path/to/script.ts", ["arg1", "arg2"]);
        try
        {
            var source = """
                const args = process.argv.slice(2);
                console.log(args[0] === 'arg1');
                console.log(args[1] === 'arg2');
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("true\ntrue\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_NoScriptArgs_ReturnsRuntimePathOnly()
    {
        ProcessBuiltIns.ClearScriptArguments();
        try
        {
            var source = """
                // When no script args set, falls back to Environment.GetCommandLineArgs()
                // argv[0] should be the runtime path (process path)
                console.log(process.argv.length >= 1);
                console.log(typeof process.argv[0] === 'string');
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("true\ntrue\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_SliceTwoPattern_WorksWithScriptArgs()
    {
        ProcessBuiltIns.SetScriptArguments("/path/to/script.ts", ["hello", "world"]);
        try
        {
            var source = """
                const userArgs = process.argv.slice(2);
                console.log(userArgs.join(' '));
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("hello world\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_EmptyScriptArgs_HasScriptPathOnly()
    {
        ProcessBuiltIns.SetScriptArguments("/path/to/script.ts", []);
        try
        {
            var source = """
                // With empty script args, argv should be [runtime, script]
                console.log(process.argv.length);
                console.log(process.argv[1]);
                console.log(process.argv.slice(2).length);
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("2\n/path/to/script.ts\n0\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_ArgsWithSpaces_PreservedCorrectly()
    {
        ProcessBuiltIns.SetScriptArguments("/path/to/script.ts", ["hello world", "foo bar"]);
        try
        {
            var source = """
                const args = process.argv.slice(2);
                console.log(args[0]);
                console.log(args[1]);
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("hello world\nfoo bar\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_ArgvIndexZero_IsRuntimePath()
    {
        ProcessBuiltIns.SetScriptArguments("/path/to/script.ts", ["arg1"]);
        try
        {
            var source = """
                // argv[0] should be the runtime path (always a string)
                console.log(typeof process.argv[0] === 'string');
                console.log(process.argv[0].length > 0);
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("true\ntrue\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_ArgvIndexOne_IsScriptPath()
    {
        ProcessBuiltIns.SetScriptArguments("/custom/path/to/my-script.ts", ["arg1"]);
        try
        {
            var source = """
                console.log(process.argv[1]);
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("/custom/path/to/my-script.ts\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_CacheInvalidation_AfterSetScriptArgs()
    {
        // First call sets some args
        ProcessBuiltIns.SetScriptArguments("/script1.ts", ["a", "b"]);
        try
        {
            var source1 = """
                console.log(process.argv.slice(2).join(','));
                """;
            var output1 = TestHarness.RunInterpreted(source1);
            Assert.Equal("a,b\n", output1);

            // Second call changes args - cache should be invalidated
            ProcessBuiltIns.SetScriptArguments("/script2.ts", ["x", "y", "z"]);
            var source2 = """
                console.log(process.argv.slice(2).join(','));
                """;
            var output2 = TestHarness.RunInterpreted(source2);
            Assert.Equal("x,y,z\n", output2);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_ManyArguments_AllPreserved()
    {
        var args = new[] { "arg1", "arg2", "arg3", "arg4", "arg5", "arg6", "arg7", "arg8", "arg9", "arg10" };
        ProcessBuiltIns.SetScriptArguments("/script.ts", args);
        try
        {
            var source = """
                const userArgs = process.argv.slice(2);
                console.log(userArgs.length);
                console.log(userArgs.join(' '));
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("10\narg1 arg2 arg3 arg4 arg5 arg6 arg7 arg8 arg9 arg10\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    [Fact]
    public void ProcessArgv_SpecialCharacters_PreservedCorrectly()
    {
        ProcessBuiltIns.SetScriptArguments("/script.ts", ["--verbose", "--output=file.txt", "-n", "test@example.com"]);
        try
        {
            var source = """
                const args = process.argv.slice(2);
                console.log(args[0]);
                console.log(args[1]);
                console.log(args[2]);
                console.log(args[3]);
                """;
            var output = TestHarness.RunInterpreted(source);
            Assert.Equal("--verbose\n--output=file.txt\n-n\ntest@example.com\n", output);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }
}
