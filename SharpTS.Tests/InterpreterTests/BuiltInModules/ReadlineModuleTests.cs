using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'readline' module.
/// Uses interpreter mode since readline module isn't fully supported in compiled mode.
/// Note: Interactive input tests are limited - we focus on object structure and exports.
/// </summary>
public class ReadlineModuleTests
{
    [Fact]
    public void CreateInterface_ReturnsObject()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Module_HasQuestionSync()
    {
        // readline module should export questionSync as a callable
        // Note: BuiltInMethod returns 'object' from typeof, not 'function'
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                console.log('questionSync' in readline);
                console.log(readline.questionSync !== undefined);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Module_HasCreateInterface()
    {
        // readline module should export createInterface as a callable
        // Note: BuiltInMethod returns 'object' from typeof, not 'function'
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                console.log('createInterface' in readline);
                console.log(readline.createInterface !== undefined);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
