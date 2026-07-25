using SharpTS.Configuration;
using SharpTS.Parsing;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ConfigurationTests;

/// <summary>
/// Discovery, <c>extends</c> resolution, and folding of tsconfig.json.
/// Mirrors <c>ReferencesTests/SharpTsManifestLoaderTests</c>, whose discovery policy this
/// deliberately copies.
/// </summary>
public class TsConfigLoaderTests
{
    #region Discovery

    [Fact]
    public void FindAndLoad_FindsConfigInStartDirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strict": true } }""");

        var result = TsConfigLoader.FindAndLoad(dir.Path);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(dir.GetPath("tsconfig.json")), result.ConfigPath);
        Assert.True(result.Strictness.Strict);
    }

    [Fact]
    public void FindAndLoad_WalksUpFromNestedDirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        dir.CreateFile("src/app/entry.ts", "export {};");

        var result = TsConfigLoader.FindAndLoad(Path.GetDirectoryName(dir.GetPath("src/app/entry.ts"))!);

        Assert.NotNull(result);
        Assert.False(result.Strictness.StrictNullChecks);
    }

    [Fact]
    public void FindAndLoad_ReturnsNullWhenAbsent()
    {
        // The temp-root ceiling stops the walk, so an unrelated tsconfig.json further up the
        // tree cannot leak into the result.
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/entry.ts", "export {};");

        Assert.Null(TsConfigLoader.FindAndLoad(Path.GetDirectoryName(dir.GetPath("src/entry.ts"))!));
    }

    #endregion

    #region Parsing

    [Fact]
    public void Load_AcceptsCommentsTrailingCommasAndAnyKeyCase()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """
            {
              // a line comment
              "CompilerOptions": {
                /* and a block comment */
                "StrictNullChecks": false,
              },
            }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.False(result.Strictness.StrictNullChecks);
    }

    [Fact]
    public void Load_MalformedJson_ThrowsNamingTheFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", "{ this is not json ");

        var ex = Assert.Throws<Exception>(() => TsConfigLoader.Load(path));

        Assert.Contains(path, ex.Message);
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        using var dir = CliTestHelper.CreateTempDirectory();

        Assert.Throws<FileNotFoundException>(() => TsConfigLoader.Load(dir.GetPath("nope.json")));
    }

    #endregion

    #region extends

    [Fact]
    public void Extends_DerivingFileOverridesBase()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("base.json", """{ "compilerOptions": { "strict": true, "strictNullChecks": true } }""");
        var path = dir.CreateFile("tsconfig.json", """
            { "extends": "./base.json", "compilerOptions": { "strictNullChecks": false } }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.True(result.Strictness.Strict);            // inherited
        Assert.False(result.Strictness.StrictNullChecks); // overridden
        Assert.Equal(2, result.ExtendsChain.Count);
        Assert.EndsWith("base.json", result.ExtendsChain[0]);   // base first
        Assert.EndsWith("tsconfig.json", result.ExtendsChain[1]);
    }

    [Fact]
    public void Extends_ResolvesRelativeToTheDeclaringFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("configs/base.json", """{ "compilerOptions": { "noImplicitAny": true } }""");
        // The parent reference is relative to configs/, not to the leaf's directory.
        dir.CreateFile("configs/mid.json", """{ "extends": "./base.json" }""");
        var path = dir.CreateFile("tsconfig.json", """{ "extends": "./configs/mid.json" }""");

        var result = TsConfigLoader.Load(path);

        Assert.True(result.Strictness.NoImplicitAny);
        Assert.Equal(3, result.ExtendsChain.Count);
    }

    [Fact]
    public void Extends_AcceptsArrayForm_LaterEntriesWin()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("a.json", """{ "compilerOptions": { "strictNullChecks": true } }""");
        dir.CreateFile("b.json", """{ "compilerOptions": { "strictNullChecks": false } }""");
        var path = dir.CreateFile("tsconfig.json", """{ "extends": ["./a.json", "./b.json"] }""");

        Assert.False(TsConfigLoader.Load(path).Strictness.StrictNullChecks);
    }

    [Fact]
    public void Extends_OmittedJsonExtension_IsResolved()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("base.json", """{ "compilerOptions": { "strict": true } }""");
        var path = dir.CreateFile("tsconfig.json", """{ "extends": "./base" }""");

        Assert.True(TsConfigLoader.Load(path).Strictness.Strict);
    }

    [Fact]
    public void Extends_FromNodeModules_IsResolved()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("node_modules/@acme/tsconfig/tsconfig.json",
            """{ "compilerOptions": { "strictFunctionTypes": true } }""");
        var path = dir.CreateFile("tsconfig.json", """{ "extends": "@acme/tsconfig" }""");

        Assert.True(TsConfigLoader.Load(path).Strictness.StrictFunctionTypes);
    }

    [Fact]
    public void Extends_Circular_ThrowsNamingTheCycle()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("a.json", """{ "extends": "./b.json" }""");
        dir.CreateFile("b.json", """{ "extends": "./a.json" }""");
        var path = dir.CreateFile("tsconfig.json", """{ "extends": "./a.json" }""");

        var ex = Assert.Throws<Exception>(() => TsConfigLoader.Load(path));

        Assert.Contains("circular 'extends'", ex.Message);
        Assert.Contains("a.json", ex.Message);
        Assert.Contains("b.json", ex.Message);
    }

    [Fact]
    public void Extends_Unresolvable_ThrowsNamingTheTarget()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """{ "extends": "./missing.json" }""");

        var ex = Assert.Throws<Exception>(() => TsConfigLoader.Load(path));

        Assert.Contains("cannot resolve 'extends' target", ex.Message);
        Assert.Contains("missing.json", ex.Message);
    }

    #endregion

    #region Other keys

    [Fact]
    public void Files_FirstEntryBecomesTheEntryPoint_Absolute()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/main.ts", "export {};");
        var path = dir.CreateFile("tsconfig.json", """{ "files": ["src/main.ts"] }""");

        var result = TsConfigLoader.Load(path);

        Assert.Equal(Path.GetFullPath(dir.GetPath("src/main.ts")), result.EntryFile);
    }

    [Fact]
    public void OutDir_ResolvesAgainstTheDeclaringFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """{ "compilerOptions": { "outDir": "./build" } }""");

        Assert.Equal(Path.GetFullPath(dir.GetPath("build")), TsConfigLoader.Load(path).OutDir);
    }

    [Fact]
    public void Decorators_WinsOverExperimentalDecorators()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json",
            """{ "compilerOptions": { "experimentalDecorators": true, "decorators": true } }""");

        Assert.Equal(DecoratorMode.Stage3, TsConfigLoader.Load(path).DecoratorMode);
    }

    [Fact]
    public void ExperimentalDecoratorsAlone_SelectsLegacy()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json",
            """{ "compilerOptions": { "experimentalDecorators": true } }""");

        Assert.Equal(DecoratorMode.Legacy, TsConfigLoader.Load(path).DecoratorMode);
    }

    #endregion

    #region Unknown-key diagnosis

    [Fact]
    public void UnknownCompilerOption_WarnsWithASuggestion()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json",
            """{ "compilerOptions": { "strictNulChecks": true } }""");

        var warning = Assert.Single(TsConfigLoader.Load(path).Warnings);

        Assert.Contains("unknown compiler option 'strictNulChecks'", warning);
        Assert.Contains("Did you mean 'strictNullChecks'?", warning);
    }

    [Fact]
    public void InapplicableEmitOptions_AreSilentByDefault()
    {
        // These appear in essentially every real tsconfig; warning on them every run would
        // train users to ignore SharpTS warnings.
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """
            { "compilerOptions": { "target": "ES2020", "module": "ESNext", "sourceMap": true } }
            """);

        Assert.Empty(TsConfigLoader.Load(path).Warnings);
    }

    [Fact]
    public void UnknownTopLevelKey_Warns()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """{ "compilerOption": {} }""");

        var warning = Assert.Single(TsConfigLoader.Load(path).Warnings);

        Assert.Contains("unknown key 'compilerOption'", warning);
        Assert.Contains("Did you mean 'compilerOptions'?", warning);
    }

    [Fact]
    public void WarningsAreAttributedToTheFileThatContainsTheTypo()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var basePath = dir.CreateFile("base.json", """{ "compilerOptions": { "strictNulChecks": true } }""");
        var path = dir.CreateFile("tsconfig.json", """{ "extends": "./base.json" }""");

        var warning = Assert.Single(TsConfigLoader.Load(path).Warnings);

        Assert.Contains(basePath, warning);
    }

    #endregion

    #region -p / --project

    [Fact]
    public void ResolveProjectPath_AcceptsADirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("tsconfig.json", "{}");

        Assert.Equal(Path.GetFullPath(dir.GetPath("tsconfig.json")),
            TsConfigLoader.ResolveProjectPath(dir.Path));
    }

    [Fact]
    public void ResolveProjectPath_AcceptsAnyFileName()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.build.json", "{}");

        Assert.Equal(Path.GetFullPath(path), TsConfigLoader.ResolveProjectPath(path));
    }

    [Fact]
    public void ResolveProjectPath_DirectoryWithoutConfig_Throws()
    {
        using var dir = CliTestHelper.CreateTempDirectory();

        var ex = Assert.Throws<Exception>(() => TsConfigLoader.ResolveProjectPath(dir.Path));

        Assert.Contains("no tsconfig.json in", ex.Message);
    }

    [Fact]
    public void ResolveProjectPath_MissingPath_ThrowsNamingIt()
    {
        using var dir = CliTestHelper.CreateTempDirectory();

        var ex = Assert.Throws<Exception>(() => TsConfigLoader.ResolveProjectPath(dir.GetPath("nope")));

        Assert.Contains("does not exist", ex.Message);
    }

    #endregion
}

/// <summary>The CLI-over-tsconfig precedence matrix, independent of any file I/O.</summary>
public class StrictnessResolutionTests
{
    private static readonly StrictnessOptions None = new();

    [Fact]
    public void NoLayers_YieldProductDefaults()
    {
        Assert.Equal(TypeCheckerOptions.Default, StrictnessOptions.Resolve(null, null));
        Assert.Equal(TypeCheckerOptions.Default, StrictnessOptions.Resolve(None, None));
    }

    [Fact]
    public void TsConfigApplies_WhenTheCliIsSilent()
    {
        var resolved = StrictnessOptions.Resolve(None, new StrictnessOptions { StrictNullChecks = false });

        Assert.False(resolved.StrictNullChecks);
    }

    [Fact]
    public void CliWins_OverTsConfig()
    {
        var resolved = StrictnessOptions.Resolve(
            new StrictnessOptions { StrictNullChecks = true },
            new StrictnessOptions { StrictNullChecks = false });

        Assert.True(resolved.StrictNullChecks);
    }

    [Fact]
    public void CliExplicitFalse_BeatsTsConfigTrue()
    {
        // The whole reason the layers are tri-state.
        var resolved = StrictnessOptions.Resolve(
            new StrictnessOptions { NoImplicitAny = false },
            new StrictnessOptions { NoImplicitAny = true });

        Assert.False(resolved.NoImplicitAny);
    }

    [Fact]
    public void TsConfigStrict_ActsAsAnUmbrella()
    {
        var resolved = StrictnessOptions.Resolve(None, new StrictnessOptions { Strict = true });

        Assert.True(resolved.StrictNullChecks);
        Assert.True(resolved.StrictFunctionTypes);
        Assert.True(resolved.NoImplicitAny);
    }

    [Fact]
    public void CliStrict_OverridesTsConfigStrict()
    {
        var resolved = StrictnessOptions.Resolve(
            new StrictnessOptions { Strict = false },
            new StrictnessOptions { Strict = true });

        Assert.False(resolved.StrictNullChecks);
        Assert.False(resolved.NoImplicitAny);
    }

    [Fact]
    public void SpecificTsConfigKey_BeatsTsConfigStrict()
    {
        var resolved = StrictnessOptions.Resolve(
            None,
            new StrictnessOptions { Strict = true, StrictFunctionTypes = false });

        Assert.True(resolved.StrictNullChecks);
        Assert.False(resolved.StrictFunctionTypes);
    }

    [Fact]
    public void CliSpecificKey_BeatsTsConfigStrictUmbrella()
    {
        var resolved = StrictnessOptions.Resolve(
            new StrictnessOptions { NoImplicitAny = false },
            new StrictnessOptions { Strict = true });

        Assert.True(resolved.StrictNullChecks);   // from the umbrella
        Assert.False(resolved.NoImplicitAny);     // CLI overrode it
    }

    [Fact]
    public void MergingLayersFirst_IsEquivalentToResolvingBoth()
    {
        // Program.cs folds the tsconfig layer INTO the CLI layer and later resolves with a null
        // second layer. That shortcut must be indistinguishable from resolving both at once.
        var cli = new StrictnessOptions { StrictFunctionTypes = true };
        var config = new StrictnessOptions { Strict = true, StrictNullChecks = false };

        var merged = new StrictnessOptions
        {
            Strict = cli.Strict ?? config.Strict,
            StrictNullChecks = cli.StrictNullChecks ?? config.StrictNullChecks,
            StrictFunctionTypes = cli.StrictFunctionTypes ?? config.StrictFunctionTypes,
            NoImplicitAny = cli.NoImplicitAny ?? config.NoImplicitAny,
        };

        Assert.Equal(StrictnessOptions.Resolve(cli, config), StrictnessOptions.Resolve(merged, null));
    }

    [Fact]
    public void ResolutionNeverTouchesMaxErrors()
    {
        var resolved = StrictnessOptions.Resolve(
            new StrictnessOptions { Strict = true }, new StrictnessOptions { Strict = false });

        Assert.Equal(TypeCheckerOptions.Default.MaxErrors, resolved.MaxErrors);
    }
}
