using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliArraySubclassTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclarationLibrariesPreserveArraySubclassRuntime(bool noLib)
    {
        const string source = """
            class Typed extends Array<number> {}
            class Plain extends Array {}
            const typed = new Typed(3);
            typed[1] = 7;
            const plain: any = new Plain("a", "b");
            console.log(typed.length, 0 in typed, typed[1]);
            console.log(plain.length, plain[0], plain[1]);
            console.log(typed instanceof Typed, typed instanceof Array, Array.isArray(typed));
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig{(noLib ? " --noLib" : "")} \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("3 false 7\n2 a b\ntrue true true\n", result.StandardOutput);
        Assert.Equal(result.StandardOutput, TestHarness.RunInterpreted(source));
        Assert.Equal(result.StandardOutput, TestHarness.RunCompiled(source));
    }

    [Fact]
    public void DeclarationLibrariesPreserveGenericArrayClassExpressionHeritage()
    {
        const string source = """
            const Child = class extends Array<number> {};
            const value = new Child();
            value.push(7);
            const item: number = value[0];
            for (const element of value) {
                const typed: number = element;
                console.log(typed, item, Array.isArray(value));
            }
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("7 7 true\n", result.StandardOutput);
    }

    [Theory]
    [InlineData("class Child<T> extends Array<T> {}", false)]
    [InlineData("class Child<T> extends Array<T> {}", true)]
    [InlineData("const Child = class<T> extends Array<T> {};", false)]
    [InlineData("const Child = class<T> extends Array<T> {};", true)]
    public void NumericReadsUseTheInstantiatedArrayElementType(string declaration, bool noLib)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", $$"""
            {{declaration}}
            const value = new Child<number>();
            value.push(7);
            const item: number = value[0];
            console.log(item);
            """);
        string options = $"--no-tsconfig{(noLib ? " --noLib" : "")} --strict";
        var run = CliTestHelper.RunCli($"{options} \"{path}\"", directory.Path);
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Equal("7\n", run.StandardOutput);

        var invalid = directory.CreateFile("invalid.ts",
            $"{declaration} const wrong: string = new Child<number>()[0];");
        var result = CliTestHelper.RunCli($"{options} --noEmit \"{invalid}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not assignable to type 'string'", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void NumericReadsComposeTransitiveArrayTypeArguments()
    {
        const string source = """
            class Base<T> extends Array<T> {}
            class Child<U> extends Base<U[]> {}
            const value = new Child<number>();
            const item: number[] = value[0];
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);

        var invalid = directory.CreateFile("invalid.ts", source + "\nconst wrong: string[] = value[0];");
        result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{invalid}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not assignable to type 'string[]'", result.StandardOutput + result.StandardError);
    }

    [Theory]
    [InlineData("declare const value: Child<number> | Child<string>; const item: number | string = value[0];",
        "const wrong: number = value[0];", "number")]
    [InlineData("function read<T extends Child<number>>(value: T): number { return value[0]; }",
        "function wrong<T extends Child<number>>(value: T): string { return value[0]; }", "string")]
    public void DelegatedNumericReadsPreserveArrayElementTypes(string source, string incompatible, string target)
    {
        const string declaration = "class Child<T> extends Array<T> {}\n";
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", declaration + source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);

        var invalid = directory.CreateFile("invalid.ts", declaration + source + "\n" + incompatible);
        result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{invalid}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains($"not assignable to type '{target}'", result.StandardOutput + result.StandardError);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("'0'", true)]
    [InlineData("index", true)]
    public void GenericArraySubclassChecksNumericWrites(string index, bool noLib)
    {
        string source = $$"""
            class Parent<T> extends Array<T> {}
            class Child<U> extends Parent<U> {}
            declare const index: number;
            const value = new Child<number>();
            value[{{index}}] = 42;
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        string options = $"--no-tsconfig --strict --noEmit {(noLib ? "--noLib" : "")}";
        var path = directory.CreateFile("valid.ts", source);
        var result = CliTestHelper.RunCli($"{options} \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);

        var invalid = directory.CreateFile("invalid.ts", source + $"\nvalue[{index}] = 'wrong';");
        result = CliTestHelper.RunCli($"{options} \"{invalid}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("array element type 'number'", result.StandardOutput + result.StandardError);
    }

    [Theory]
    [InlineData("const Child = class extends Array<number> {}; const value = new Child();")]
    [InlineData("const Child = class<T> extends Array<T> {}; const value = new Child<number>();")]
    public void DeclarationLibrariesKeepTheElementTypeInInheritedArrayMethods(string declaration)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var validPath = directory.CreateFile("valid.ts",
            $"{declaration} const length: number = value.push(); value.push(7);");
        var valid = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{validPath}\"", directory.Path);
        Assert.True(valid.ExitCode == 0, valid.StandardOutput + valid.StandardError);

        var path = directory.CreateFile("main.ts", $"{declaration} value.push(7); value.push(\"wrong\");");
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{path}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Argument 1 expected type 'number' but got", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void AShadowingArrayClassKeepsItsDeclaredMembers()
    {
        const string source = """
            function check() {
                class Array { push(value: string): string { return value; } }
                const Child = class extends Array {};
                const right: string = new Child().push("value");
                const wrong: number = new Child().push("value");
            }
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{path}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not assignable to type 'number'", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void QualifiedAnyArraySuperclassKeepsUnknownMembers()
    {
        const string source = """
            const X: any = { Array: class {} };
            class Child<T> extends X.Array {}
            const value = new Child<number>();
            const length: string = value.length;
            value.push("value");
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noEmit \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    [Theory]
    [InlineData("function check() { const Array = 1; class Values extends Array {} }", "Superclass must be a class")]
    [InlineData("class Array {} class Values extends Array<number> {}", "Cannot use type arguments")]
    [InlineData("class Values extends Array<number, string> {}", "No base constructor")]
    [InlineData("class Array {} const Values = class extends Array<number> {};", "Cannot use type arguments")]
    [InlineData("const Values = class extends Array<number, string> {};", "No base constructor")]
    public void LoadedArrayBridgePreservesShadowingAndTypeArgumentChecks(string source, string diagnostic)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile \"{path}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(diagnostic, result.StandardOutput + result.StandardError);
    }
}
