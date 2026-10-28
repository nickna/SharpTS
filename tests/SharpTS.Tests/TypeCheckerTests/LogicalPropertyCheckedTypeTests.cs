using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LogicalPropertyCheckedTypeTests
{
    [Fact]
    public void PinnedPropertyAssignmentResultsAndMemberReadsHaveDifferentCheckedTypes()
    {
        const string source = "declare let x:{a?:boolean}; x.a ??= true; x.a &&= false;";
        var document = new SourceDocument("logical-assignment.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(strictNullChecks: true).WithEditorMetadata(); checker.Check(statements, document);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var observations = checker.EditorFacts.GetSourceTypeObservations(document);
        Assert.Contains(observations, fact => source[fact.Span.Start..fact.Span.End] == "x.a ??= true" && fact.Display.Text == "boolean");
        Assert.Contains(observations, fact => source[fact.Span.Start..fact.Span.End] == "x.a &&= false" && fact.Display.Text == "false | undefined");
        var members = observations.Where(fact => source[fact.Span.Start..fact.Span.End] is "a" or "x.a").ToArray();
        Assert.Equal(4, members.Length); Assert.All(members, fact => Assert.Equal("boolean | undefined", fact.Display.Text));
        checker.EditorFacts.ClearDocument(document);
        Assert.Empty(checker.EditorFacts.GetSourceTypeObservations(document));
    }

    [Theory, ModeData]
    public void RuntimeStillEvaluatesReceiversAndRightSidesOnlyWhenRequired(ExecutionMode mode)
    {
        const string source = """
            let reads=0,writes=0,right=0;
            const target={ get a():boolean|undefined { reads++;return undefined; },set a(value:boolean|undefined){ writes++; } };
            function value():boolean {right++;return true;}
            console.log(target.a ??= value(),target.a &&= value());
            console.log(reads,writes,right);
            """;
        Assert.Equal("true undefined\n2 1 1\n", TestHarness.Run(source, mode));
    }
}
