using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Tests for CLI script execution with arguments.
/// </summary>
public class CliScriptExecutionTests
{
    [Fact]
    public void Execute_SimpleScript()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello, World!", result.StandardOutput);
    }

    [Fact]
    public void Execute_WithArgs_PassesToProcessArgv()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("args.ts", CliFixtures.ProcessArgvArgsOnlyScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\" arg1 arg2", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("arg count: 2", result.StandardOutput);
        Assert.Contains("arg: arg1", result.StandardOutput);
        Assert.Contains("arg: arg2", result.StandardOutput);
    }

    [Fact]
    public void Execute_DoubleDashSeparator_PassesFlagsAsArgs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("args.ts", CliFixtures.ProcessArgvArgsOnlyScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\" -- --flag --other", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("arg: --flag", result.StandardOutput);
        Assert.Contains("arg: --other", result.StandardOutput);
    }

    [Fact]
    public void Execute_NonexistentScript_Error()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var missingPath = tempDir.GetPath("missing.ts");

        var result = CliTestHelper.RunCli($"\"{missingPath}\"", tempDir.Path);

        // Should fail - file not found
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Execute_WithDecoratorFlag_LegacyMode()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("dec.ts", CliFixtures.DecoratorTestScript);

        var result = CliTestHelper.RunCli($"--experimentalDecorators \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("decorated: greet", result.StandardOutput);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact]
    public void Execute_NumericComputation()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("numeric.ts", CliFixtures.NumericScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("15", result.StandardOutput); // 1+2+3+4+5 = 15
    }

    [Fact]
    public void Execute_ProcessArgv_HasRuntimeAndScriptPath()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("argv.ts", CliFixtures.ProcessArgvScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        // process.argv[0] is runtime path, argv[1] is script path
        var lines = result.StandardOutput.Trim().Split('\n');
        Assert.True(lines.Length >= 2, "Expected at least runtime path and script path");
        // Script path should be in the output
        Assert.Contains("argv.ts", result.StandardOutput);
    }

    [Fact]
    public void Execute_ArgsAfterScript_NoDoubleDash()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("args.ts", CliFixtures.ProcessArgvArgsOnlyScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\" value1 value2 value3", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("arg count: 3", result.StandardOutput);
        Assert.Contains("arg: value1", result.StandardOutput);
        Assert.Contains("arg: value2", result.StandardOutput);
        Assert.Contains("arg: value3", result.StandardOutput);
    }

    [Fact]
    public void Execute_MixedArgsWithDoubleDash()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("args.ts", CliFixtures.ProcessArgvArgsOnlyScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\" before -- after", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("arg: before", result.StandardOutput);
        Assert.Contains("arg: after", result.StandardOutput);
    }

    [Fact]
    public void Execute_ClassDefinition()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var classScript = """
            class Person {
                name: string;
                constructor(name: string) {
                    this.name = name;
                }
                greet(): void {
                    console.log("Hello, " + this.name);
                }
            }
            const p = new Person("Alice");
            p.greet();
            """;
        var scriptPath = tempDir.CreateFile("class.ts", classScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello, Alice", result.StandardOutput);
    }

    [Fact]
    public void Execute_ArrowFunctions()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var arrowScript = """
            const add = (a: number, b: number): number => a + b;
            const result = add(3, 4);
            console.log(result);
            """;
        var scriptPath = tempDir.CreateFile("arrow.ts", arrowScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("7", result.StandardOutput);
    }

    [Fact]
    public void Execute_ArrayMethods()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var arrayScript = """
            const numbers: number[] = [1, 2, 3, 4, 5];
            const doubled = numbers.map((n: number): number => n * 2);
            const sum = doubled.reduce((acc: number, n: number): number => acc + n, 0);
            console.log(sum);
            """;
        var scriptPath = tempDir.CreateFile("array.ts", arrayScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("30", result.StandardOutput); // (1+2+3+4+5)*2 = 30
    }

    [Fact]
    public void Execute_TryCatch()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var tryCatchScript = """
            try {
                throw "caught this";
            } catch (e) {
                console.log("error: " + e);
            } finally {
                console.log("finally");
            }
            """;
        var scriptPath = tempDir.CreateFile("trycatch.ts", tryCatchScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("error: caught this", result.StandardOutput);
        Assert.Contains("finally", result.StandardOutput);
    }

    [Fact]
    public void Execute_TemplateLiterals()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var templateScript = """
            const name = "World";
            const greeting = `Hello, ${name}!`;
            console.log(greeting);
            """;
        var scriptPath = tempDir.CreateFile("template.ts", templateScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Hello, World!", result.StandardOutput);
    }

    [Fact]
    public void Execute_Destructuring()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var destructScript = """
            const obj = { x: 1, y: 2 };
            const { x, y } = obj;
            console.log(x + y);
            """;
        var scriptPath = tempDir.CreateFile("destruct.ts", destructScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("3", result.StandardOutput);
    }
}
