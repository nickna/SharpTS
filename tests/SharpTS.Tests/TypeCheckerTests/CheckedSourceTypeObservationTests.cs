using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class CheckedSourceTypeObservationTests
{
    [Fact]
    public void SourceNamesKeepTheirCheckedLexicalOwnersAndSurviveIndexReuse()
    {
        const string source = "let value: number = 1; { let value: string = 'inner'; value; } value; type Alias = number; let alias: Alias; type Lazy<T> = T;";
        var document = new SourceDocument("types.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata();
        checker.Check(statements, document);
        var facts = checker.EditorFacts.GetSourceTypeObservations(document);
        var names = facts.Where(fact => source[fact.Span.Start..fact.Span.End] == "value").ToArray();
        Assert.Equal(4, names.Length);
        Assert.Equal("number", names[0].Display.Text);
        Assert.Equal("string", names[1].Display.Text);
        Assert.Equal("string", names[2].Display.Text);
        Assert.Equal("number", names[3].Display.Text);
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "Alias" && fact.Display.Text == "number");
        Assert.DoesNotContain(facts, fact => source[fact.Span.Start..fact.Span.End] == "T");
        checker.EditorFacts.Clear();
        Assert.Equal("number", names[0].Display.Text);
        Assert.Empty(checker.EditorFacts.GetSourceTypeObservations(document));
    }

    [Fact]
    public void QueryingAnEqualButDifferentDocumentDoesNotFindFacts()
    {
        const string source = "const value = 1;";
        var document = new SourceDocument("types.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata();
        checker.Check(statements, document);
        Assert.NotEmpty(checker.EditorFacts.GetSourceTypeObservations(document));
        Assert.Empty(checker.EditorFacts.GetSourceTypeObservations(new SourceDocument(document.Path, document.Text)));
    }

    [Fact]
    public void SourceQueriesDistinguishConstructorInstanceAndPropertyNames()
    {
        const string source = "class Box { value: number = 1; } const item = new Box(); Box; item; item.value; type Lazy = Missing<number>;";
        var document = new SourceDocument("classes.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata();
        checker.Check(statements, document);
        var facts = checker.EditorFacts.GetSourceTypeObservations(document);
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "Box" && fact.Display.Text == "typeof Box");
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "item" && fact.Display.Text == "Box");
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "value" && fact.Display.Text == "number");
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "Lazy" && fact.Display.Unsupported is not null);
    }

    [Fact]
    public void ConformanceRenderingPreservesDomainsAndReportsUnsupportedTypes()
    {
        Assert.Equal("(1 | 2)[]", CheckedTypeDisplay.Render(new TypeInfo.Array(new TypeInfo.Union(
            [new TypeInfo.NumberLiteral(1), new TypeInfo.NumberLiteral(2)]))).Text);
        Assert.Equal("Generator<string, number, boolean>", CheckedTypeDisplay.Render(new TypeInfo.Generator(TypeInfo.String.Shared)
            { ReturnType = TypeInfo.Primitive.Number, NextType = TypeInfo.Primitive.Boolean }).Text);
        var unsupported = CheckedTypeDisplay.Render(TypeInfo.Inferred.Shared);
        Assert.Null(unsupported.Text);
        Assert.NotNull(unsupported.Unsupported);
    }

    [Fact]
    public void SourceFunctionNamesUseTheCheckedPublicOverloadSurface()
    {
        const string source = "function choose(value: 'a'): number; function choose(value: string): string; function choose(value: any): any { return value; } choose;";
        var document = new SourceDocument("overloads.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata();
        checker.Check(statements, document);
        var names = checker.EditorFacts.GetSourceTypeObservations(document)
            .Where(fact => source[fact.Span.Start..fact.Span.End] == "choose").ToArray();
        Assert.NotEmpty(names);
        Assert.All(names, fact => Assert.Equal("{ (value: \"a\"): number; (value: string): string; }", fact.Display.Text));
    }

    [Fact]
    public void RecoveryHolesNeverBecomeSuccessfulSourceTypeObservations()
    {
        const string source = "const value = 1; function f(input: number): number { return input; } f(";
        var parsed = Parser.ParseForEditor(new("recovered-types.ts", source), source.Length,
            EditorQueryKind.SignatureHelp, EditorRecoveryPolicy.Default);
        Assert.True(parsed.IsRecovered);
        var checker = new TypeChecker().WithEditorMetadata();
        checker.CheckWithRecovery(parsed.ParseResult.Statements, parsed.Document);
        var facts = checker.EditorFacts.GetSourceTypeObservations(parsed.Document);
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "value" && fact.Display.Text == "1");
        Assert.All(facts, fact => Assert.True(fact.Span.Start >= 0 && fact.Span.End <= source.Length));
        Assert.DoesNotContain(facts, fact => source[fact.Span.Start..fact.Span.End] == "f(" && fact.Display.Text is not null);
    }
}
