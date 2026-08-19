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

    #region Jsx options

    [Fact]
    public void Load_FoldsJsxOptions()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """
            {
              "compilerOptions": {
                "jsx": "react",
                "jsxFactory": "h",
                "jsxFragmentFactory": "HFragment",
                "jsxImportSource": "preact"
              }
            }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal(JsxMode.React, result.Jsx);
        Assert.Equal("h", result.JsxFactory);
        Assert.Equal("HFragment", result.JsxFragmentFactory);
        Assert.Equal("preact", result.JsxImportSource);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("jsx"));
    }

    [Theory]
    [InlineData("react-jsx", JsxMode.ReactJsx)]
    [InlineData("react-jsxdev", JsxMode.ReactJsxDev)]
    [InlineData("none", JsxMode.None)]
    public void Load_ParsesEveryJsxMode(string value, JsxMode expected)
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json",
            $$"""{ "compilerOptions": { "jsx": "{{value}}" } }""");

        Assert.Equal(expected, TsConfigLoader.Load(path).Jsx);
    }

    [Fact]
    public void Load_LeavesJsxNullWhenUnset()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """{ "compilerOptions": {} }""");

        var result = TsConfigLoader.Load(path);

        Assert.Null(result.Jsx);
        Assert.Null(result.JsxFactory);
        Assert.Null(result.JsxFragmentFactory);
        Assert.Null(result.JsxImportSource);
    }

    [Theory]
    [InlineData("preserve")]
    [InlineData("react-native")]
    public void Load_RejectsEmitOnlyJsxModes(string value)
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json",
            $$"""{ "compilerOptions": { "jsx": "{{value}}" } }""");

        var ex = Assert.Throws<Exception>(() => TsConfigLoader.Load(path));
        Assert.Contains("cannot emit .jsx output", ex.Message);
    }

    [Fact]
    public void Load_JsxFoldsAcrossExtendsChain()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("base.json", """
            { "compilerOptions": { "jsx": "react", "jsxFactory": "h" } }
            """);
        var path = dir.CreateFile("tsconfig.json", """
            { "extends": "./base.json", "compilerOptions": { "jsx": "react-jsx" } }
            """);

        var result = TsConfigLoader.Load(path);

        // Deriving file wins per key; untouched keys inherit from the base.
        Assert.Equal(JsxMode.ReactJsx, result.Jsx);
        Assert.Equal("h", result.JsxFactory);
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
    public void Load_FoldsLibraryAndTypePackageOptions()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("tsconfig.json", """
            {
              "compilerOptions": {
                "lib": ["ES2022", "DOM"],
                "noLib": false,
                "types": ["node"],
                "typeRoots": ["./typings"]
              }
            }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal(["ES2022", "DOM"], result.Lib);
        Assert.False(result.NoLib);
        Assert.Equal(["node"], result.Types);
        Assert.Equal([Path.GetFullPath(dir.GetPath("typings"))], result.TypeRoots);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("lib", StringComparison.Ordinal));
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

    [Fact]
    public void IncludeAndExclude_SelectRootFiles()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src/main.ts", "export {};");
        dir.CreateFile("src/main.spec.ts", "export {};");
        dir.CreateFile("other.ts", "export {};");
        var path = dir.CreateFile("tsconfig.json", """
            { "include": ["src/**/*.ts"], "exclude": ["**/*.spec.ts"] }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal([Path.GetFullPath(dir.GetPath("src/main.ts"))], result.RootFiles);
    }

    [Fact]
    public void FilesAreUnionedWithIncludeAndNotFilteredByExclude()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("forced.ts", "export {};");
        dir.CreateFile("src/included.ts", "export {};");
        var path = dir.CreateFile("tsconfig.json", """
            {
              "files": ["forced.ts"],
              "include": ["src"],
              "exclude": ["forced.ts"]
            }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal(2, result.RootFiles.Count);
        Assert.Contains(Path.GetFullPath(dir.GetPath("forced.ts")), result.RootFiles);
        Assert.Contains(Path.GetFullPath(dir.GetPath("src/included.ts")), result.RootFiles);
    }

    [Fact]
    public void IncludeSupportsWildcardsInDirectorySegments()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("src-a/one.ts", "export {};");
        dir.CreateFile("src-b/two.ts", "export {};");
        var path = dir.CreateFile(
            "tsconfig.json",
            """{ "include": ["src-*/*.ts"] }""");

        Assert.Equal(2, TsConfigLoader.Load(path).RootFiles.Count);
    }

    [Fact]
    public void EmptyFilesArrayIsPreserved()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("ignored.ts", "export {};");
        var path = dir.CreateFile("tsconfig.json", """{ "files": [] }""");

        Assert.Empty(TsConfigLoader.Load(path).RootFiles);
    }

    [Fact]
    public void PathsAndBaseUrlResolveAgainstDeclaringConfig()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("configs/base.json", """
            {
              "compilerOptions": {
                "baseUrl": "../src",
                "paths": { "@app/*": ["app/*"] },
                "moduleResolution": "bundler"
              }
            }
            """);
        var path = dir.CreateFile("tsconfig.json", """
            { "extends": "./configs/base.json", "files": [] }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal(ModuleResolutionMode.Bundler, result.ModuleResolution.Mode);
        Assert.Equal(Path.GetFullPath(dir.GetPath("src")), result.ModuleResolution.BaseUrl);
        Assert.Equal(
            Path.GetFullPath(dir.GetPath("src/app/*")),
            Assert.Single(result.ModuleResolution.Paths["@app/*"]));
    }

    [Fact]
    public void TypesAndTypeRootsResolveDeclarationEntries()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string declaration = dir.CreateFile(
            "typings/custom/index.d.ts",
            "declare const customGlobal: string;");
        var path = dir.CreateFile("tsconfig.json", """
            {
              "files": [],
              "compilerOptions": {
                "typeRoots": ["./typings"],
                "types": ["custom"]
              }
            }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal(Path.GetFullPath(declaration), Assert.Single(result.DeclarationFiles));
        Assert.Equal(Path.GetFullPath(dir.GetPath("typings")), Assert.Single(result.TypeRoots!));
    }

    [Fact]
    public void ProjectReferencesResolveToConfigPaths()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string referenced = dir.CreateFile("packages/core/tsconfig.json", """{ "files": [] }""");
        var path = dir.CreateFile("tsconfig.json", """
            { "files": [], "references": [{ "path": "./packages/core" }] }
            """);

        var result = TsConfigLoader.Load(path);

        Assert.Equal(Path.GetFullPath(referenced), Assert.Single(result.ProjectReferences));
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
        Assert.False(resolved.CheckVariableUseBeforeAssignment);
    }

    [Fact]
    public void CliWins_OverTsConfig()
    {
        var resolved = StrictnessOptions.Resolve(
            new StrictnessOptions { StrictNullChecks = true },
            new StrictnessOptions { StrictNullChecks = false });

        Assert.True(resolved.StrictNullChecks);
        Assert.True(resolved.CheckVariableUseBeforeAssignment);
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
        Assert.True(resolved.CheckVariableUseBeforeAssignment);
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
