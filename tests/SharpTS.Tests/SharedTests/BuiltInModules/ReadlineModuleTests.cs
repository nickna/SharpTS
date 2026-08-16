using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'readline' module.
/// </summary>
public class ReadlineModuleTests
{
    [Theory, ModeData]
    public void CreateInterface_ReturnsObject(ExecutionMode mode)
    {
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

    [Theory, ModeData]
    public void Module_HasQuestionSync(ExecutionMode mode)
    {
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

    [Theory, ModeData]
    public void Module_HasCreateInterface(ExecutionMode mode)
    {
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

    [Theory, ModeData]
    public void Interface_HasEventEmitterMethods(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                console.log(typeof rl.on === 'function');
                console.log(typeof rl.once === 'function');
                console.log(typeof rl.off === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Interface_SetAndGetPrompt(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                rl.setPrompt('$ ');
                console.log(rl.getPrompt() === '$ ');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Interface_DefaultPrompt(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                console.log(rl.getPrompt());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("> \n", output);
    }

    [Theory, ModeData]
    public void Interface_Close_EmitsEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                let closeCalled = false;
                rl.on('close', () => { closeCalled = true; });
                rl.close();
                console.log(closeCalled);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
    }

    [Theory, ModeData]
    public void Interface_Close_EmitsEvent_WithCount(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                let count = 0;
                rl.on('close', () => { count++; });
                rl.close();
                console.log('count:', count);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("count: 1", output);
    }

    [Theory, ModeData]
    public void Interface_PauseResume(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                console.log(typeof rl.pause === 'function');
                console.log(typeof rl.resume === 'function');
                rl.pause();
                rl.resume();
                console.log('ok');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("ok", output);
    }

    [Theory, ModeData]
    public void Interface_PauseResume_EmitsEvents(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                const events: string[] = [];
                rl.on('pause', () => { events.push('paused'); });
                rl.on('resume', () => { events.push('resumed'); });
                rl.pause();
                rl.resume();
                console.log(events.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("paused", output);
        Assert.Contains("resumed", output);
    }

    [Theory, ModeData]
    public void Interface_Write(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                console.log(typeof rl.write === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Interface_PromptWithCustomPrefix(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface({ prompt: '>> ' });
                console.log(rl.getPrompt() === '>> ');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Interface_Question_Exists(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                console.log(typeof rl.question === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Interface_MultipleSetPrompt(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as readline from 'readline';
                const rl = readline.createInterface();
                rl.setPrompt('a> ');
                console.log(rl.getPrompt());
                rl.setPrompt('b> ');
                console.log(rl.getPrompt());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a> \nb> \n", output);
    }

    [Theory, ModeData]
    public void NamedImport_CreateInterface(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createInterface } from 'readline';
                const rl = createInterface();
                console.log(typeof rl === 'object');
                console.log(rl.getPrompt());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
        Assert.Contains("> ", output);
    }
}
