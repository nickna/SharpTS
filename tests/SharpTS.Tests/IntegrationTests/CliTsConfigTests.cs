using System.Text.Json;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end coverage for tsconfig.json discovery and the strictness flags, driving the real
/// CLI process. Template: <see cref="CliManifestTests"/>.
/// </summary>
/// <remarks>
/// Errors reach the user through <c>Console.WriteLine</c>, so assertions check
/// <c>StandardOutput</c>, not stderr. Paths stay relative to the temp workspace because
/// <see cref="CliTestHelper.RunCli"/> passes arguments as one space-split string.
/// </remarks>
public class CliTsConfigTests
{
    private const string NullAssignment = "let x: number = null;\nconsole.log(\"ran\");\n";

    #region Discovery and precedence

    [Fact]
    public void NoTsConfig_KeepsProductDefaults()
    {
        // The regression canary: strictNullChecks is on by default, with or without this feature.
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not assignable", result.StandardOutput);
    }

    [Fact]
    public void TsConfig_StrictNullChecksFalse_IsHonored()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ran\n", result.StandardOutput);
    }

    [Fact]
    public void TsConfig_IsDiscoveredByUpwardWalkFromTheEntryScript()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("src/app/entry.ts", NullAssignment);

        var result = CliTestHelper.RunCli("src/app/entry.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ran\n", result.StandardOutput);
    }

    [Fact]
    public void CliFlag_BeatsTsConfig()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("--strictNullChecks main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void CliExplicitFalse_BeatsTsConfigTrue()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strict": true } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("--strictNullChecks=false main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void NoTsConfigFlag_SuppressesDiscovery()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("--no-tsconfig main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void ExtendsChain_IsFollowed()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("base.json", """{ "compilerOptions": { "strict": true } }""");
        dir.CreateFile("tsconfig.json",
            """{ "extends": "./base.json", "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode); // the deriving file wins
    }

    #endregion

    #region -p / --project

    [Fact]
    public void Project_AcceptsAFilePath()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("configs/loose.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("-p configs/loose.json main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Project_AcceptsADirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("configs/tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("--project configs main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Project_MissingPath_ExitsNonzeroNamingIt()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("-p nope.json main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("nope.json", result.StandardOutput);
        Assert.Contains("does not exist", result.StandardOutput);
    }

    #endregion

    #region Malformed and unknown

    [Fact]
    public void MalformedTsConfig_ExitsNonzeroNamingTheFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", "{ not json ");
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("tsconfig.json", result.StandardOutput);
        Assert.Contains("not valid JSON", result.StandardOutput);
    }

    [Fact]
    public void UnknownCompilerOption_WarnsButStillRuns()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNulChecks": false } }""");
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode); // warnings never change the exit code
        Assert.Contains("Did you mean 'strictNullChecks'?", result.StandardOutput);
        Assert.Contains("ran", result.StandardOutput);
    }

    [Fact]
    public void EmitOptions_AreAcceptedSilently()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """
            { "compilerOptions": { "target": "ES2020", "module": "ESNext", "sourceMap": true } }
            """);
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ran\n", result.StandardOutput);
    }

    #endregion

    #region --showConfig

    [Fact]
    public void ShowConfig_EmitsParseableJsonWithProvenance()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("--showConfig main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.StandardOutput);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("compilerOptions").GetProperty("strictNullChecks").GetBoolean());
        Assert.Equal("tsconfig",
            root.GetProperty("sharpts").GetProperty("provenance").GetProperty("strictNullChecks").GetString());
        Assert.Contains("tsconfig.json", root.GetProperty("sharpts").GetProperty("configFile").GetString()!);
        Assert.DoesNotContain("ran", result.StandardOutput); // the program must not run
    }

    [Fact]
    public void ShowConfig_WithoutAnyTsConfig_StillReports()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("--showConfig main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        using var doc = JsonDocument.Parse(result.StandardOutput);
        var sharpts = doc.RootElement.GetProperty("sharpts");

        Assert.Equal(JsonValueKind.Null, sharpts.GetProperty("configFile").ValueKind);
        Assert.Equal("default", sharpts.GetProperty("provenance").GetProperty("strictNullChecks").GetString());
    }

    #endregion

    #region --noEmit

    [Fact]
    public void NoEmit_CleanProgram_ExitsZeroWithoutRunning()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("--noEmit main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("ran", result.StandardOutput);
    }

    [Fact]
    public void NoEmit_TypeError_ExitsNonzero()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", NullAssignment);

        var result = CliTestHelper.RunCli("--noEmit main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void NoEmit_InCompileMode_ProducesNoAssembly()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", "console.log(\"ran\");");

        var result = CliTestHelper.RunCli("--compile main.ts --noEmit", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(dir.GetPath("main.dll")));
    }

    #endregion

    #region noImplicitAny end-to-end

    [Fact]
    public void NoImplicitAny_FromTsConfig_ReportsDeclaredFunctionParameters()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "noImplicitAny": true } }""");
        dir.CreateFile("main.ts", "function f(x) { return x; }\nconsole.log(f(1));");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("implicitly has an 'any' type", result.StandardOutput);
    }

    [Fact]
    public void NoImplicitAny_DoesNotFireOnCallbacks()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strict": true } }""");
        dir.CreateFile("main.ts", "const a = [1, 2, 3];\nconsole.log(a.map(x => x * 2).length);");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3\n", result.StandardOutput);
    }

    #endregion
}
