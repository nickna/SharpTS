using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorInvocationEligibilityTests
{
    private sealed record Checked(SourceDocument Document, TypeCheckDiagnosticResult Result,
        FrozenEditorSemanticIndex Facts);

    private static Checked Check(string source, bool capture = true, bool recover = false)
    {
        var document = new SourceDocument("signature eligibility.ts", source);
        List<Stmt> statements;
        if (recover)
        {
            var parsed = Parser.ParseForEditor(document, source.Length, EditorQueryKind.SignatureHelp,
                EditorRecoveryPolicy.Default);
            document = parsed.Document;
            statements = parsed.ParseResult.Statements;
        }
        else statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata(capture);
        var result = checker.CheckWithRecovery(statements, document);
        return new(document, result, checker.EditorFacts.Freeze());
    }

    private static EditorInvocationFact Call(Checked value, string marker = "call")
    {
        int offset = value.Document.Text.IndexOf("/*" + marker + "*/", StringComparison.Ordinal) + marker.Length + 4;
        var syntax = value.Document.EditorSyntax!.Invocations.Where(invocation => invocation.Span.Contains(offset))
            .OrderBy(invocation => invocation.Span.Length).First();
        return Assert.IsType<EditorInvocationFact>(value.Facts.GetInvocation(value.Document, syntax.Owner));
    }

    private static void Unavailable(EditorInvocationFact fact)
    {
        Assert.NotEmpty(fact.Candidates);
        Assert.All(fact.Candidates, candidate => Assert.False(candidate.Declared.IsAvailable));
        Assert.Equal(EditorInvocationStatus.Unavailable, fact.Status);
        Assert.False(fact.IsComplete);
        Assert.Null(fact.SelectedOrdinal);
        Assert.Null(fact.SelectedSignature);
    }

    [Theory]
    [InlineData("declare function f(input: Missing): number; /*call*/f(1);")]
    [InlineData("declare function f(input: number): Missing; /*call*/f(1);")]
    [InlineData("const f = (input: Missing): number => 1; /*call*/f(1);")]
    [InlineData("let f: (input: Missing) => number; /*call*/f(1);")]
    [InlineData("interface Factory { new (input: Missing): object; } let f: Factory; /*call*/new f(1);")]
    public void UnknownSignatureAnnotationsCannotBecomeAnyCandidates(string source)
    {
        var value = Check(source);
        var ordinary = Check(source, capture: false);
        Assert.Equal(ordinary.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)),
            value.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)));
        Assert.Empty(ordinary.Facts.Invocations);
        Unavailable(Call(value));
    }

    [Fact]
    public void ReturnedCallableNeedsItsOwnProofEvenWhenTheCalleeHasNoDeclarationOrigin()
    {
        var value = Check("declare function factory(): (input: Missing) => number; const fn = factory(); /*call*/fn(1);");
        var call = Call(value);
        Assert.Null(Assert.Single(call.Candidates).Origin);
        // The inferred variable has no annotation of its own; only the exact returned
        // signature's resolution can distinguish this fallback from legitimate any.
        Unavailable(call);
    }

    [Fact]
    public void PreparatoryAliasExpansionCannotLaunderAnUnprovenCachedSignature()
    {
        const string source = "type Broken = Missing; type Callable = (input: Broken) => number; " +
            "function prepare(fn: Callable) { return fn; } let later: Callable; /*call*/later(1);";
        var value = Check(source);
        Assert.True(value.Result.IsSuccess, string.Join(Environment.NewLine, value.Result.Errors));
        Unavailable(Call(value));
    }

    [Theory]
    [InlineData("declare function f(input: any): any; /*call*/f(1);")]
    [InlineData("type Loose = any; let f: (input: Loose) => number; /*call*/f(1);")]
    [InlineData("interface Callable { (input: any): number; } let f: Callable; /*call*/f(1);")]
    [InlineData("declare function factory(): (input: any) => number; const f = factory(); /*call*/f(1);")]
    [InlineData("const f = (input: any): number => 1; /*call*/f(1);")]
    public void ActuallyCheckedAnyAndCallableValuesKeepTheirSignature(string source)
    {
        var value = Check(source);
        Assert.True(value.Result.IsSuccess, string.Join(Environment.NewLine, value.Result.Errors));
        var call = Call(value);
        Assert.True(Assert.Single(call.Candidates).Declared.IsAvailable);
        Assert.True(call.IsComplete);
        Assert.Equal(0, call.SelectedOrdinal);
        Assert.True(call.SelectedSignature!.IsAvailable);
    }

    [Fact]
    public void IndependentInterfaceOverloadsKeepTheirOrdinalsWithoutAnUnprovedWinner()
    {
        var value = Check("interface Callable { (input: Missing): number; (input: string): number; } " +
            "let f: Callable; /*call*/f('text');");
        var call = Call(value);
        Assert.Equal(new[] { 0, 1 }, call.Candidates.Select(candidate => candidate.Ordinal));
        Assert.False(call.Candidates[0].Declared.IsAvailable);
        Assert.True(call.Candidates[1].Declared.IsAvailable);
        Assert.Equal("string", call.Candidates[1].Declared.Parameters[0].Type.Text);
        Assert.False(call.IsComplete);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, call.Status);
        Assert.Null(call.SelectedOrdinal);
        Assert.Null(call.SelectedSignature);
    }

    [Theory]
    [InlineData("class Base<T> { read(input: Missing): T { return null; } } class Derived extends Base<number> {} const value = new Derived(); /*call*/value.read(1);")]
    [InlineData("class Base<T> { read<U>(input: U): U { return input; } } class Derived extends Base<number> {} const value = new Derived(); /*call*/value.read(1);")]
    public void SubstitutedClassSignaturesRetainTheirExactSourceAnnotationProof(string source)
    {
        var value = Check(source);
        var call = Call(value);
        Assert.NotNull(Assert.Single(call.Candidates).Origin);
        Unavailable(call);
    }

    [Fact]
    public void SubstitutedKnownClassSignatureRemainsAvailable()
    {
        var value = Check("class Base<T> { read(input: T): T { return input; } } class Derived extends Base<number> {} " +
            "const value = new Derived(); /*call*/value.read(1);");
        Assert.True(value.Result.IsSuccess, string.Join(Environment.NewLine, value.Result.Errors));
        var call = Call(value);
        Assert.True(Assert.Single(call.Candidates).Declared.IsAvailable);
        Assert.Equal("number", call.Candidates[0].Declared.Parameters[0].Type.Text);
    }

    [Fact]
    public void RecoveredReturnedCallableCannotBorrowProofFromAnAvailableVariable()
    {
        var value = Check("declare function factory(): (input: Missing) => number; const fn = factory(); /*call*/fn(", recover: true);
        var call = Call(value);
        Assert.True(call.IsRecovered);
        Unavailable(call);
    }

    [Theory]
    [InlineData("declare function f<T>(input: T): T; /*bad*/f<Missing>(1); /*good*/f<number>(1);")]
    [InlineData("declare class Box<T> { constructor(input: T); } /*bad*/new Box<Missing>(1); /*good*/new Box<number>(1);")]
    [InlineData("type Broken = Missing; function prepare(value: Broken) { return value; } declare function f<T>(input: T): T; /*bad*/f<Broken>(1); /*good*/f<any>(1);")]
    public void UnknownExplicitTypeArgumentsVetoOnlyTheirOwnInstantiationAndSelection(string source)
    {
        var value = Check(source);
        var ordinary = Check(source, capture: false);
        Assert.Equal(ordinary.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)),
            value.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)));
        var bad = Call(value, "bad");
        var declared = Assert.Single(bad.Candidates);
        Assert.True(declared.Declared.IsAvailable);
        Assert.Contains("T", declared.Declared.Parameters[0].Type.Text, StringComparison.Ordinal);
        Assert.False(declared.Instantiated!.IsAvailable);
        if (bad.Kind == EditorInvocationKind.New)
        {
            // This is the original captured class definition, before its arguments resolve.
            Assert.Equal("Box<T>", declared.ConstructedType!.Text);
            Assert.Equal("Box<T>", declared.Declared.ReturnType.Text);
            Assert.DoesNotContain("any", declared.Declared.Label, StringComparison.Ordinal);
        }
        Assert.False(bad.IsComplete);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, bad.Status);
        Assert.Null(bad.SelectedOrdinal);
        Assert.Null(bad.SelectedSignature);
        var good = Call(value, "good");
        Assert.True(Assert.Single(good.Candidates).Declared.IsAvailable);
        Assert.True(good.Candidates[0].Instantiated!.IsAvailable);
        Assert.Equal("input", good.Candidates[0].Instantiated!.Parameters[0].Name);
        Assert.Contains("input:", good.Candidates[0].Instantiated!.Label, StringComparison.Ordinal);
        Assert.Equal(0, good.SelectedOrdinal);
        Assert.True(good.SelectedSignature!.IsAvailable);
        Assert.Equal(declared.OriginalSignatureId, good.Candidates[0].OriginalSignatureId);
    }

    [Theory]
    [InlineData("class Box<T> { read(input: T): T { return input; } } let bad: Box<Missing>; let good: Box<number>; /*bad*/bad.read(1); /*good*/good.read(1);")]
    [InlineData("type Callable<T> = { (input: number): number; value: T }; let bad: Callable<Missing>; let good: Callable<any>; /*bad*/bad(1); /*good*/good(1);")]
    [InlineData("class Box<T> { constructor(input: T) {} read(input: T): T { return input; } } const bad = new Box<Missing>(1); const good = new Box<number>(1); /*bad*/bad.read(1); /*good*/good.read(1);")]
    public void ExactUnavailableCalleeOrReceiverProofVetoesSubstitutedFallbacks(string source)
    {
        var value = Check(source);
        var ordinary = Check(source, capture: false);
        Assert.Equal(ordinary.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)),
            value.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)));
        Unavailable(Call(value, "bad"));
        var good = Call(value, "good");
        Assert.True(Assert.Single(good.Candidates).Declared.IsAvailable);
        Assert.Equal("number", good.Candidates[0].Declared.Parameters[0].Type.Text);
        Assert.Equal(0, good.SelectedOrdinal);
    }

    [Fact]
    public void OneUnknownPublicOverloadDoesNotEraseIndependentlyProvedCandidates()
    {
        var value = Check("declare function f(input: Missing): number; declare function f(input: string): number; /*call*/f('text');");
        var call = Call(value);
        Assert.Equal(new[] { 0, 1 }, call.Candidates.Select(candidate => candidate.Ordinal));
        Assert.False(call.Candidates[0].Declared.IsAvailable);
        Assert.True(call.Candidates[1].Declared.IsAvailable);
        Assert.Equal("string", call.Candidates[1].Declared.Parameters[0].Type.Text);
        Assert.False(call.IsComplete);
        Assert.Null(call.SelectedSignature);
    }

    [Fact]
    public void SignatureEligibilityUsesExactReferencesAndDoesNotChangeAnEarlierFreeze()
    {
        var document = new SourceDocument("identity.ts", "f()");
        var owner = new Expr.Variable(new(TokenType.IDENTIFIER, "f", null, 1, 0));
        var failed = new TypeInfo.Function([TypeInfo.Any.Shared], TypeInfo.Primitive.Number);
        var equalButIndependent = failed with { };
        var index = new EditorSemanticIndex();
        index.RecordInvocation(document, owner, EditorInvocationKind.Call,
            [new(0, failed), new(1, equalButIndependent)], 1, equalButIndependent, EditorInvocationStatus.Selected);
        var earlier = index.Freeze();
        index.RecordSignatureProof(failed, false);
        index.RecordSignatureProof(failed, true); // A cache hit cannot erase embedded fallback provenance.
        var current = index.Freeze().GetInvocation(document, owner)!;
        Assert.False(current.Candidates[0].Declared.IsAvailable);
        Assert.True(current.Candidates[1].Declared.IsAvailable);
        Assert.Null(current.SelectedSignature);
        Assert.True(earlier.GetInvocation(document, owner)!.Candidates[0].Declared.IsAvailable);
        index.Clear();
        index.RecordInvocation(document, owner, EditorInvocationKind.Call, [new(0, failed)]);
        Assert.True(index.Freeze().GetInvocation(document, owner)!.Candidates[0].Declared.IsAvailable);
    }
}
