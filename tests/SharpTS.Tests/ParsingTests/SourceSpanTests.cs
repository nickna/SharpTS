using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParsingTests;

/// <summary>
/// Covers the source model that editor navigation and portable-PDB sequence points are both built
/// on: exact spans for source-backed statements, and explicit provenance across the transforms that
/// rewrite the AST between parsing and emission.
/// </summary>
public class SourceSpanTests
{
    private static SourceDocument Parse(string source, string path = "test.ts")
    {
        var document = new SourceDocument(path, source);
        new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        return document;
    }

    private static List<Stmt> ParseStatements(string source, out SourceDocument document)
    {
        document = new SourceDocument("test.ts", source);
        return new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
    }

    /// <summary>Returns the exact source text a node's span covers.</summary>
    private static string TextOf(SourceDocument document, object node)
    {
        Assert.True(document.Spans.TryGetSpan(node, out var span), "node has no recorded span");
        Assert.False(span.IsHidden, "node was marked compiler-generated");
        return document.Text[span.Start..span.End];
    }

    // ---------------------------------------------------------------- exact spans

    [Fact]
    public void StatementSpansCoverExactlyTheirSourceText()
    {
        var statements = ParseStatements("const x = 1;\nlet y = x + 2;\n", out var document);

        Assert.Equal("const x = 1;", TextOf(document, statements[0]));
        Assert.Equal("let y = x + 2;", TextOf(document, statements[1]));
    }

    [Fact]
    public void DeclarationSpansCoverTheWholeDeclaration()
    {
        const string source = "function add(a: number, b: number): number {\n  return a + b;\n}\n";
        var statements = ParseStatements(source, out var document);

        var function = Assert.IsType<Stmt.Function>(statements[0]);
        Assert.Equal(source.TrimEnd('\n'), TextOf(document, function));
        Assert.Equal("return a + b;", TextOf(document, function.Body![0]));
    }

    [Fact]
    public void NestedStatementsGetTheirOwnSpans()
    {
        const string source = "if (a) {\n  b();\n} else {\n  c();\n}\n";
        var statements = ParseStatements(source, out var document);

        var conditional = Assert.IsType<Stmt.If>(statements[0]);
        Assert.Equal("b();", TextOf(document, Assert.IsType<Stmt.Block>(conditional.ThenBranch).Statements[0]));
        Assert.Equal("c();", TextOf(document, Assert.IsType<Stmt.Block>(conditional.ElseBranch!).Statements[0]));
    }

    [Fact]
    public void SpansAreContainedByTheirEnclosingStatement()
    {
        var statements = ParseStatements("while (going) {\n  step();\n}\n", out var document);

        var loop = Assert.IsType<Stmt.While>(statements[0]);
        var body = Assert.IsType<Stmt.Block>(loop.Body).Statements[0];

        Assert.True(document.Spans.TryGetSpan(loop, out var outer));
        Assert.True(document.Spans.TryGetSpan(body, out var inner));
        Assert.True(outer.Contains(inner), $"{outer} should contain {inner}");
    }

    /// <summary>
    /// Positions are derived from offsets, so a span has to survive the round trip a debugger and an
    /// LSP client both perform.
    /// </summary>
    [Fact]
    public void SpansConvertToOneBasedPositions()
    {
        var statements = ParseStatements("const a = 1;\nconst b = 2;\n", out var document);

        var position = document.PositionOf(statements[1]);
        Assert.NotNull(position);
        Assert.Equal((2, 1, 2, 13), position!.Value);
    }

    // ---------------------------------------------------------------- token offsets

    [Fact]
    public void TokenEndCoversDelimitersNotJustContent()
    {
        var tokens = new Lexer("\"hello\"").ScanTokens();

        var literal = tokens[0];
        Assert.Equal(TokenType.STRING, literal.Type);
        Assert.Equal(0, literal.Start);
        Assert.Equal(7, literal.End); // includes both quotes
    }

    /// <summary>
    /// Closing a nested generic splits <c>&gt;&gt;</c> into two tokens. The remainder has to keep a
    /// real offset or everything parsed after a nested generic loses its position.
    /// </summary>
    [Fact]
    public void SplittingNestedGenericClosersPreservesOffsets()
    {
        const string source = "let m: Array<Array<number>> = [];\nlet after = 1;\n";
        var statements = ParseStatements(source, out var document);

        Assert.Equal("let after = 1;", TextOf(document, statements[1]));
    }

    // ---------------------------------------------------------------- transform provenance

    /// <summary>
    /// Hoisting rebuilds every statement enclosing a <c>var</c>. Those rebuilt statements are still
    /// the user's code and must keep pointing at it.
    /// </summary>
    [Fact]
    public void VarHoistingKeepsSpansOnRewrittenStatements()
    {
        const string source = "function f() {\n  if (c) { var x = 1; }\n  return x;\n}\n";
        var statements = ParseStatements(source, out var document);

        var function = Assert.IsType<Stmt.Function>(statements[0]);
        var body = function.Body!;

        // The synthetic `var x;` the hoister prepends is scaffolding, not something to step onto.
        Assert.True(document.Spans.IsHidden(body[0]));

        // Everything the user actually wrote still resolves to its own text.
        var conditional = Assert.IsType<Stmt.If>(body[1]);
        Assert.Equal("if (c) { var x = 1; }", TextOf(document, conditional));
        Assert.Equal("return x;", TextOf(document, body[2]));
    }

    [Fact]
    public void DestructuringLoweringAttributesEveryPartToTheDeclaration()
    {
        var statements = ParseStatements("const [a, b] = pair;\n", out var document);

        var lowered = Assert.IsType<Stmt.Sequence>(statements[0]);
        Assert.Equal("const [a, b] = pair;", TextOf(document, lowered));
        Assert.All(lowered.Statements, part => Assert.Equal("const [a, b] = pair;", TextOf(document, part)));
    }

    [Fact]
    public void NestedDestructuringAttributesInnerPartsToo()
    {
        const string source = "const [[a, b], [c, d]] = pairs;\n";
        var statements = ParseStatements(source, out var document);

        var lowered = Assert.IsType<Stmt.Sequence>(statements[0]);
        foreach (var nested in lowered.Statements.OfType<Stmt.Sequence>())
        {
            Assert.All(nested.Statements, part => Assert.Equal(source.TrimEnd('\n'), TextOf(document, part)));
        }
    }

    /// <summary>
    /// Attributing a lowering's parts must stay linear in the size of the AST.
    /// </summary>
    /// <remarks>
    /// Statement nodes are shared between sequences and a sequence is re-recorded by every enclosing
    /// production, so a walk that re-descends into already-attributed subtrees compounds badly — an
    /// earlier revision of this code turned a two-file project check into a 19-second run. The bound
    /// here is ~100x the real cost, so it flags a blowup without being sensitive to machine speed.
    /// </remarks>
    [Fact]
    public void AttributingNestedLoweringsStaysLinear()
    {
        var source = new System.Text.StringBuilder();
        for (int i = 0; i < 400; i++)
            source.Append($"const [[a{i}, b{i}], [c{i}, d{i}]] = pairs;\n");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ParseStatements(source.ToString(), out var document);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Parsing 400 nested destructuring declarations took {stopwatch.Elapsed.TotalSeconds:F1}s, " +
            "which suggests span attribution is re-walking shared subtrees.");
        Assert.NotEqual(0, document.Spans.Count);
    }

    // ---------------------------------------------------------------- span table semantics

    [Fact]
    public void SpansAreKeyedByReferenceNotByValue()
    {
        var table = new SpanTable();
        var keyword = new Token(TokenType.BREAK, "break", null, 1, 0);
        var first = new Stmt.Break(keyword);
        var second = new Stmt.Break(keyword);

        // Records compare equal by value; the table must still tell these two apart.
        Assert.Equal(first, second);

        table.Record(first, new SourceSpan(0, 5));
        table.Record(second, new SourceSpan(100, 105));

        Assert.Equal(new SourceSpan(0, 5), table.GetSpan(first));
        Assert.Equal(new SourceSpan(100, 105), table.GetSpan(second));
    }

    [Fact]
    public void TheFirstSpanRecordedForANodeWins()
    {
        var table = new SpanTable();
        var node = new object();

        table.Record(node, new SourceSpan(10, 20));
        table.Record(node, new SourceSpan(0, 50));

        Assert.Equal(new SourceSpan(10, 20), table.GetSpan(node));
    }

    [Fact]
    public void CopyingASpanFromANodeWithoutOneIsANoOp()
    {
        var table = new SpanTable();
        var original = new object();
        var replacement = new object();

        table.CopySpan(original, replacement);

        Assert.Null(table.GetSpan(replacement));
    }

    [Fact]
    public void HiddenNodesAreDistinctFromNodesWithNoSpan()
    {
        var table = new SpanTable();
        var hidden = new object();
        var unknown = new object();

        table.MarkHidden(hidden);

        Assert.True(table.IsHidden(hidden));
        Assert.True(table.TryGetSpan(hidden, out var span) && span.IsHidden);
        Assert.False(table.IsHidden(unknown));
        Assert.False(table.TryGetSpan(unknown, out _));
    }

    [Fact]
    public void HiddenSpansAreNotContainedByAnything()
    {
        var enclosing = new SourceSpan(0, 100);

        Assert.False(enclosing.Contains(SourceSpan.Hidden));
        Assert.False(SourceSpan.Hidden.Contains(enclosing));
        Assert.False(SourceSpan.Hidden.Contains(5));
    }

    // ---------------------------------------------------------------- document model

    [Fact]
    public void LineIndexRoundTripsOffsetsAndPositions()
    {
        var index = new LineIndex("one\ntwo\r\nthree");

        Assert.Equal(3, index.LineCount);
        Assert.Equal((1, 1), index.ToPosition(0));
        Assert.Equal((2, 1), index.ToPosition(4));
        Assert.Equal((3, 3), index.ToPosition(11));
        Assert.Equal(4, index.ToOffset(2, 1));
        Assert.Equal(11, index.ToOffset(3, 3));
    }

    [Fact]
    public void DocumentChecksumTracksContent()
    {
        var first = new SourceDocument("a.ts", "const x = 1;");
        var same = new SourceDocument("b.ts", "const x = 1;");
        var different = new SourceDocument("c.ts", "const x = 2;");

        Assert.Equal(first.Checksum, same.Checksum);
        Assert.NotEqual(first.Checksum, different.Checksum);
    }

    [Fact]
    public void ParsingIntoADocumentPopulatesItsSpanTable()
    {
        var document = Parse("const x = 1;\n");

        Assert.NotEqual(0, document.Spans.Count);
    }
}
