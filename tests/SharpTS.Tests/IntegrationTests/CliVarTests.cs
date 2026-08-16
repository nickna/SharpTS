using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end tests for the <c>var</c> keyword and JavaScript var-hoisting semantics.
/// Covers both interpreter and compiled modes.
/// </summary>
public class CliVarTests
{
    private static (int ExitCode, string StdOut) CompileAndRun(
        TempTestDirectory tempDir, string entryFile)
    {
        var compile = CliTestHelper.RunCli($"-c \"{entryFile}\"", tempDir.Path);
        if (compile.ExitCode != 0)
        {
            return (compile.ExitCode, compile.StandardOutput + compile.StandardError);
        }

        var dllName = Path.GetFileNameWithoutExtension(entryFile) + ".dll";
        var dllPath = tempDir.GetPath(dllName);
        Assert.True(File.Exists(dllPath), $"Expected output DLL at {dllPath}");

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = tempDir.Path
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut + stdErr);
    }

    [Fact]
    public void Interpreted_VarBasic()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("basic.cjs", """
            var a = 1;
            var b = "hello";
            console.log(a, b);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 hello", result.StandardOutput);
    }

    [Fact]
    public void Interpreted_VarMultiDeclarator()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("multi.cjs", """
            var a = 1, b = 2, c = a + b;
            console.log(a, b, c);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 2 3", result.StandardOutput);
    }

    [Fact]
    public void Interpreted_LetMultiDeclarator()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("let-multi.cjs", """
            let a = 10, b = 20, c = 30;
            console.log(a + b + c);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("60", result.StandardOutput);
    }

    [Fact]
    public void Interpreted_VarHoistedFromNestedBlock()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("hoist.ts", """
            function test(): number {
              if (true) {
                var inner = 42;
              }
              return inner;
            }
            console.log(test());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("42", result.StandardOutput);
    }

    [Fact]
    public void Interpreted_VarHoistedFromForLoop()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("for-hoist.ts", """
            function sum(): number {
              var total = 0;
              for (var i = 0; i < 5; i++) {
                total = total + i;
              }
              return total + i;
            }
            console.log(sum());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        // 0+1+2+3+4 = 10, plus i=5 after loop = 15
        Assert.Contains("15", result.StandardOutput);
    }

    [Fact]
    public void Interpreted_VarReDeclarationInDifferentBlocks()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("redecl.ts", """
            function pick(useA: boolean): string {
              if (useA) {
                var x = "a-branch";
              } else {
                var x = "b-branch";
              }
              return x;
            }
            console.log(pick(true));
            console.log(pick(false));
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("a-branch", result.StandardOutput);
        Assert.Contains("b-branch", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarBasic()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("basic.cjs", """
            var a = 1;
            var b = "hello";
            console.log(a, b);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("1 hello", output);
    }

    [Fact]
    public void Compiled_VarHoistedFromNestedBlock()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("hoist.ts", """
            function test(): number {
              if (true) {
                var inner = 42;
              }
              return inner;
            }
            console.log(test());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("42", output);
    }

    [Fact]
    public void Compiled_VarMultiDeclarator()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("multi.cjs", """
            var a = 1, b = 2, c = a + b;
            console.log(a, b, c);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("1 2 3", output);
    }

    // ── Arrow function var hoisting (issue #19) ──────────────────────

    [Fact]
    public void Interpreted_VarHoistInArrowFunction()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("arrow-hoist.cjs", """
            const fn = () => {
              if (true) {
                var inner = 42;
              }
              return inner;
            };
            console.log(fn());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("42", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarHoistInArrowFunction()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("arrow-hoist.cjs", """
            const fn = () => {
              if (true) {
                var inner = 42;
              }
              return inner;
            };
            console.log(fn());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("42", output);
    }

    [Fact]
    public void Interpreted_VarHoistInFunctionExpression()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("funcexpr-hoist.cjs", """
            const fn = function() {
              if (true) {
                var inner = 99;
              }
              return inner;
            };
            console.log(fn());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("99", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarHoistInFunctionExpression()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("funcexpr-hoist.cjs", """
            const fn = function() {
              if (true) {
                var inner = 99;
              }
              return inner;
            };
            console.log(fn());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("99", output);
    }

    [Fact]
    public void Interpreted_VarHoistInObjectMethod()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("method-hoist.cjs", """
            const obj = {
              test() {
                if (true) { var x = 7; }
                return x;
              }
            };
            console.log(obj.test());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("7", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarHoistInObjectMethod()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("method-hoist.cjs", """
            const obj = {
              test() {
                if (true) { var x = 7; }
                return x;
              }
            };
            console.log(obj.test());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("7", output);
    }

    // ── var redeclaration in same scope (issue #20) ─────────────────

    [Fact]
    public void Interpreted_VarRedeclarationTopLevel()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("redecl-top.cjs", """
            var x = 1;
            var x = 2;
            console.log(x);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarRedeclarationTopLevel()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("redecl-top.cjs", """
            var x = 1;
            var x = 2;
            console.log(x);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("2", output);
    }

    [Fact]
    public void Interpreted_VarRedeclarationInFunction()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("redecl-func.ts", """
            function foo(): number {
              var x = 1;
              var x = 2;
              return x;
            }
            console.log(foo());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("2", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarRedeclarationInFunction()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("redecl-func.ts", """
            function foo(): number {
              var x = 1;
              var x = 2;
              return x;
            }
            console.log(foo());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("2", output);
    }

    [Fact]
    public void Interpreted_VarRedeclarationWithoutInitializer()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("redecl-noinit.cjs", """
            var y = "hello";
            console.log(y);
            var y;
            console.log(y);
            var y = "world";
            console.log(y);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StandardOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("hello", lines[0].Trim());
        Assert.Equal("hello", lines[1].Trim());
        Assert.Equal("world", lines[2].Trim());
    }

    [Fact]
    public void Compiled_VarRedeclarationWithoutInitializer()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("redecl-noinit.cjs", """
            var y = "hello";
            console.log(y);
            var y;
            console.log(y);
            var y = "world";
            console.log(y);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("hello", lines[0].Trim());
        Assert.Equal("hello", lines[1].Trim());
        Assert.Equal("world", lines[2].Trim());
    }

    [Fact]
    public void Interpreted_VarRedeclarationAnnotationOnly_PreservesValue()
    {
        // Issue #336: an annotation-only duplicate of a matching type compiles to a self-assign
        // (`z = z`) — a runtime no-op that must NOT reset the existing binding to undefined.
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("redecl-anno.ts", """
            var y: string = "hello";
            console.log(y);
            var y: string;
            console.log(y);
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StandardOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("hello", lines[0].Trim());
        Assert.Equal("hello", lines[1].Trim());
    }

    [Fact]
    public void Compiled_VarRedeclarationAnnotationOnly_PreservesValue()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("redecl-anno.ts", """
            var y: string = "hello";
            console.log(y);
            var y: string;
            console.log(y);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("hello", lines[0].Trim());
        Assert.Equal("hello", lines[1].Trim());
    }

    [Fact]
    public void Interpreted_VarRedeclarationMixedTopAndNested()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var script = tempDir.CreateFile("redecl-mixed.ts", """
            function test(): string {
              var x = "top";
              if (true) {
                var x = "nested";
              }
              return x;
            }
            console.log(test());
            """);

        var result = CliTestHelper.RunCli($"\"{script}\"", tempDir.Path);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("nested", result.StandardOutput);
    }

    [Fact]
    public void Compiled_VarRedeclarationMixedTopAndNested()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("redecl-mixed.ts", """
            function test(): string {
              var x = "top";
              if (true) {
                var x = "nested";
              }
              return x;
            }
            console.log(test());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("nested", output);
    }
}
