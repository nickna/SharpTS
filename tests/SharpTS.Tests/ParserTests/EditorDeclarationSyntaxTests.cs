using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class EditorDeclarationSyntaxTests
{
    private static (List<Stmt> Statements, SourceDocument Document, EditorSyntaxIndex Syntax) Parse(
        string source, JsxParseOptions? jsx = null)
    {
        var document = new SourceDocument(jsx is null ? "declarations.ts" : "declarations.tsx", source);
        var parser = new Parser(new Lexer(source) { JsxTolerant = jsx is not null }.ScanTokens())
            .WithSourceDocument(document).WithEditorSyntax();
        if (jsx is not null) parser.WithJsx(source, jsx);
        var statements = parser.ParseOrThrow();
        return (statements, document, Assert.IsType<EditorSyntaxIndex>(document.EditorSyntax));
    }

    private static string Text(SourceDocument document, EditorSyntaxRecord record) =>
        document.Text[record.Span.Start..record.Span.End];

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void WrittenTypesKeepGroupingAndPartialGenericCloserRanges(string newline)
    {
        string source = "// 😀" + newline + "type Result = Box< Inner< ( string /*keep*/ | number ) > >;";
        var parsed = Parse(source);
        var alias = Assert.IsType<Stmt.TypeAlias>(Assert.Single(parsed.Statements));
        var outer = Assert.IsType<NamedTypeNode>(alias.TypeDefinitionNode);
        var inner = Assert.IsType<NamedTypeNode>(Assert.Single(outer.TypeArguments!));
        var union = Assert.IsType<UnionTypeNode>(Assert.Single(inner.TypeArguments!));
        Assert.Equal("Box< Inner< ( string /*keep*/ | number ) > >",
            Text(parsed.Document, Assert.Single(parsed.Syntax.GetRecords(outer), record => record.Role == EditorSyntaxRole.Annotation)));
        Assert.Contains(parsed.Syntax.GetRecords(union), record => record.Kind == EditorSyntaxKind.Grouping &&
            Text(parsed.Document, record) == "( string /*keep*/ | number )");
        Assert.Equal("string /*keep*/ | number", parsed.Document.Text[parsed.Document.Spans.GetSpan(union)!.Value.Start..parsed.Document.Spans.GetSpan(union)!.Value.End]);
    }

    [Theory]
    [InlineData("type T = A<B<C>>;", "B<C>")]
    [InlineData("type T = A<B<C<D>>>;", "B<C<D>>")]
    public void FusedGenericClosersAreAttributedOneCharacterAtATime(string source, string expected)
    {
        var parsed = Parse(source);
        var outer = Assert.IsType<NamedTypeNode>(Assert.IsType<Stmt.TypeAlias>(Assert.Single(parsed.Statements)).TypeDefinitionNode);
        var inner = Assert.Single(outer.TypeArguments!);
        var span = parsed.Document.Spans.GetSpan(inner)!.Value;
        Assert.Equal(expected, source[span.Start..span.End]);
    }

    [Fact]
    public void ImplicitAnyHasNoWrittenRangeButItsParameterHasAName()
    {
        var parsed = Parse("type Callback = (value) => number; type Object = { implicit; explicit: string };");
        var callback = Assert.IsType<FunctionTypeNode>(Assert.IsType<Stmt.TypeAlias>(parsed.Statements[0]).TypeDefinitionNode);
        var parameter = Assert.Single(callback.Parameters);
        Assert.Empty(parsed.Syntax.GetRecords(parameter.Type));
        Assert.False(parsed.Document.Spans.TryGetSpan(parameter.Type, out _));
        Assert.Contains(parsed.Syntax.GetRecords(parameter), record => record.Role == EditorSyntaxRole.DeclarationName && Text(parsed.Document, record) == "value");
        var objectType = Assert.IsType<ObjectTypeNode>(Assert.IsType<Stmt.TypeAlias>(parsed.Statements[1]).TypeDefinitionNode);
        var implicitMember = Assert.IsType<PropertyMemberNode>(objectType.Members[0]);
        Assert.Empty(parsed.Syntax.GetRecords(implicitMember.Type));
        Assert.Contains(parsed.Syntax.GetRecords(implicitMember), record => record.Role == EditorSyntaxRole.MemberName && Text(parsed.Document, record) == "implicit");
    }

    [Fact]
    public void HeadersBodiesParametersAndPrivateAnnotationTwinsSurviveMethodCopies()
    {
        const string source = "class Container { #value: Box<string>; static map(type: string): Box<string> { return make(type); } #read(arg: number): string { return this.#value; } set value(input: string) { consume(input); } }";
        var parsed = Parse(source);
        var @class = Assert.IsType<Stmt.Class>(Assert.Single(parsed.Statements));
        Assert.Contains(parsed.Syntax.GetRecords(@class), record => record.Role == EditorSyntaxRole.Header && Text(parsed.Document, record) == "class Container ");
        Assert.Contains(parsed.Syntax.GetRecords(@class.Fields[0]), record => record.Role == EditorSyntaxRole.PrivateName && Text(parsed.Document, record) == "#value");
        Assert.Null(@class.Fields[0].TypeAnnotationNode);
        Assert.Contains(parsed.Syntax.Records, record => record.Kind == EditorSyntaxKind.Type && record.Role == EditorSyntaxRole.Annotation &&
            record.Span.Start == source.IndexOf("Box<string>", StringComparison.Ordinal) && Text(parsed.Document, record) == "Box<string>");
        var method = @class.Methods.Single(method => method.Name.Lexeme == "map");
        Assert.Contains(parsed.Syntax.GetRecords(method), record => record.Kind == EditorSyntaxKind.ParameterList && Text(parsed.Document, record) == "(type: string)");
        Assert.Contains(parsed.Syntax.GetRecords(method), record => record.Role == EditorSyntaxRole.Body && Text(parsed.Document, record) == "{ return make(type); }");
        Assert.Contains(parsed.Syntax.GetRecords(method.Parameters[0]), record => record.Role == EditorSyntaxRole.DeclarationName && record.Token!.Start == source.IndexOf("type:", StringComparison.Ordinal));
        var privateMethod = @class.Methods.Single(method => method.IsPrivate);
        Assert.Null(privateMethod.ReturnTypeNode);
        Assert.Contains(parsed.Syntax.Records, record => record.Kind == EditorSyntaxKind.Type && record.Role == EditorSyntaxRole.Annotation &&
            record.Span.Start == source.IndexOf("): string", StringComparison.Ordinal) + 3 && Text(parsed.Document, record) == "string");
        var accessor = Assert.Single(@class.Accessors!);
        Assert.Contains(parsed.Syntax.GetRecords(accessor), record => record.Kind == EditorSyntaxKind.ParameterList && Text(parsed.Document, record) == "(input: string)");
    }

    [Fact]
    public void ComputedMembersDoNotExposeFabricatedNames()
    {
        var parsed = Parse("class C { [key](): number { return 1; } [key]: string; get [key](): string { return ''; } }");
        Assert.DoesNotContain(parsed.Syntax.Records, record => record.Token?.Lexeme == "<computed>");
        Assert.DoesNotContain(parsed.Syntax.Records, record => record.Token is { Start: < 0 } && record.IsAuthoritative);
    }

    [Fact]
    public void FailedGenericLookaheadRestoresFusedTokensAndDoesNotLeakTypeViews()
    {
        const string source = "a < B<C>> d; later();";
        var parsed = Parse(source);
        var expression = Assert.IsType<Stmt.Expression>(parsed.Statements[0]).Expr;
        var span = parsed.Document.Spans.GetSpan(expression)!.Value;
        Assert.Equal("a < B<C>> d", source[span.Start..span.End]);
        Assert.DoesNotContain(parsed.Syntax.Records, record => record.Kind == EditorSyntaxKind.Type);
        var invocation = Assert.Single(parsed.Syntax.Invocations);
        Assert.Equal("later()", source[invocation.Span.Start..invocation.Span.End]);
    }

    [Theory]
    [InlineData(JsxMode.React)]
    [InlineData(JsxMode.ReactJsx)]
    [InlineData(JsxMode.ReactJsxDev)]
    public void TsxQueriesUseWrittenAttributesAndEmbeddedCallsWithoutFactoryContexts(JsxMode mode)
    {
        const string source = "const view = <UI.Card data-id=\"😀\" key={identify(1)}>{render(nested(2))}<span> hello </span></UI.Card>;";
        var parsed = Parse(source, new JsxParseOptions(mode));
        var root = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Const>(parsed.Statements.Last()).Initializer);
        Assert.NotNull(root.JsxOrigin);
        Assert.Contains(parsed.Syntax.GetRecords(root), record => record.Kind == EditorSyntaxKind.JsxElement && Text(parsed.Document, record) == source[13..^1]);
        Assert.Equal(new[] { "identify(1)", "render(nested(2))", "nested(2)" }, parsed.Syntax.Invocations.Select(call => source[call.Span.Start..call.Span.End]));
        Assert.Contains(parsed.Syntax.Records, record => record.Kind == EditorSyntaxKind.Name && Text(parsed.Document, record) == "data-id");
        Assert.Contains(parsed.Syntax.Records, record => record.Kind == EditorSyntaxKind.Literal && Text(parsed.Document, record) == "\"😀\"");
        Assert.DoesNotContain(parsed.Syntax.Records, record => record.Token?.Lexeme.StartsWith("__sharpts_", StringComparison.Ordinal) == true);
        Assert.True(parsed.Document.Spans.GetSpan(root.Callee)!.Value.IsHidden);
        Assert.True(parsed.Document.Spans.GetSpan(root.Arguments[1])!.Value.IsHidden);
        Assert.Equal("nested(2)", source[parsed.Syntax.FindInvocation(source.IndexOf("2)", StringComparison.Ordinal))!.Span.Start..parsed.Syntax.FindInvocation(source.IndexOf("2)", StringComparison.Ordinal))!.Span.End]);
    }

    [Fact]
    public void OrdinaryParserDoesNotCaptureEditorViewsOrChangeStatementSpans()
    {
        const string source = "function f(value: string) { return value; }";
        var document = new SourceDocument("ordinary.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        Assert.Null(document.EditorSyntax);
        var function = Assert.IsType<Stmt.Function>(Assert.Single(statements));
        Assert.Equal(new SourceSpan(0, source.Length), document.Spans.GetSpan(function));
        Assert.False(document.Spans.TryGetSpan(function.Parameters[0].TypeAnnotationNode!, out _));
    }

    [Fact]
    public void TsxGenericAnnotationsRemainReachableThroughLoweredJsxMetadata()
    {
        const string source = "const view = <Component<Box<string>> value={value} />;";
        var parsed = Parse(source, JsxParseOptions.Default);
        var root = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Const>(parsed.Statements.Last()).Initializer);
        var type = Assert.Single(root.JsxOrigin!.TypeArgumentNodes!);
        Assert.Contains(parsed.Syntax.GetRecords(type!), record => record.Kind == EditorSyntaxKind.Type && Text(parsed.Document, record) == "Box<string>");
    }

    [Fact]
    public void RecoveredTsxRootCannotImpersonateWrittenWholeExpression()
    {
        var parsed = Parser.ParseForEditor(new SourceDocument("recovered.tsx", "const view = <span>"), 19,
            EditorQueryKind.Syntax, EditorRecoveryPolicy.Default);
        var root = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Const>(Assert.Single(parsed.ParseResult.Statements, statement => statement is Stmt.Const)).Initializer);
        Assert.DoesNotContain(parsed.Syntax.GetRecords(root), record => record.IsAuthoritative);
    }

    [Fact]
    public void SyntaxOnlyPrivateAnnotationDoesNotChangeCheckerInputsOrDiagnostics()
    {
        const string source = "class Container { #value: Container[\"#value\"]; }";
        var ordinary = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var editor = Parse(source);
        Assert.Null(Assert.IsType<Stmt.Class>(Assert.Single(ordinary)).Fields[0].TypeAnnotationNode);
        Assert.Null(Assert.IsType<Stmt.Class>(Assert.Single(editor.Statements)).Fields[0].TypeAnnotationNode);
        var ordinaryResult = new TypeChecker().CheckWithRecovery(ordinary);
        var editorResult = new TypeChecker().CheckWithRecovery(editor.Statements);
        Assert.Equal(ordinaryResult.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)),
            editorResult.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)));
        Assert.Contains(editor.Syntax.Records, record => record.Kind == EditorSyntaxKind.Type && record.Role == EditorSyntaxRole.Annotation &&
            Text(editor.Document, record) == "Container[\"#value\"]");
    }
}
