using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class EditorExpressionTests
{
    [Fact]
    public void EveryExpressionFamilyHasAnExplicitProvenanceClassification()
    {
        // DestructuringAssign is a source-equivalent parser lowering. Call/ArrowFunction and
        // literals can also be synthesized; those instances must be hidden or explicitly copied.
        // Adding an AST family requires choosing its source policy and extending the corpus.
        Type[] written =
        [
            typeof(Expr.Comma), typeof(Expr.Binary), typeof(Expr.Logical), typeof(Expr.NullishCoalescing),
            typeof(Expr.Ternary), typeof(Expr.Grouping), typeof(Expr.Literal), typeof(Expr.Unary),
            typeof(Expr.Delete), typeof(Expr.Variable), typeof(Expr.Assign), typeof(Expr.Call),
            typeof(Expr.Get), typeof(Expr.Set), typeof(Expr.GetPrivate), typeof(Expr.PrivateIn),
            typeof(Expr.SetPrivate), typeof(Expr.CallPrivate), typeof(Expr.This), typeof(Expr.New),
            typeof(Expr.ArrayLiteral), typeof(Expr.ObjectLiteral), typeof(Expr.GetIndex), typeof(Expr.SetIndex),
            typeof(Expr.Super), typeof(Expr.CompoundAssign), typeof(Expr.CompoundSet), typeof(Expr.CompoundSetIndex),
            typeof(Expr.LogicalAssign), typeof(Expr.LogicalSet), typeof(Expr.LogicalSetIndex),
            typeof(Expr.PrefixIncrement), typeof(Expr.PostfixIncrement), typeof(Expr.ArrowFunction),
            typeof(Expr.TemplateLiteral), typeof(Expr.TaggedTemplateLiteral), typeof(Expr.Spread),
            typeof(Expr.TypeAssertion), typeof(Expr.Satisfies), typeof(Expr.Await), typeof(Expr.DynamicImport),
            typeof(Expr.ImportMeta), typeof(Expr.Yield), typeof(Expr.RegexLiteral),
            typeof(Expr.NonNullAssertion), typeof(Expr.ClassExpr),
        ];
        Type[] sourceEquivalentLowerings = [typeof(Expr.DestructuringAssign)];
        Assert.Equal(AstNodeCatalog.ExprTypes.OrderBy(type => type.FullName),
            written.Concat(sourceEquivalentLowerings).OrderBy(type => type.FullName));
    }

    private static EditorParseArtifact Parse(string source, EditorQueryKind kind = EditorQueryKind.Syntax,
        string path = "editor.ts", EditorRecoveryPolicy? policy = null, JsxParseOptions? jsx = null)
    {
        int caret = source.IndexOf('|');
        if (caret >= 0) source = source.Remove(caret, 1);
        else caret = source.Length;
        return Parser.ParseForEditor(new SourceDocument(path, source), caret, kind,
            policy ?? EditorRecoveryPolicy.Default, jsxOptions: jsx);
    }

    private static string Text(EditorParseArtifact parsed, object node)
    {
        Assert.True(parsed.Document.Spans.TryGetSpan(node, out SourceSpan span));
        Assert.False(span.IsHidden);
        return parsed.Document.Text[span.Start..span.End];
    }

    [Fact]
    public void IntermediateMemberAndCallNodesKeepTheirExactRanges()
    {
        var parsed = Parse("a.b.c(1).d;");
        Assert.True(parsed.ParseResult.IsSuccess);
        var final = Assert.IsType<Expr.Get>(Assert.IsType<Stmt.Expression>(Assert.Single(parsed.ParseResult.Statements)).Expr);
        var call = Assert.IsType<Expr.Call>(final.Object);
        var second = Assert.IsType<Expr.Get>(call.Callee);
        var first = Assert.IsType<Expr.Get>(second.Object);
        Assert.Equal("a.b.c(1).d", Text(parsed, final));
        Assert.Equal("a.b.c(1)", Text(parsed, call));
        Assert.Equal("a.b.c", Text(parsed, second));
        Assert.Equal("a.b", Text(parsed, first));
        Assert.Equal("a", Text(parsed, first.Object));
        Assert.Equal(new[] { "b", "c", "d" }, parsed.Syntax.Members.Select(member => member.Name.Lexeme));
    }

    [Theory]
    [InlineData("a + b * c")]
    [InlineData("a ** b ** c")]
    [InlineData("a ? b : c")]
    [InlineData("a ?? b")]
    [InlineData("a && b")]
    [InlineData("a == b")]
    [InlineData("a << b")]
    [InlineData("++a")]
    [InlineData("a++")]
    [InlineData("delete a.b")]
    [InlineData("(a as number)!")]
    [InlineData("a satisfies number")]
    public void ExpressionProductionsKeepWrittenExtent(string expression)
    {
        var parsed = Parse(expression + ";");
        Assert.True(parsed.ParseResult.IsSuccess);
        Expr node = Assert.IsType<Stmt.Expression>(Assert.Single(parsed.ParseResult.Statements)).Expr;
        Assert.Equal(expression, Text(parsed, node));
        Assert.Contains(parsed.Syntax.GetRecords(node), record => record.Kind == EditorSyntaxKind.Expression &&
            record.Span == new SourceSpan(0, expression.Length) && record.IsAuthoritative);
    }

    [Theory]
    [InlineData("value = other")]
    [InlineData("value += other")]
    [InlineData("value &&= other")]
    public void AssignmentReplacementRetainsItsLexicalName(string source)
    {
        var parsed = Parse(source + ";");
        var name = parsed.Syntax.FindNarrowest(1, role: EditorSyntaxRole.Name);
        Assert.NotNull(name);
        Assert.Equal("value", name!.Token!.Lexeme);
        Assert.Equal(new SourceSpan(0, 5), name.Span);
    }

    [Fact]
    public void AssignmentReplacementRetainsLiteralMemberTokenAndReceiver()
    {
        var parsed = Parse("receiver['field'] += value;");
        Assert.True(parsed.ParseResult.IsSuccess);
        var member = Assert.Single(parsed.Syntax.Members);
        Assert.IsType<Expr.CompoundSetIndex>(member.Owner);
        Assert.True(member.IsIndex);
        Assert.Equal("'field'", parsed.Document.Text[member.Name.Start..member.Name.End]);
        Assert.Equal("receiver", Text(parsed, member.Receiver));
        Assert.Equal("receiver['field'] += value", Text(parsed, member.Owner));
    }

    [Fact]
    public void PrivateAccessAndBrandCheckKeepWrittenNames()
    {
        var parsed = Parse("class C { #field = 1; m(other: C) { other.#field = 2; return #field in other; } }");
        Assert.True(parsed.ParseResult.IsSuccess);
        Assert.Contains(parsed.Syntax.Members, member => member.IsPrivate && member.Owner is Expr.SetPrivate &&
            member.Name.Lexeme == "#field");
        Assert.Contains(parsed.Syntax.Records, record => record.Node is Expr.PrivateIn &&
            record.Role == EditorSyntaxRole.PrivateName && record.Token!.Lexeme == "#field");
    }

    [Fact]
    public void InvocationSeparatorsExcludeNestedCommasAndLiteralContents()
    {
        const string source = "outer<Map<string, number>>('a,b', inner(1, 2), [3, 4]);";
        var parsed = Parse(source);
        Assert.True(parsed.ParseResult.IsSuccess);
        var outer = parsed.Syntax.Invocations.Single(call => call.Callee is Expr.Variable v && v.Name.Lexeme == "outer");
        Assert.Equal(2, outer.Commas.Count);
        Assert.Equal("(", source[outer.OpenParen.Start..outer.OpenParen.End]);
        Assert.Equal(")", source[outer.CloseParen!.Value.Start..outer.CloseParen.Value.End]);
        int innerOffset = source.IndexOf("2)", StringComparison.Ordinal);
        var inner = parsed.Syntax.FindInvocation(innerOffset);
        Assert.IsType<Expr.Variable>(inner!.Callee);
        Assert.Equal("inner", ((Expr.Variable)inner.Callee).Name.Lexeme);
        Assert.Equal(1, inner.GetActiveArgumentIndex(innerOffset));
    }

    [Fact]
    public void NewAndOptionalCallsKeepDelimitersAndFlags()
    {
        var parsed = Parse("new ns.C<number>(1, 2); fn?.(3); obj?.['key'];");
        Assert.True(parsed.ParseResult.IsSuccess);
        Assert.Contains(parsed.Syntax.Invocations, invocation => invocation.IsNew && invocation.Commas.Count == 1);
        Assert.Contains(parsed.Syntax.Invocations, invocation => invocation.IsOptional && !invocation.IsNew);
        Assert.Contains(parsed.Syntax.Members, member => member.IsIndex && member.IsOptional && member.Name.Literal?.ToString() == "key");
    }

    [Theory]
    [InlineData("x => x + 1", "x", "x =>", "x + 1")]
    [InlineData("(x: number) => x", "(x: number)", "(x: number) =>", "x")]
    [InlineData("async (x: number) => x", "(x: number)", "async (x: number) =>", "x")]
    [InlineData("function named(x: number): number { return x; }", "(x: number)", "function named(x: number): number", "{ return x; }")]
    public void FunctionExpressionHeaderAndBodyViewsKeepSource(string expression, string parameters, string header, string body)
    {
        var parsed = Parse("const fn = " + expression + ";");
        Assert.True(parsed.ParseResult.IsSuccess);
        var arrow = Assert.IsType<Expr.ArrowFunction>(Assert.IsType<Stmt.Const>(Assert.Single(parsed.ParseResult.Statements)).Initializer);
        var views = parsed.Syntax.GetRecords(arrow);
        Assert.Contains(views, view => view.Kind == EditorSyntaxKind.ParameterList &&
            parsed.Document.Text[view.Span.Start..view.Span.End] == parameters);
        Assert.Contains(views, view => view.Role == EditorSyntaxRole.Header &&
            parsed.Document.Text[view.Span.Start..view.Span.End] == header);
        Assert.Contains(views, view => view.Role == EditorSyntaxRole.Body &&
            parsed.Document.Text[view.Span.Start..view.Span.End] == body);
        Assert.Contains(parsed.Syntax.Records, view => ReferenceEquals(view.Node, arrow.Parameters[0]) &&
            view.Role == EditorSyntaxRole.DeclarationName && view.Token!.Lexeme == "x");
    }

    [Fact]
    public void ObjectShorthandMethodsAndAccessorsKeepSourceOwners()
    {
        const string source = "const object = { value, get read(): number { return value; }, set write(next: number) { value = next; }, method(x: number) { return x; } };";
        var parsed = Parse(source);
        Assert.True(parsed.ParseResult.IsSuccess);
        var literal = Assert.IsType<Expr.ObjectLiteral>(Assert.IsType<Stmt.Const>(Assert.Single(parsed.ParseResult.Statements)).Initializer);
        var shorthand = Assert.IsType<Expr.Variable>(literal.Properties[0].Value);
        Assert.Equal("value", Text(parsed, shorthand));
        Assert.Contains(parsed.Syntax.GetRecords(shorthand), view => view.Role == EditorSyntaxRole.Name &&
            view.Token!.Lexeme == "value" && view.IsAuthoritative);

        string[] headers = ["get read(): number", "set write(next: number)", "method(x: number)"];
        string[] parameterLists = ["()", "(next: number)", "(x: number)"];
        for (int index = 1; index < literal.Properties.Count; index++)
        {
            var function = Assert.IsType<Expr.ArrowFunction>(literal.Properties[index].Value);
            Assert.StartsWith(headers[index - 1], Text(parsed, function));
            var views = parsed.Syntax.GetRecords(function);
            Assert.Contains(views, view => view.Role == EditorSyntaxRole.Header &&
                source[view.Span.Start..view.Span.End] == headers[index - 1]);
            Assert.Contains(views, view => view.Kind == EditorSyntaxKind.ParameterList &&
                source[view.Span.Start..view.Span.End] == parameterLists[index - 1]);
            Assert.Contains(views, view => view.Role == EditorSyntaxRole.Body &&
                source[view.Span.Start] == '{' && source[view.Span.End - 1] == '}');
        }

        var setter = Assert.IsType<Expr.ArrowFunction>(literal.Properties[2].Value);
        Assert.Contains(parsed.Syntax.GetRecords(setter.Parameters[0]), view =>
            view.Role == EditorSyntaxRole.DeclarationName && view.Token!.Lexeme == "next");
    }

    [Theory]
    [InlineData("<T>(x: T): T => x", "<T>(x: T): T =>")]
    [InlineData("async <T>(x: T): T => x", "async <T>(x: T): T =>")]
    public void GenericArrowCloneKeepsWholeHeaderAndParameterViews(string expression, string header)
    {
        var parsed = Parse("const identity = " + expression + ";");
        Assert.True(parsed.ParseResult.IsSuccess);
        var arrow = Assert.IsType<Expr.ArrowFunction>(Assert.IsType<Stmt.Const>(Assert.Single(parsed.ParseResult.Statements)).Initializer);
        Assert.Equal(expression, Text(parsed, arrow));
        Assert.Contains(parsed.Syntax.GetRecords(arrow), view => view.Role == EditorSyntaxRole.Header &&
            parsed.Document.Text[view.Span.Start..view.Span.End] == header);
        Assert.Contains(parsed.Syntax.GetRecords(arrow), view => view.Kind == EditorSyntaxKind.ParameterList &&
            parsed.Document.Text[view.Span.Start..view.Span.End] == "(x: T)");
    }

    [Fact]
    public void UnicodeAndCrLfOffsetsUseOriginalUtf16Source()
    {
        const string source = "const emoji = '😀';\r\nreceiver /* comment */ . field('😀');";
        var parsed = Parse(source);
        Assert.True(parsed.ParseResult.IsSuccess);
        var member = Assert.Single(parsed.Syntax.Members);
        Assert.Equal(source.IndexOf("field", StringComparison.Ordinal), member.Name.Start);
        Assert.Equal((2, 26), parsed.Document.Lines.ToPosition(member.Name.Start));
        Assert.Equal("receiver /* comment */ . field", Text(parsed, member.Owner));
    }

    [Fact]
    public void OrdinaryParsingKeepsStatementSpansWithoutEditorExpressionCapture()
    {
        const string source = "const value = receiver.field(1);";
        var document = new SourceDocument("ordinary.ts", source);
        var parser = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document);
        var statement = Assert.IsType<Stmt.Const>(Assert.Single(parser.ParseOrThrow()));
        Assert.Null(document.EditorSyntax);
        Assert.Null(document.Spans.GetSpan(statement.Initializer!));
        Assert.Equal(new SourceSpan(0, source.Length), document.Spans.GetSpan(statement));
    }

    [Fact]
    public void ResolvedJsxOptionsDoNotEnableJsxForTsSource()
    {
        var parsed = Parse("const value = <number>original;", jsx: JsxParseOptions.Default);
        Assert.True(parsed.ParseResult.IsSuccess);
        Assert.IsType<Expr.TypeAssertion>(Assert.IsType<Stmt.Const>(Assert.Single(parsed.ParseResult.Statements)).Initializer);
    }

    [Fact]
    public void TsxRecoveryUsesFilePragmas()
    {
        var parsed = Parse("/** @jsx custom.make */\nconst value = <Comp />;", path: "editor.tsx");
        Assert.True(parsed.ParseResult.IsSuccess);
        var jsx = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Const>(Assert.Single(parsed.ParseResult.Statements)).Initializer);
        Assert.Equal("custom.make", jsx.JsxOrigin!.FactoryName);
    }
}
