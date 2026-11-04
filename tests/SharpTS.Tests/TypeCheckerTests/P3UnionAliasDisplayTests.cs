using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3UnionAliasDisplayTests
{
    [Fact]
    public void CallableParametersKeepTheWrittenUnionAliasWithoutRenamingEqualUnions()
    {
        const string source = """
            type Key = string | number | symbol;
            declare const aliased: (value: Key) => boolean;
            declare const structural: (value: string | number | symbol) => boolean;
            aliased; structural;
            { type Key = number | boolean; declare const shadow: (value: Key) => boolean; shadow; }
            """;
        var document = new SourceDocument("union-alias.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var observations = checker.EditorFacts.GetSourceTypeObservations(document);
        Assert.Contains(observations, fact => source[fact.Span.Start..fact.Span.End] == "aliased" && fact.Display.Text == "(value: Key) => boolean");
        Assert.Contains(observations, fact => source[fact.Span.Start..fact.Span.End] == "structural" && fact.Display.Text == "(value: string | number | symbol) => boolean");
        Assert.Contains(observations, fact => source[fact.Span.Start..fact.Span.End] == "shadow" && fact.Display.Text == "(value: Key) => boolean");
    }

    [Fact]
    public void AliasPresentationKeepsTheStructuralArgumentDomain()
    {
        const string source = "type Key=string|number|symbol;declare const f:(value:Key)=>boolean;f('x');f(1);f(Symbol());f(false);";
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        Assert.Equal("TS2345", Assert.Single(diagnostics).TsCode);
    }
}
