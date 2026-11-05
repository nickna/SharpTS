using System.Diagnostics;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliCommonJsClassBindingTests
{
    public static IEnumerable<object[]> ClassMembers()
    {
        yield return ["instance-method", """
            class Reader { read() { return exports.value; } }
            exports.Reader = Reader;
            """, "console.log(new api.Reader().read());", "7\n"];
        yield return ["static-method", """
            class Reader { static read() { return exports.value; } }
            exports.Reader = Reader;
            """, "console.log(api.Reader.read());", "7\n"];
        yield return ["getter", """
            class Reader { get value() { return exports.value; } }
            exports.Reader = Reader;
            """, "console.log(new api.Reader().value);", "7\n"];
        yield return ["setter", """
            class Reader { set value(value) { exports.value = value; } }
            exports.Reader = Reader;
            """, """
            const reader = new api.Reader();
            reader.value = 9;
            console.log(api.value);
            """, "9\n"];
        yield return ["live-exports-value", """
            class Reader { read() { return exports.value; } }
            exports.Reader = Reader;
            exports.value = 11;
            """, "console.log(new api.Reader().read());", "11\n"];
        yield return ["shadowed-wrapper-parameters", """
            class Reader {
              read(exports, module, require) {
                return [exports.value, module.exports.value, require('./value.cjs').value].join(':');
              }
            }
            exports.Reader = Reader;
            """, """
            console.log(new api.Reader().read(
              { value: 8 }, { exports: { value: 9 } }, () => ({ value: 10 })));
            """, "8:9:10\n"];
        yield return ["shadowed-wrapper-assignments", """
            class Reader {
              read(exports, module) {
                exports = { value: 11 };
                module.exports = { value: 12 };
                return [exports.value, module.exports.value].join(':');
              }
            }
            exports.Reader = Reader;
            """, """
            console.log(new api.Reader().read({ value: 8 }, { exports: { value: 9 } }));
            console.log(api.value);
            console.log(typeof api.Reader);
            """, "11:12\n7\nfunction\n"];
    }

    [Theory, MemberData(nameof(ClassMembers))]
    public void ClassMemberUsesItsLexicalCommonJsBindings(
        string name, string declaration, string consumer, string expected)
    {
        Assert.NotEmpty(name);
        var files = new Dictionary<string, string>
        {
            ["library.cjs"] = "exports.value = 7;\n" + declaration,
            ["value.cjs"] = "exports.value = 100;",
            ["main.cjs"] = "const api = require('./library.cjs');\n" + consumer,
        };

        AssertVerifiedProgram(files, expected);
    }

    [Fact]
    public void ClassMembersKeepDifferentModulesExportsIndependent()
    {
        const string declaration = """
            class Reader { read() { return exports.value; } }
            exports.Reader = Reader;
            """;
        var files = new Dictionary<string, string>
        {
            ["first.cjs"] = "exports.value = 7;\n" + declaration,
            ["second.cjs"] = "exports.value = 9;\n" + declaration,
            ["main.cjs"] = """
                const first = require('./first.cjs');
                const second = require('./second.cjs');
                console.log(new first.Reader().read());
                console.log(new second.Reader().read());
                console.log(new first.Reader().read());
                """,
        };

        AssertVerifiedProgram(files, "7\n9\n7\n");
    }

    [Theory]
    [InlineData("reduce", "[[\"*.js\"],[\"*.txt\",\"*.json\"]]\n")]
    [InlineData("reduceRight", "[[\"*.js\"],[\"*.json\",\"*.txt\"]]\n")]
    public void NestedReduceUsesItsOwnArrayReceiver(string method, string expected)
    {
        var source = $$"""
            function optimize(globParts) {
              return globParts.map(parts => parts.{{method}}((set, part) => {
                set.push(part);
                return set;
              }, []));
            }
            console.log(JSON.stringify(optimize([['*.js'], ['*.txt', '*.json']])));
            """;

        AssertVerifiedProgram(new Dictionary<string, string> { ["main.cjs"] = source }, expected);
    }

    [Fact]
    public void NestedReduceRestoresTheOuterReceiverAfterAnException()
    {
        const string source = """
            const observations = [];
            function recover(rows) {
              return rows.map(parts => {
                try {
                  parts.reduce((accumulator, part, index, receiver) => {
                    observations.push(receiver === parts);
                    throw new Error('stop');
                  }, 0);
                } catch (error) {
                  observations.push(error.message === 'stop');
                }
                return parts[0];
              });
            }
            console.log(JSON.stringify(recover([['a'], ['b']])));
            console.log(JSON.stringify(observations));
            """;

        AssertVerifiedProgram(new Dictionary<string, string> { ["main.cjs"] = source },
            "[\"a\",\"b\"]\n[true,true,true,true]\n");
    }

    private static void AssertVerifiedProgram(Dictionary<string, string> files, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        foreach (var (name, source) in files)
            directory.CreateFile(name, source);

        var entry = directory.GetPath("main.cjs");
        var interpreter = CliTestHelper.RunCli($"--no-tsconfig \"{entry}\"", directory.Path);
        Assert.True(interpreter.ExitCode == 0, interpreter.StandardOutput + interpreter.StandardError);
        Assert.Empty(interpreter.StandardError);
        Assert.Equal(expected, interpreter.StandardOutput);

        var output = directory.GetPath("main.dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{entry}\" -o \"{output}\" --verify", directory.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);

        var start = new ProcessStartInfo("dotnet", $"\"{output}\"")
        {
            WorkingDirectory = directory.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var run = TestProcess.Run(start, CliTestHelper.DefaultTimeout);
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Empty(run.StandardError);
        Assert.Equal(expected, CliTestHelper.NormalizeOutput(run.StandardOutput));
    }
}
