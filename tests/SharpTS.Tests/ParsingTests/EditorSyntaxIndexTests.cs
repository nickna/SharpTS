using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParsingTests;

public sealed class EditorSyntaxIndexTests
{
    [Fact]
    public void EqualNodesKeepIndependentReferenceKeyedViews()
    {
        var first = new Expr.Literal(1d);
        var second = new Expr.Literal(1d);
        Assert.Equal(first, second);
        var document = new SourceDocument("equal.ts", "1; 1;");
        var builder = new EditorSyntaxBuilder();
        builder.Record(new EditorSyntaxRecord(first, EditorSyntaxKind.Literal, new(0, 1)));
        builder.Record(new EditorSyntaxRecord(second, EditorSyntaxKind.Literal, new(3, 4)));
        var index = builder.Build(document, [new Stmt.Expression(first), new Stmt.Expression(second)], default);

        Assert.Equal(new SourceSpan(0, 1), Assert.Single(index.GetRecords(first)).Span);
        Assert.Equal(new SourceSpan(3, 4), Assert.Single(index.GetRecords(second)).Span);
        Assert.Empty(index.GetRecords(new Expr.Literal(1d)));
        Assert.Same(first, index.FindNarrowest(0)!.Node);
        Assert.Same(second, index.FindNarrowest(3)!.Node);
    }

    [Fact]
    public void PublicationDropsAbandonedViewsAndTheirSpanEntries()
    {
        var kept = new Expr.Literal(1d);
        var abandoned = new Expr.Literal(1d);
        var document = new SourceDocument("speculation.ts", "1;");
        var builder = new EditorSyntaxBuilder();
        builder.Record(new EditorSyntaxRecord(kept, EditorSyntaxKind.Literal, new(0, 1)));
        builder.Record(new EditorSyntaxRecord(abandoned, EditorSyntaxKind.Literal, new(0, 1)));
        document.Spans.Record(kept, new(0, 1));
        document.Spans.Record(abandoned, new(0, 1));

        var index = builder.Build(document, [new Stmt.Expression(kept)], default);

        Assert.Single(index.Records);
        Assert.Same(kept, index.Records[0].Node);
        Assert.False(document.Spans.TryGetSpan(abandoned, out _));
        Assert.True(document.Spans.TryGetSpan(kept, out _));
    }

    [Fact]
    public void PublishedCollectionsAndCommaListsDoNotFollowBuilderMutations()
    {
        var callee = new Expr.Variable(new(TokenType.IDENTIFIER, "f", null, 1, 0));
        var call = new Expr.Call(callee, new(TokenType.RIGHT_PAREN, ")", null, 1, 5), null, []);
        var commas = new List<SourceSpan> { new(3, 4) };
        var builder = new EditorSyntaxBuilder();
        builder.Record(new EditorInvocationSyntax(call, callee, new(0, 6), new(1, 2), new(5, 6), commas));
        var index = builder.Build(new("call.ts", "f(1,2)"), [new Stmt.Expression(call)], default);

        commas.Clear();
        builder.Record(new EditorSyntaxRecord(call, EditorSyntaxKind.Expression, new(0, 6)));

        Assert.Empty(index.Records);
        Assert.Equal(new SourceSpan(3, 4), Assert.Single(Assert.Single(index.Invocations).Commas));
    }

    [Fact]
    public void NarrowestLookupUsesHalfOpenRangesAndStableRecordingOrderForExactTies()
    {
        var first = new Expr.Literal(1d);
        var second = new Expr.Literal(2d);
        var builder = new EditorSyntaxBuilder();
        builder.Record(new EditorSyntaxRecord(first, EditorSyntaxKind.Expression, new(0, 5)));
        builder.Record(new EditorSyntaxRecord(first, EditorSyntaxKind.Literal, new(1, 2)));
        builder.Record(new EditorSyntaxRecord(second, EditorSyntaxKind.Literal, new(1, 2)));
        var index = builder.Build(new("ties.ts", " 1   "), [new Stmt.Expression(first), new Stmt.Expression(second)], default);

        Assert.Same(first, index.FindNarrowest(1)!.Node);
        Assert.Equal(EditorSyntaxKind.Expression, index.FindNarrowest(2)!.Kind);
        Assert.Null(index.FindNarrowest(5));
        Assert.Null(index.FindNarrowest(-1));
    }

    [Theory]
    [InlineData(EditorSyntaxOrigin.Recovered)]
    [InlineData(EditorSyntaxOrigin.Synthetic)]
    [InlineData(EditorSyntaxOrigin.Implicit)]
    public void NonWrittenOriginsAreNeverAuthoritativeLocations(EditorSyntaxOrigin origin)
    {
        var expression = new Expr.Literal(1d);
        var builder = new EditorSyntaxBuilder();
        builder.Record(new EditorSyntaxRecord(expression, EditorSyntaxKind.Literal, new(0, 1), Origin: origin));
        var index = builder.Build(new("origin.ts", "1"), [new Stmt.Expression(expression)], default);

        Assert.False(Assert.Single(index.Records).IsAuthoritative);
        Assert.Null(index.FindNarrowest(0));
    }

    [Fact]
    public void SourceEquivalentCopiesKeepProvenanceAndDropOldWholeExpressionForAssignments()
    {
        var receiver = new Expr.Literal(1d);
        var name = new Token(TokenType.IDENTIFIER, "x", null, 1, 2);
        var original = new Expr.Get(receiver, name);
        var replacement = new Expr.Set(receiver, name, new Expr.Literal(2d));
        var builder = new EditorSyntaxBuilder();
        builder.Record(new EditorSyntaxRecord(original, EditorSyntaxKind.Expression, new(0, 3)));
        builder.Record(new EditorSyntaxRecord(original, EditorSyntaxKind.Name, new(2, 3), EditorSyntaxRole.MemberName, Token: name));
        builder.CopyViews(original, replacement);
        builder.Record(new EditorSyntaxRecord(replacement, EditorSyntaxKind.Expression, new(0, 7)));
        var index = builder.Build(new("assignment.ts", "a.x = 2"), [new Stmt.Expression(replacement)], default);

        Assert.Equal(new SourceSpan(0, 7), Assert.Single(index.GetRecords(replacement), record => record.Kind == EditorSyntaxKind.Expression).Span);
        Assert.Equal(EditorSyntaxOrigin.SourceEquivalent, Assert.Single(index.GetRecords(replacement), record => record.Role == EditorSyntaxRole.MemberName).Origin);
        Assert.Empty(index.GetRecords(original));
    }

    [Fact]
    public void NewCalleeAndNestedAuxiliaryTypeRootsRemainReachable()
    {
        var callee = new Expr.Variable(new(TokenType.IDENTIFIER, "C", null, 1, 0));
        var leaf = new NamedTypeNode("T", null, 1);
        var parameter = new ParameterTypeNode("x", leaf, false, false, 1);
        var signature = new FunctionTypeNode(null, [parameter], leaf, 1);
        var construct = new Expr.New(callee, ["(x: T) => T"], [], [signature]);
        var builder = new EditorSyntaxBuilder();
        foreach (object node in new object[] { callee, leaf, parameter, signature })
            builder.Record(new EditorSyntaxRecord(node, EditorSyntaxKind.Type, new(0, 1)));

        var index = builder.Build(new("reachable.ts", "C"), [new Stmt.Expression(construct)], default);

        Assert.Equal(4, index.Records.Count);
    }

    [Fact]
    public void AttachedTypeSyntaxFollowsOnlyItsFinalSourceEquivalentOwner()
    {
        var original = new Expr.Literal(1d);
        var replacement = new Expr.Literal(2d);
        var abandoned = new Expr.Literal(3d);
        var leaf = new NamedTypeNode("T", null, 1);
        var outer = new ArrayTypeNode(leaf, 1);
        var lost = new NamedTypeNode("Lost", null, 1);
        var builder = new EditorSyntaxBuilder();
        builder.Attach(original, outer);
        builder.Copy(original, replacement);
        builder.Attach(abandoned, lost);
        foreach (TypeNode type in new TypeNode[] { outer, leaf, lost })
            builder.Record(new EditorSyntaxRecord(type, EditorSyntaxKind.Type, new(0, 1)));

        var index = builder.Build(new("attached.ts", "T"), [new Stmt.Expression(replacement)], default);

        Assert.Single(index.GetRecords(outer));
        Assert.Single(index.GetRecords(leaf));
        Assert.Empty(index.GetRecords(lost));
        Assert.Empty(index.GetRecords(original));
    }

    [Fact]
    public void TypeReachabilityCatalogRequiresAnExplicitDecisionForEveryTypeNode()
    {
        Type[] supported =
        [
            typeof(NamedTypeNode), typeof(LiteralTypeNode), typeof(UniqueSymbolTypeNode), typeof(ReadonlyTypeNode),
            typeof(TypePredicateNode), typeof(AssertsNonNullTypeNode), typeof(ArrayTypeNode), typeof(UnionTypeNode),
            typeof(IntersectionTypeNode), typeof(KeyofTypeNode), typeof(IndexedAccessTypeNode), typeof(ConditionalTypeNode),
            typeof(InferTypeNode), typeof(TypeQueryNode), typeof(FunctionTypeNode), typeof(ConstructorTypeNode),
            typeof(GenericFunctionTypeNode), typeof(GenericConstructorTypeNode), typeof(TemplateLiteralTypeNode),
            typeof(MappedTypeNode), typeof(TupleTypeNode), typeof(ObjectTypeNode),
        ];
        var concrete = typeof(TypeNode).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(TypeNode).IsAssignableFrom(type));
        Assert.Equal(supported.Select(type => type.FullName).Order(), concrete.Select(type => type.FullName).Order());
    }

    [Theory]
    [InlineData("type T = A<B<C>>;", "A<B<C>>", "B<C>")]
    [InlineData("type T = A<B<C<D>>>;", "A<B<C<D>>>", "C<D>")]
    public void SplitGenericClosersGiveInnerAndOuterTypesTheirExactEnds(string text, string outerText, string innerText)
    {
        var document = new SourceDocument("generic.ts", text);
        new Parser(new Lexer(text).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var typeViews = document.EditorSyntax!.Records.Where(record => record.Kind == EditorSyntaxKind.Type &&
            record.Role == EditorSyntaxRole.Whole).Select(record => text[record.Span.Start..record.Span.End]).ToArray();

        Assert.Contains(outerText, typeViews);
        Assert.Contains(innerText, typeViews);
    }

    [Fact]
    public void OptInCapturePreservesUtf16OffsetsAndDocumentOwnership()
    {
        const string text = "const emoji = \"😀\";\r\nconst same = \"😀\";";
        var first = new SourceDocument("first.ts", text);
        var second = new SourceDocument("second.ts", text);
        new Parser(new Lexer(text).ScanTokens()).WithSourceDocument(first).WithEditorSyntax().ParseOrThrow();
        new Parser(new Lexer(text).ScanTokens()).WithSourceDocument(second).WithEditorSyntax().ParseOrThrow();

        Assert.Same(first, first.EditorSyntax!.Document);
        Assert.Same(second, second.EditorSyntax!.Document);
        Assert.NotSame(first.EditorSyntax.Records[0].Node, second.EditorSyntax.Records[0].Node);
        var literal = first.EditorSyntax.FindNarrowest(text.LastIndexOf("😀", StringComparison.Ordinal), EditorSyntaxKind.Literal);
        Assert.NotNull(literal);
        Assert.Equal("\"😀\"", text[literal.Span.Start..literal.Span.End]);
        Assert.Equal(4, literal.Span.Length);
    }

    [Fact]
    public void DisabledCaptureAndCancellationDoNotRetainAnEditorCollector()
    {
        var ordinary = new SourceDocument("ordinary.ts", "const x = 1;");
        new Parser(new Lexer(ordinary.Text).ScanTokens()).WithSourceDocument(ordinary).ParseOrThrow();
        Assert.Null(ordinary.EditorSyntax);
        Assert.Null(ordinary.Spans.Copied);

        var canceled = new SourceDocument("cancel.ts", ordinary.Text);
        var parser = new Parser(new Lexer(canceled.Text).ScanTokens()).WithSourceDocument(canceled).WithEditorSyntax()
            .WithCancellation(new CancellationToken(canceled: true));
        Assert.Throws<OperationCanceledException>(() => parser.Parse());
        Assert.Null(canceled.EditorSyntax);
        Assert.Null(canceled.Spans.Copied);
    }

    [Fact]
    public void NormalizedContextualAndLiteralNamesUseOriginalTokensWithoutChangingTheAst()
    {
        const string text = "class C { get \"quoted\"(): number { return 1; } } function f(type: string) {}";
        var document = new SourceDocument("names.ts", text);
        var statements = new Parser(new Lexer(text).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var accessor = Assert.Single(Assert.IsType<Stmt.Class>(statements[0]).Accessors!);
        var function = Assert.IsType<Stmt.Function>(statements[1]);

        Assert.Equal(-1, accessor.Name.Start);
        Assert.Equal(-1, function.Parameters[0].Name.Start);
        var quoted = Assert.Single(document.EditorSyntax!.GetRecords(accessor), record => record.Role == EditorSyntaxRole.MemberName);
        Assert.Equal(TokenType.STRING, quoted.Token!.Type);
        Assert.Equal("\"quoted\"", text[quoted.Span.Start..quoted.Span.End]);
        var contextual = Assert.Single(document.EditorSyntax.GetRecords(function.Parameters[0]), record => record.Role == EditorSyntaxRole.DeclarationName);
        Assert.Equal("type", text[contextual.Span.Start..contextual.Span.End]);
    }
}
