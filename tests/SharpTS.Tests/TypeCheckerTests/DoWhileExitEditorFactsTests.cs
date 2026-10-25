using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DoWhileExitEditorFactsTests
{
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
