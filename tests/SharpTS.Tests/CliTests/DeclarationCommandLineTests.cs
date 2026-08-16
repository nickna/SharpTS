using SharpTS.Cli;
using Xunit;

namespace SharpTS.Tests.CliTests;

public class DeclarationCommandLineTests
{
    private readonly CommandLineParser _parser = new();

    [Fact]
    public void ParseCompileDeclarationOptions()
    {
        var result = _parser.Parse([
            "--declaration",
            "--declarationDir=types",
            "--compile",
            "library.ts",
        ]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.True(compile.GlobalOptions.Declaration);
        Assert.False(compile.GlobalOptions.EmitDeclarationOnly);
        Assert.Equal("types", compile.GlobalOptions.DeclarationDir);
    }

    [Fact]
    public void EmitDeclarationOnlyImpliesDeclaration()
    {
        var result = _parser.Parse(["--emitDeclarationOnly", "--compile", "library.ts"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.True(compile.GlobalOptions.Declaration);
        Assert.True(compile.GlobalOptions.EmitDeclarationOnly);
    }

    [Fact]
    public void NoEmitConflictsWithEmitDeclarationOnly()
    {
        var result = _parser.Parse([
            "--noEmit",
            "--emitDeclarationOnly",
            "--compile",
            "library.ts",
        ]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("cannot be combined", error.Message);
    }

    [Fact]
    public void DeclarationOptionsAreRejectedForScriptExecution()
    {
        var result = _parser.Parse(["--declaration", "library.ts"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("require --compile or -p/--project", error.Message);
    }
}
