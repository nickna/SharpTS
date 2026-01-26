using SharpTS.Cli;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CliTests;

/// <summary>
/// Unit tests for CommandLineParser argument parsing.
/// </summary>
public class CommandLineParserTests
{
    private readonly CommandLineParser _parser = new();

    #region Help Flag Tests

    [Fact]
    public void Parse_Help_LongFlag_ReturnsHelp()
    {
        var result = _parser.Parse(["--help"]);
        Assert.IsType<ParsedCommand.Help>(result);
    }

    [Fact]
    public void Parse_Help_ShortFlag_ReturnsHelp()
    {
        var result = _parser.Parse(["-h"]);
        Assert.IsType<ParsedCommand.Help>(result);
    }

    #endregion

    #region Version Flag Tests

    [Fact]
    public void Parse_Version_LongFlag_ReturnsVersion()
    {
        var result = _parser.Parse(["--version"]);
        Assert.IsType<ParsedCommand.Version>(result);
    }

    [Fact]
    public void Parse_Version_ShortFlag_ReturnsVersion()
    {
        var result = _parser.Parse(["-v"]);
        Assert.IsType<ParsedCommand.Version>(result);
    }

    #endregion

    #region REPL Mode Tests

    [Fact]
    public void Parse_NoArgs_ReturnsRepl()
    {
        var result = _parser.Parse([]);
        Assert.IsType<ParsedCommand.Repl>(result);
    }

    [Fact]
    public void Parse_NoArgs_DefaultsToStage3Decorators()
    {
        var result = _parser.Parse([]);

        var repl = Assert.IsType<ParsedCommand.Repl>(result);
        Assert.Equal(DecoratorMode.Stage3, repl.Options.DecoratorMode);
    }

    [Fact]
    public void Parse_LegacyDecorators_SetsLegacyMode()
    {
        var result = _parser.Parse(["--experimentalDecorators"]);

        var repl = Assert.IsType<ParsedCommand.Repl>(result);
        Assert.Equal(DecoratorMode.Legacy, repl.Options.DecoratorMode);
    }

    [Fact]
    public void Parse_EmitDecoratorMetadata_SetsFlag()
    {
        var result = _parser.Parse(["--emitDecoratorMetadata"]);

        var repl = Assert.IsType<ParsedCommand.Repl>(result);
        Assert.True(repl.Options.EmitDecoratorMetadata);
    }

    #endregion

    #region Script Execution Tests

    [Fact]
    public void Parse_ScriptPath_ReturnsScriptCommand()
    {
        var result = _parser.Parse(["script.ts"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal("script.ts", script.ScriptPath);
        Assert.Empty(script.ScriptArgs);
    }

    [Fact]
    public void Parse_ScriptWithArgs_IncludesArgsInScriptArgs()
    {
        var result = _parser.Parse(["script.ts", "arg1", "arg2"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal("script.ts", script.ScriptPath);
        Assert.Equal(["arg1", "arg2"], script.ScriptArgs);
    }

    [Fact]
    public void Parse_ScriptWithDoubleDash_PassesFlagsAsScriptArgs()
    {
        var result = _parser.Parse(["script.ts", "--", "--flag", "value"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal("script.ts", script.ScriptPath);
        Assert.Equal(["--flag", "value"], script.ScriptArgs);
    }

    [Fact]
    public void Parse_ScriptWithGlobalOptions_AppliesOptions()
    {
        var result = _parser.Parse(["--experimentalDecorators", "script.ts"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal("script.ts", script.ScriptPath);
        Assert.Equal(DecoratorMode.Legacy, script.Options.DecoratorMode);
    }

    [Fact]
    public void Parse_ScriptWithArgsBeforeAndAfterDoubleDash_CombinesArgs()
    {
        var result = _parser.Parse(["script.ts", "arg1", "--", "arg2", "arg3"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal(["arg1", "arg2", "arg3"], script.ScriptArgs);
    }

    #endregion

    #region Compile Mode Tests

    [Fact]
    public void Parse_CompileLongFlag_ReturnsCompileCommand()
    {
        var result = _parser.Parse(["--compile", "file.ts"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal("file.ts", compile.InputFile);
        Assert.Equal("file.dll", compile.OutputFile);
    }

    [Fact]
    public void Parse_CompileShortFlag_ReturnsCompileCommand()
    {
        var result = _parser.Parse(["-c", "file.ts"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal("file.ts", compile.InputFile);
    }

    [Fact]
    public void Parse_Compile_CustomOutput_SetsOutputFile()
    {
        var result = _parser.Parse(["-c", "file.ts", "-o", "custom.dll"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal("file.ts", compile.InputFile);
        Assert.Equal("custom.dll", compile.OutputFile);
    }

    [Fact]
    public void Parse_Compile_TargetExe_SetsOutputTarget()
    {
        var result = _parser.Parse(["-c", "file.ts", "-t", "exe"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal(OutputTarget.Exe, compile.CompileOptions.Target);
        Assert.Equal("file.exe", compile.OutputFile);
    }

    [Fact]
    public void Parse_Compile_TargetDll_SetsOutputTarget()
    {
        var result = _parser.Parse(["-c", "file.ts", "--target", "dll"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal(OutputTarget.Dll, compile.CompileOptions.Target);
    }

    [Fact]
    public void Parse_Compile_AllPackageFlags_ParsesCorrectly()
    {
        var result = _parser.Parse([
            "-c", "file.ts",
            "--pack",
            "--push", "https://api.nuget.org/v3/index.json",
            "--api-key", "secret-key",
            "--package-id", "MyPackage",
            "--version", "2.0.0"
        ]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.True(compile.PackOptions.Pack);
        Assert.Equal("https://api.nuget.org/v3/index.json", compile.PackOptions.PushSource);
        Assert.Equal("secret-key", compile.PackOptions.ApiKey);
        Assert.Equal("MyPackage", compile.PackOptions.PackageIdOverride);
        Assert.Equal("2.0.0", compile.PackOptions.VersionOverride);
    }

    [Fact]
    public void Parse_Compile_PushImpliesPack()
    {
        var result = _parser.Parse(["-c", "file.ts", "--push", "https://nuget.org"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.True(compile.PackOptions.Pack);
    }

    [Fact]
    public void Parse_Compile_RepeatableReference_CollectsAll()
    {
        var result = _parser.Parse(["-c", "file.ts", "-r", "a.dll", "--reference", "b.dll", "-r", "c.dll"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal(["a.dll", "b.dll", "c.dll"], compile.CompileOptions.References);
    }

    [Fact]
    public void Parse_Compile_MissingInputFile_ReturnsError()
    {
        var result = _parser.Parse(["--compile"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("Missing input file", error.Message);
        Assert.Equal(64, error.ExitCode);
        Assert.True(error.ShowCompileUsage);
    }

    [Fact]
    public void Parse_Compile_InvalidTarget_ReturnsError()
    {
        var result = _parser.Parse(["-c", "file.ts", "-t", "invalid"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("Invalid target 'invalid'", error.Message);
        Assert.Equal(64, error.ExitCode);
    }

    [Fact]
    public void Parse_Compile_TargetMissingValue_ReturnsError()
    {
        var result = _parser.Parse(["-c", "file.ts", "-t"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("-t requires a value", error.Message);
        Assert.Equal(64, error.ExitCode);
    }

    [Fact]
    public void Parse_Compile_AllCompileOptions_ParsesCorrectly()
    {
        var result = _parser.Parse([
            "-c", "file.ts",
            "--preserveConstEnums",
            "--ref-asm",
            "--sdk-path", "/path/to/sdk",
            "--verify",
            "--msbuild-errors",
            "--quiet"
        ]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.True(compile.CompileOptions.PreserveConstEnums);
        Assert.True(compile.CompileOptions.UseReferenceAssemblies);
        Assert.Equal("/path/to/sdk", compile.CompileOptions.SdkPath);
        Assert.True(compile.CompileOptions.VerifyIL);
        Assert.True(compile.CompileOptions.MsBuildErrors);
        Assert.True(compile.CompileOptions.QuietMode);
    }

    [Fact]
    public void Parse_Compile_WithGlobalDecorators_AppliesGlobalOptions()
    {
        var result = _parser.Parse(["--experimentalDecorators", "-c", "file.ts"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal(DecoratorMode.Legacy, compile.GlobalOptions.DecoratorMode);
    }

    #endregion

    #region GenDecl Mode Tests

    [Fact]
    public void Parse_GenDecl_TypeName_ReturnsGenDeclCommand()
    {
        var result = _parser.Parse(["--gen-decl", "System.Console"]);

        var genDecl = Assert.IsType<ParsedCommand.GenDecl>(result);
        Assert.Equal("System.Console", genDecl.TypeOrAssembly);
        Assert.Null(genDecl.OutputPath);
    }

    [Fact]
    public void Parse_GenDecl_WithOutput_SetsOutputPath()
    {
        var result = _parser.Parse(["--gen-decl", "System.Console", "-o", "console.d.ts"]);

        var genDecl = Assert.IsType<ParsedCommand.GenDecl>(result);
        Assert.Equal("System.Console", genDecl.TypeOrAssembly);
        Assert.Equal("console.d.ts", genDecl.OutputPath);
    }

    [Fact]
    public void Parse_GenDecl_AssemblyPath_ReturnsGenDeclCommand()
    {
        var result = _parser.Parse(["--gen-decl", "./MyAssembly.dll"]);

        var genDecl = Assert.IsType<ParsedCommand.GenDecl>(result);
        Assert.Equal("./MyAssembly.dll", genDecl.TypeOrAssembly);
    }

    [Fact]
    public void Parse_GenDecl_MissingArg_ReturnsError()
    {
        var result = _parser.Parse(["--gen-decl"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("Usage:", error.Message);
        Assert.Equal(64, error.ExitCode);
    }

    #endregion

    #region LspBridge Mode Tests

    [Fact]
    public void Parse_LspBridge_NoOptions_ReturnsLspBridgeCommand()
    {
        var result = _parser.Parse(["lsp-bridge"]);

        var lspBridge = Assert.IsType<ParsedCommand.LspBridge>(result);
        Assert.Null(lspBridge.ProjectFile);
        Assert.Empty(lspBridge.References);
        Assert.Null(lspBridge.SdkPath);
    }

    [Fact]
    public void Parse_LspBridge_WithProject_SetsProjectFile()
    {
        var result = _parser.Parse(["lsp-bridge", "--project", "myapp.csproj"]);

        var lspBridge = Assert.IsType<ParsedCommand.LspBridge>(result);
        Assert.Equal("myapp.csproj", lspBridge.ProjectFile);
    }

    [Fact]
    public void Parse_LspBridge_WithReferences_CollectsAll()
    {
        var result = _parser.Parse(["lsp-bridge", "-r", "a.dll", "--reference", "b.dll"]);

        var lspBridge = Assert.IsType<ParsedCommand.LspBridge>(result);
        Assert.Equal(["a.dll", "b.dll"], lspBridge.References);
    }

    [Fact]
    public void Parse_LspBridge_WithSdkPath_SetsSdkPath()
    {
        var result = _parser.Parse(["lsp-bridge", "--sdk-path", "/path/to/sdk"]);

        var lspBridge = Assert.IsType<ParsedCommand.LspBridge>(result);
        Assert.Equal("/path/to/sdk", lspBridge.SdkPath);
    }

    [Fact]
    public void Parse_LspBridge_AllOptions_ParsesCorrectly()
    {
        var result = _parser.Parse([
            "lsp-bridge",
            "--project", "app.csproj",
            "-r", "lib.dll",
            "--sdk-path", "/sdk"
        ]);

        var lspBridge = Assert.IsType<ParsedCommand.LspBridge>(result);
        Assert.Equal("app.csproj", lspBridge.ProjectFile);
        Assert.Single(lspBridge.References);
        Assert.Equal("lib.dll", lspBridge.References[0]);
        Assert.Equal("/sdk", lspBridge.SdkPath);
    }

    #endregion

    #region Error Cases Tests

    [Fact]
    public void Parse_UnknownFlag_ReturnsError()
    {
        var result = _parser.Parse(["--unknown"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("Unknown option '--unknown'", error.Message);
        Assert.Equal(64, error.ExitCode);
    }

    [Fact]
    public void Parse_UnknownShortFlag_ReturnsError()
    {
        var result = _parser.Parse(["-x"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("Unknown option '-x'", error.Message);
    }

    [Fact]
    public void Parse_HelpAfterOtherArgs_NotTreatedAsHelp()
    {
        // --help only works as the first argument
        var result = _parser.Parse(["script.ts", "--help"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal("script.ts", script.ScriptPath);
        Assert.Contains("--help", script.ScriptArgs);
    }

    [Fact]
    public void Parse_VersionAfterOtherArgs_NotTreatedAsVersion()
    {
        var result = _parser.Parse(["script.ts", "--version"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Contains("--version", script.ScriptArgs);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_DoubleDashWithNoScriptArgs_ReturnsEmptyScriptArgs()
    {
        var result = _parser.Parse(["script.ts", "--"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Empty(script.ScriptArgs);
    }

    [Fact]
    public void Parse_MultipleDecoratorFlags_LastOneWins()
    {
        var result = _parser.Parse(["--experimentalDecorators", "--noDecorators"]);

        var repl = Assert.IsType<ParsedCommand.Repl>(result);
        Assert.Equal(DecoratorMode.None, repl.Options.DecoratorMode);
    }

    [Fact]
    public void Parse_OutputTarget_CaseInsensitive()
    {
        var result = _parser.Parse(["-c", "file.ts", "-t", "DLL"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal(OutputTarget.Dll, compile.CompileOptions.Target);
    }

    [Fact]
    public void Parse_OutputTarget_ExeUppercase()
    {
        var result = _parser.Parse(["-c", "file.ts", "-t", "EXE"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.Equal(OutputTarget.Exe, compile.CompileOptions.Target);
    }

    #endregion
}
