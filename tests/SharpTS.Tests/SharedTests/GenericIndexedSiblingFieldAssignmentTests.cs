using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericIndexedSiblingFieldAssignmentTests
{
    [Theory, ModeData]
    public void MatchingTypeParameterAssignmentsPassStrictChecking(ExecutionMode mode)
    {
        var source = """
            class Parent { other: number = 3; }
            class Child extends Parent { value: Child['other'] = 4; }
            class Box<T> {
                other: T;
                value: Box<T>['other'];
                constructor(value: T) { this.other = value; this.value = value; }
            }
            const child = new Child();
            const numeric = new Box<number>(5);
            const text = new Box<string>('text');
            console.log(child.other, child.value, numeric.other, numeric.value, text.other, text.value);
            """.ReplaceLineEndings("\n") + "\n";
        const string expected = "3 4 5 5 text text\n";
        // Use the reported integrated checked-file path, whose generic binding differs
        // from the standalone checker API. A rejected source must never execute.
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var checkedRun = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib \"{sourcePath}\"",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(checkedRun.ExitCode == 0, checkedRun.StandardOutput + checkedRun.StandardError);
        Assert.Empty(checkedRun.StandardError);
        Assert.Equal(expected, checkedRun.StandardOutput);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
