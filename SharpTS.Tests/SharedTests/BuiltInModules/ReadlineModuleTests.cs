using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'readline' module.
/// Tests synchronous methods that work in both interpreter and compiled modes.
/// Note: typeof checks on module namespace exports are interpreter-only due to
/// compiler limitations in how module namespaces expose their exports.
/// </summary>
public class ReadlineModuleTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CreateInterface_ReturnsObject(ExecutionMode mode)
    {
        // createInterface should return an object
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                console.log(typeof rl === 'object');
                console.log(rl !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Module_HasQuestionSync(ExecutionMode mode)
    {
        // readline module should export questionSync as a function
        // Interpreter-only: compiled mode doesn't support typeof checks on module namespace exports
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                console.log(typeof readline.questionSync === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Module_HasCreateInterface(ExecutionMode mode)
    {
        // readline module should export createInterface as a function
        // Interpreter-only: compiled mode doesn't support typeof checks on module namespace exports
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                console.log(typeof readline.createInterface === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }
}
