using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3SelectedMappedAliasDisplayTests
{
    [Fact]
    public void ConditionalResultsRetainTheSelectedMappedDeclarationAndArguments()
    {
        const string source = """
            type ConvertedObject<T extends object> = {[K in keyof T]: Converted<T[K]>};
            type Converted<T> = T extends string | number | boolean | null ? T
              : T extends undefined | Function ? never
              : T extends {toJSON(): infer R} ? R
              : T extends object ? ConvertedObject<T> : "unknown";
            declare class Custom {toJSON(): "correct";}
            type Example = {custom: Custom; nested: {value: Custom}};
            declare const value: Converted<Example>;
            const leaf = value.custom;
            const nested = value.nested;
            const checked: 'correct' = nested.value;
            """;
        var document = new SourceDocument("mapped-alias.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var facts = checker.EditorFacts.GetSourceTypeObservations(document);
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "value" && fact.Display.Text == "ConvertedObject<Example>");
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "leaf" && fact.Display.Text == "\"correct\"");
        Assert.Contains(facts, fact => source[fact.Span.Start..fact.Span.End] == "nested" && fact.Display.Text == "ConvertedObject<{ value: Custom; }>");
    }
}
