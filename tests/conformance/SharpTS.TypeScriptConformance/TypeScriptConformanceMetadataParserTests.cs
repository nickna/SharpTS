using Xunit;
using Xunit.Abstractions;

namespace SharpTS.TypeScriptConformance;

public class TypeScriptConformanceMetadataParserTests
{
    [Fact]
    public void NoDirectives_TreatsEntireSourceAsSingleVirtualFile()
    {
        var src = "const x = 1;\nconst y = 2;\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("foo.ts", src);

        Assert.Empty(meta.RawDirectives);
        Assert.Single(meta.Files);
        Assert.Equal("foo.ts", meta.Files[0].Name);
        Assert.Equal(src, meta.Files[0].Body);
        Assert.Null(meta.Target);
        Assert.False(meta.Strict);
    }

    [Fact]
    public void ScalarDirective_ParsesIntoTypedFieldAndRawDictionary()
    {
        var src = "// @target: es2015\nconst x = 1;\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", src);

        Assert.Equal("es2015", meta.Target);
        Assert.Equal("es2015", meta.RawDirectives["target"]);
        Assert.Single(meta.Files);
    }

    [Fact]
    public void MultipleDirectives_AllExtracted()
    {
        var src = "// @target: es2015\n// @strict: true\n// @declaration: true\nconst x = 1;\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", src);

        Assert.Equal("es2015", meta.Target);
        Assert.True(meta.Strict);
        Assert.Equal("true", meta.RawDirectives["declaration"]);
        Assert.Equal(3, meta.RawDirectives.Count);
    }

    [Fact]
    public void LibDirective_ParsesAsCommaSeparatedList()
    {
        var src = "// @lib: es2015,dom\nconst x = 1;\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", src);

        Assert.Equal(new[] { "es2015", "dom" }, meta.Lib);
    }

    [Fact]
    public void LibDirective_TrimsWhitespaceAndIgnoresEmptyEntries()
    {
        var src = "// @lib: es2015 , , dom\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", src);

        Assert.Equal(new[] { "es2015", "dom" }, meta.Lib);
    }

    [Fact]
    public void Filename_SplitsIntoVirtualFiles()
    {
        var src =
            "// @module: commonjs\n" +
            "\n" +
            "// @filename: a.ts\n" +
            "export const x = 1;\n" +
            "\n" +
            "// @filename: b.ts\n" +
            "import { x } from './a';\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("test.ts", src);

        Assert.Equal("commonjs", meta.Module);
        Assert.Equal(2, meta.Files.Count);
        Assert.Equal("a.ts", meta.Files[0].Name);
        Assert.Equal("export const x = 1;\n", meta.Files[0].Body);
        Assert.Equal("b.ts", meta.Files[1].Name);
        Assert.Equal("import { x } from './a';\n", meta.Files[1].Body);

        // @filename: itself is a structural marker, not a directive value
        Assert.False(meta.RawDirectives.ContainsKey("filename"));
    }

    [Fact]
    public void DirectiveAfterFilename_StillRecordedGlobally_AndKeptInBody()
    {
        // Mirrors a real corpus pattern: @module appears AFTER @filename: main.ts.
        var src =
            "// @esModuleInterop: true\n" +
            "// @target: esnext\n" +
            "// @filename: main.ts\n" +
            "// @module: commonjs\n" +
            "import a from \"a\";\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("test.ts", src);

        Assert.Equal("esnext", meta.Target);
        Assert.Equal("commonjs", meta.Module);
        Assert.Equal("true", meta.RawDirectives["esmoduleinterop"]);

        Assert.Single(meta.Files);
        Assert.Equal("main.ts", meta.Files[0].Name);
        // Body preserves the @module: line as a comment so source line numbers
        // line up with what TS baselines reference.
        Assert.StartsWith("// @module: commonjs", meta.Files[0].Body);
    }

    [Fact]
    public void DirectiveKeyIsCaseInsensitive_StoredLowercase()
    {
        var src = "// @StrictNullChecks: true\n// @NOIMPLICITANY: false\n";
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", src);

        Assert.True(meta.StrictNullChecks);
        Assert.False(meta.NoImplicitAny);
        Assert.True(meta.RawDirectives.ContainsKey("strictnullchecks"));
        Assert.True(meta.RawDirectives.ContainsKey("noimplicitany"));
    }

    [Fact]
    public void BoolValue_IsCaseInsensitive()
    {
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", "// @strict: TRUE\n");
        Assert.True(meta.Strict);
    }

    [Fact]
    public void UnknownBoolValue_LeavesNullableNull()
    {
        // Phase-1 behavior: unrecognized values don't throw — they just don't set the typed flag.
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", "// @strictNullChecks: maybe\n");
        Assert.Null(meta.StrictNullChecks);
        Assert.Equal("maybe", meta.RawDirectives["strictnullchecks"]);
    }

    [Fact]
    public void HasAnyDirective_MatchesCaseInsensitively()
    {
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", "// @experimentalDecorators: true\n");
        Assert.True(meta.HasAnyDirective(new[] { "experimentalDecorators" }));
        Assert.True(meta.HasAnyDirective(new[] { "EXPERIMENTALDECORATORS" }));
        Assert.False(meta.HasAnyDirective(new[] { "useDefineForClassFields" }));
    }

    [Fact]
    public void EmptySource_ReturnsOneEmptyFile()
    {
        var meta = TypeScriptConformanceMetadataParser.Parse("a.ts", "");
        Assert.Single(meta.Files);
        Assert.Equal(string.Empty, meta.Files[0].Body);
        Assert.Empty(meta.RawDirectives);
    }

    /// <summary>
    /// Acceptance check from #82: parse every <c>.ts</c> in the corpus and
    /// confirm the parser doesn't throw on anything. Soft-skip when the
    /// submodule isn't initialized.
    /// </summary>
    [Fact]
    public void Corpus_ParsesEveryFileWithoutThrowing()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;

        var conformanceDir = TypeScriptConformancePaths.ConformanceDir(root);
        var files = Directory.EnumerateFiles(conformanceDir, "*.ts", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);

        var failures = new System.Collections.Concurrent.ConcurrentBag<(string Path, Exception Ex)>();
        Parallel.ForEach(files, path =>
        {
            try
            {
                var src = File.ReadAllText(path);
                _ = TypeScriptConformanceMetadataParser.Parse(path, src);
            }
            catch (Exception ex)
            {
                failures.Add((path, ex));
            }
        });

        Assert.True(
            failures.IsEmpty,
            $"Parser threw on {failures.Count} corpus file(s). First: {(failures.TryPeek(out var first) ? first.Path + " — " + first.Ex.Message : "")}");
    }
}
