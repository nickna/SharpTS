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
    public void Parse_CompileTimings_SetsReadableTimingMode()
    {
        var compile = Assert.IsType<ParsedCommand.Compile>(
            _parser.Parse(["-c", "file.ts", "--timings"]));

        Assert.True(compile.CompileOptions.Timings);
        Assert.False(compile.CompileOptions.TimingsJson);
    }

    [Fact]
    public void Parse_CompileTimingsJson_SetsJsonTimingMode()
    {
        var compile = Assert.IsType<ParsedCommand.Compile>(
            _parser.Parse(["-c", "file.ts", "--timings-json"]));

        Assert.False(compile.CompileOptions.Timings);
        Assert.True(compile.CompileOptions.TimingsJson);
    }

    [Theory]
    [InlineData("-c", "file.ts", "--timings", "--timings-json")]
    [InlineData("-c", "file.ts", "--timings", "--showConfig")]
    [InlineData("-c", "file.ts", "--timings-json", "--showConfig")]
    public void Parse_CompileTimingConflicts_ReturnUsageError(params string[] args)
    {
        var error = Assert.IsType<ParsedCommand.Error>(_parser.Parse(args));

        Assert.Equal(64, error.ExitCode);
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
    public void Parse_Compile_Hosted_RequiresDllAndSetsHiddenOption()
    {
        var result = _parser.Parse(["-c", "file.ts", "--hosted"]);

        var compile = Assert.IsType<ParsedCommand.Compile>(result);
        Assert.True(compile.CompileOptions.Hosted);

        var invalid = _parser.Parse([
            "-c", "file.ts", "--hosted", "--target", "exe"]);
        var error = Assert.IsType<ParsedCommand.Error>(invalid);
        Assert.Contains("valid only with --target dll", error.Message);
        Assert.Equal(64, error.ExitCode);
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
    public void Parse_GenDecl_JsonFlag_SetsJson()
    {
        var result = _parser.Parse(["--gen-decl", "System.Guid", "--json"]);

        var genDecl = Assert.IsType<ParsedCommand.GenDecl>(result);
        Assert.Equal("System.Guid", genDecl.TypeOrAssembly);
        Assert.True(genDecl.Json);
    }

    [Fact]
    public void Parse_GenDecl_DefaultsJsonFalse()
    {
        var result = _parser.Parse(["--gen-decl", "System.Guid"]);

        var genDecl = Assert.IsType<ParsedCommand.GenDecl>(result);
        Assert.False(genDecl.Json);
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
    public void Parse_ProjectWithoutScript_ReturnsProjectCommand()
    {
        var result = _parser.Parse(["-p", "tsconfig.json", "--incremental"]);

        var project = Assert.IsType<ParsedCommand.Project>(result);
        Assert.Equal("tsconfig.json", project.Options.ProjectPath);
        Assert.True(project.Options.Incremental);
    }

    [Fact]
    public void Parse_BuildCollectsProjectsAndFlags()
    {
        var result = _parser.Parse(["--build", "packages/a", "packages/b", "--force"]);

        var build = Assert.IsType<ParsedCommand.Build>(result);
        Assert.Equal(["packages/a", "packages/b"], build.ProjectPaths);
        Assert.True(build.Options.Force);
    }

    [Fact]
    public void Parse_BuildDefaultsToCurrentDirectory()
    {
        var result = _parser.Parse(["-b"]);

        var build = Assert.IsType<ParsedCommand.Build>(result);
        Assert.Equal(["."], build.ProjectPaths);
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

    [Fact]
    public void Parse_JsxOptions()
    {
        var result = _parser.Parse([
            "--jsx", "react",
            "--jsxFactory=h",
            "--jsxFragmentFactory", "HFragment",
            "--jsxImportSource=preact",
            "app.tsx",
        ]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal(JsxMode.React, script.Options.Jsx);
        Assert.Equal("h", script.Options.JsxFactory);
        Assert.Equal("HFragment", script.Options.JsxFragmentFactory);
        Assert.Equal("preact", script.Options.JsxImportSource);
        Assert.Equal(JsxMode.React, script.Options.ResolvedJsxOptions.Mode);
        Assert.Equal("h", script.Options.ResolvedJsxOptions.Factory);
    }

    [Fact]
    public void Parse_JsxDefaultsToAutomaticRuntime()
    {
        var result = _parser.Parse(["app.tsx"]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Null(script.Options.Jsx);
        var resolved = script.Options.ResolvedJsxOptions;
        Assert.Equal(JsxMode.ReactJsx, resolved.Mode);
        Assert.Equal("React.createElement", resolved.Factory);
        Assert.Equal("React.Fragment", resolved.FragmentFactory);
        Assert.Equal("react", resolved.ImportSource);
    }

    [Fact]
    public void Parse_JsxPreserveIsRejectedWithEmitExplanation()
    {
        var result = _parser.Parse(["--jsx", "preserve", "app.tsx"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("cannot emit .jsx output", error.Message);
    }

    [Fact]
    public void Parse_JsxUnknownModeIsRejected()
    {
        var result = _parser.Parse(["--jsx", "vue", "app.tsx"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("react-jsx", error.Message);
    }

    [Fact]
    public void Parse_JsxWithoutValueIsRejected()
    {
        var result = _parser.Parse(["--jsx"]);

        var error = Assert.IsType<ParsedCommand.Error>(result);
        Assert.Contains("--jsx requires a mode", error.Message);
    }

    [Fact]
    public void Parse_TypeScriptProgramOptions()
    {
        var result = _parser.Parse([
            "--lib", "ES2022,DOM",
            "--types=node,jest",
            "--typeRoots", "./types,./vendor-types",
            "--noLib=false",
            "app.ts",
        ]);

        var script = Assert.IsType<ParsedCommand.Script>(result);
        Assert.Equal(["ES2022", "DOM"], script.Options.Lib);
        Assert.Equal(["node", "jest"], script.Options.Types);
        Assert.Equal(["./types", "./vendor-types"], script.Options.TypeRoots);
        Assert.False(script.Options.NoLib);
        Assert.True(script.Options.TypeScriptProgramOptions.LoadDefaultLib);
        Assert.True(script.Options.TypeScriptProgramOptions.PreferDeclarationFiles);
    }

    [Fact]
    public void Parse_NewAvaloniaApplication()
    {
        var command = Assert.IsType<ParsedCommand.NewAvalonia>(
            _parser.Parse(["new", "avalonia", "-n", "Counter", "-o", "apps/Counter"]));
        Assert.Equal("Counter", command.Name);
        Assert.Equal("apps/Counter", command.OutputDirectory);
        Assert.Equal("0.2.0-preview.1", command.GuiSdkVersion);
    }

    [Fact]
    public void Parse_ApplicationPublishKeepsDeploymentConceptsSeparate()
    {
        var command = Assert.IsType<ParsedCommand.Application>(_parser.Parse([
            "app", "publish", "main.tsx", "--host", "avalonia", "--rid", "win-x64",
            "--self-contained", "true", "--single-file", "false", "--source", "feed", "-o", "dist",
        ]));
        Assert.Equal("publish", command.Action);
        Assert.Equal("avalonia", command.Host);
        Assert.Equal("win-x64", command.RuntimeIdentifier);
        Assert.True(command.SelfContained);
        Assert.False(command.SingleFile);
        Assert.Equal("feed", command.GuiSdkSource);
        Assert.Equal("dist", command.OutputDirectory);
    }

    [Fact]
    public void Parse_ApplicationRunForwardsOnlyArgumentsAfterSeparator()
    {
        var command = Assert.IsType<ParsedCommand.Application>(_parser.Parse([
            "app", "run", "main.tsx", "--mode", "compiled", "--", "--headless", "value",
        ]));
        Assert.Equal("compiled", command.Mode);
        Assert.Equal(["--headless", "value"], command.ApplicationArgs);
    }

    [Theory]
    [InlineData("--self-contained")]
    [InlineData("--single-file")]
    public void Parse_ApplicationRejectsMissingDeploymentBoolean(string option)
    {
        var error = Assert.IsType<ParsedCommand.Error>(_parser.Parse(["app", "publish", option]));
        Assert.Equal(64, error.ExitCode);
    }

    #endregion
}
