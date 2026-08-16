using SharpTS.Cli;
using SharpTS.Configuration;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CliTests;

/// <summary>
/// Parsing and resolution of the tsc-compatible strictness flags, <c>--noEmit</c>, and the two
/// flag-handling bugs fixed alongside them (<c>--decorators</c> being unhandled, and compile
/// mode silently swallowing unknown flags).
/// </summary>
/// <remarks>
/// Kept separate from <see cref="CommandLineParserTests"/> so the pre-existing coverage stays
/// untouched as a regression witness.
/// </remarks>
public class CommandLineParserStrictnessTests
{
    private readonly CommandLineParser _parser = new();

    private GlobalOptions ParseScriptOptions(params string[] args)
    {
        var result = _parser.Parse(args);
        return Assert.IsType<ParsedCommand.Script>(result).Options;
    }

    #region Defaults — the zero-regression guarantee at the CLI boundary

    [Fact]
    public void NoFlags_LeavesEveryStrictnessKeyUnset()
    {
        var options = ParseScriptOptions("script.ts");

        Assert.True(options.Strictness.IsEmpty);
        Assert.False(options.NoEmit);
    }

    [Fact]
    public void NoFlags_ResolveToProductDefaults()
    {
        var options = ParseScriptOptions("script.ts");

        Assert.Equal(TypeCheckerOptions.Default, options.TypeCheckerOptions);
    }

    #endregion

    #region Individual flags

    [Theory]
    [InlineData("--strictNullChecks")]
    [InlineData("--strictFunctionTypes")]
    [InlineData("--noImplicitAny")]
    [InlineData("--strict")]
    public void BareFlag_MeansTrue(string flag)
    {
        var s = ParseScriptOptions(flag, "script.ts").Strictness;

        Assert.False(s.IsEmpty);
        Assert.All(new[] { s.Strict, s.StrictNullChecks, s.StrictFunctionTypes, s.NoImplicitAny }
                       .Where(v => v is not null),
                   v => Assert.True(v));
    }

    [Fact]
    public void StrictNullChecksFalse_IsDistinctFromAbsent()
    {
        var explicitlyOff = ParseScriptOptions("--strictNullChecks=false", "script.ts").Strictness;
        var absent = ParseScriptOptions("script.ts").Strictness;

        Assert.False(explicitlyOff.StrictNullChecks);   // explicitly false
        Assert.Null(absent.StrictNullChecks);           // absent
        Assert.False(explicitlyOff.TypeCheckerOptionsFor().StrictNullChecks);
        Assert.True(absent.TypeCheckerOptionsFor().StrictNullChecks);
    }

    [Fact]
    public void EqualsTrue_IsAcceptedToo()
    {
        Assert.True(ParseScriptOptions("--strictNullChecks=true", "script.ts").Strictness.StrictNullChecks);
    }

    [Theory]
    [InlineData("--strict=maybe")]
    [InlineData("--noImplicitAny=1")]
    [InlineData("--noEmit=yes")]
    public void NonBooleanValue_IsARejectedWithExitCode64(string arg)
    {
        var error = Assert.IsType<ParsedCommand.Error>(_parser.Parse([arg, "script.ts"]));

        Assert.Equal(64, error.ExitCode);
        Assert.Contains("expects 'true' or 'false'", error.Message);
    }

    #endregion

    #region The --strict umbrella

    [Fact]
    public void Strict_TurnsOnAllThree()
    {
        var resolved = ParseScriptOptions("--strict", "script.ts").TypeCheckerOptions;

        Assert.True(resolved.StrictNullChecks);
        Assert.True(resolved.StrictFunctionTypes);
        Assert.True(resolved.NoImplicitAny);
    }

    [Fact]
    public void StrictFalse_TurnsOffAllThree_IncludingSharpTsDefaultStrictNullChecks()
    {
        var resolved = ParseScriptOptions("--strict=false", "script.ts").TypeCheckerOptions;

        Assert.False(resolved.StrictNullChecks);
        Assert.False(resolved.StrictFunctionTypes);
        Assert.False(resolved.NoImplicitAny);
    }

    [Fact]
    public void IndividualFlag_OverridesTheUmbrella()
    {
        var resolved = ParseScriptOptions("--strict", "--strictFunctionTypes=false", "script.ts")
            .TypeCheckerOptions;

        Assert.True(resolved.StrictNullChecks);
        Assert.False(resolved.StrictFunctionTypes);   // specific beats umbrella
        Assert.True(resolved.NoImplicitAny);
    }

    [Fact]
    public void UmbrellaNeverTouchesMaxErrors()
    {
        Assert.Equal(TypeCheckerOptions.Default.MaxErrors,
            ParseScriptOptions("--strict", "script.ts").TypeCheckerOptions.MaxErrors);
    }

    #endregion

    #region --noEmit

    [Fact]
    public void NoEmit_SetsTheFlag()
    {
        Assert.True(ParseScriptOptions("--noEmit", "script.ts").NoEmit);
    }

    [Fact]
    public void NoEmit_CombinedWithPack_IsRejected()
    {
        var error = Assert.IsType<ParsedCommand.Error>(
            _parser.Parse(["--noEmit", "--compile", "app.ts", "--pack"]));

        Assert.Equal(64, error.ExitCode);
        Assert.Contains("--noEmit cannot be combined with --pack", error.Message);
    }

    [Fact]
    public void NoEmit_AloneInCompileMode_IsFine()
    {
        var compile = Assert.IsType<ParsedCommand.Compile>(
            _parser.Parse(["--noEmit", "--compile", "app.ts"]));

        Assert.True(compile.GlobalOptions.NoEmit);
    }

    #endregion

    #region Flags reach compile mode

    [Fact]
    public void StrictnessFlags_ApplyToCompileMode()
    {
        var compile = Assert.IsType<ParsedCommand.Compile>(
            _parser.Parse(["--strict", "--compile", "app.ts"]));

        Assert.True(compile.GlobalOptions.TypeCheckerOptions.NoImplicitAny);
    }

    #endregion

    #region Regressions fixed alongside

    [Fact]
    public void Decorators_IsAccepted_AndSelectsStage3()
    {
        // Advertised in --help and emitted by the MSBuild SDK, but previously unhandled:
        // run mode returned "Unknown option '--decorators'" with exit 64.
        var options = ParseScriptOptions("--decorators", "script.ts");

        Assert.Equal(DecoratorMode.Stage3, options.DecoratorMode);
    }

    [Fact]
    public void CompileMode_RejectsUnknownFlag_InsteadOfSilentlyIgnoringIt()
    {
        var error = Assert.IsType<ParsedCommand.Error>(
            _parser.Parse(["--compile", "app.ts", "--verfiy"]));

        Assert.Equal(64, error.ExitCode);
        Assert.Contains("Unknown option '--verfiy'", error.Message);
    }

    [Fact]
    public void CompileMode_ReportsMissingValueForKnownFlag()
    {
        var error = Assert.IsType<ParsedCommand.Error>(_parser.Parse(["--compile", "app.ts", "-o"]));

        Assert.Equal(64, error.ExitCode);
        Assert.Contains("requires a value", error.Message);
    }

    [Fact]
    public void CompileMode_StillAcceptsEveryKnownFlag()
    {
        // Guards the new unknown-flag rejection against over-triggering.
        var compile = Assert.IsType<ParsedCommand.Compile>(_parser.Parse(
        [
            "--compile", "app.ts", "-o", "out.dll", "-t", "exe", "--bundler", "builtin",
            "--preserveConstEnums", "--ref-asm", "--sdk-path", "/sdk", "--verify",
            "--msbuild-errors", "--quiet", "--standalone",
        ]));

        Assert.Equal("out.dll", compile.OutputFile);
        Assert.True(compile.CompileOptions.VerifyIL);
        Assert.True(compile.CompileOptions.Standalone);
    }

    #endregion

    #region Script-argument passthrough

    [Fact]
    public void FlagsAfterDoubleDash_ReachTheScript_NotTheParser()
    {
        var script = Assert.IsType<ParsedCommand.Script>(
            _parser.Parse(["app.ts", "--", "--strict"]));

        Assert.Contains("--strict", script.ScriptArgs);
        Assert.True(script.Options.Strictness.IsEmpty);
    }

    #endregion
}

file static class StrictnessTestExtensions
{
    /// <summary>Folds a CLI-only layer the way Program.cs does when no tsconfig is found.</summary>
    public static TypeCheckerOptions TypeCheckerOptionsFor(this StrictnessOptions cli)
        => StrictnessOptions.Resolve(cli, null);
}
