using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DoWhileExitEditorFactsTests
{
    [Fact]
    public void CallableObjectDomainProjectionKeepsOnlyTheTwoCheckedInvocations()
    {
        const string source = """
            interface Reader{<T>(value:T):T;}
            declare const reader:Reader;
            const first:string=reader('first');
            declare const choose:boolean;
            let source:string|number='s';
            let target:string|number|boolean='s';
            do {
                if(choose){target=reader(source);break;}
                source=1;target=1;
            } while(true);
            const checked:string|number=target;
            """;
        var document = new SourceDocument("do-while-callable-object.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var facts = checker.EditorFacts.Freeze();
        Assert.Equal(2, facts.Invocations.Count);
        var calls = document.EditorSyntax!.Invocations.Select(invocation =>
            Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocation.Owner))).ToArray();
        Assert.Equal(2, calls.Length);
        var first = Assert.Single(calls[0].Candidates);
        var inside = Assert.Single(calls[1].Candidates);
        Assert.Equal(first.OriginalSignatureId, inside.OriginalSignatureId);
        if (first.Origin is { } origin) Assert.Same(origin.Owner, inside.Origin!.Owner);
        else Assert.Null(inside.Origin);
        Assert.All(calls, call =>
        {
            Assert.Equal(EditorInvocationStatus.Selected, call.Status);
            Assert.True(call.IsComplete);
            Assert.True(Assert.Single(call.Candidates).Declared.IsAvailable);
            Assert.True(call.SelectedSignature!.IsAvailable);
        });
    }

    [Fact]
    public void ReturnedCallableKeepsItsProvenSignatureWithoutRecheckingAnInvocation()
    {
        const string source = """
            declare const choose:boolean;
            function make<T>(value:T):()=>T{return()=>value;}
            let source:string|number='s';
            let reader:(()=>string|number)|boolean=()=>source;
            do {
                if(choose){reader=make(source);break;}
                source=1;reader=()=>source;
            } while(true);
            const value:string|number=/*call*/reader();
            """;
        var document = new SourceDocument("do-while-returned-callable.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var facts = checker.EditorFacts.Freeze();
        Assert.Equal(2, facts.Invocations.Count);
        int offset = source.IndexOf("/*call*/", StringComparison.Ordinal) + "/*call*/".Length;
        var syntax = document.EditorSyntax!.Invocations.Where(invocation => invocation.Span.Contains(offset))
            .OrderBy(invocation => invocation.Span.Length).First();
        var call = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, syntax.Owner));
        Assert.True(call.IsComplete);
        Assert.Equal(EditorInvocationStatus.Selected, call.Status);
        Assert.True(Assert.Single(call.Candidates).Declared.IsAvailable);
        Assert.True(call.SelectedSignature!.IsAvailable);
        Assert.Contains("string", call.SelectedSignature.ReturnType.Text, StringComparison.Ordinal);
        Assert.Contains("number", call.SelectedSignature.ReturnType.Text, StringComparison.Ordinal);
    }
}
