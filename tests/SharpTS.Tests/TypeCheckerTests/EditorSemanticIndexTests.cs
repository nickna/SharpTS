using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorSemanticIndexTests
{
    [Fact]
    public void FinalAttemptClearsOccurrenceReceiverAndOldInvocationSelection()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("attempt.ts", "fn()");
        var owner = new Expr.Call(new Expr.Variable(Name("fn")), Name(")"), null, []);
        var signature = new TypeInfo.Function([], TypeInfo.Primitive.Number);
        index.CompleteExpression(document, owner, TypeInfo.Primitive.Number);
        index.SetReceiverMembers(document, owner, [new("value", TypeInfo.Primitive.Number)]);
        index.RecordInvocation(document, owner, EditorInvocationKind.Call, [new(0, signature)], 0, signature,
            EditorInvocationStatus.Selected, TypeInfo.Primitive.Number);
        var before = index.Freeze();

        index.BeginExpression(document, owner);
        index.BeginInvocation(document, owner, EditorInvocationKind.Call);
        index.RecordInvocationCandidates(document, owner, [new(0, signature)]);
        index.FailInvocation(document, owner);
        var after = index.Freeze();

        Assert.Equal(EditorFactAvailability.Unavailable, after.GetOccurrence(document, owner)!.Availability);
        Assert.Empty(after.GetReceiverMembers(document, owner).Members);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, after.GetInvocation(document, owner)!.Status);
        Assert.Null(after.GetInvocation(document, owner)!.SelectedSignature);
        Assert.Equal(EditorInvocationStatus.Selected, before.GetInvocation(document, owner)!.Status);
        Assert.Equal("number", before.GetOccurrence(document, owner)!.Type.Text);
    }

    [Fact]
    public void SignatureIdentityAndSourceOwnershipUseReferencesAndFrozenValues()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("identity.ts", "f()");
        var callee = new Expr.Variable(Name("f"));
        var first = new Expr.Call(callee, Name(")"), null, []);
        var equal = first with { };
        var parameters = new List<TypeInfo> { TypeInfo.Primitive.Number };
        var original = new TypeInfo.Function(parameters, TypeInfo.String.Shared, ParamNames: ["input"]);
        var other = original with { };
        index.RecordInvocation(document, first, EditorInvocationKind.Call, [new(0, original), new(1, other)]);
        var frozen = index.Freeze();
        var fact = frozen.GetInvocation(document, first)!;
        Assert.NotEqual(fact.Candidates[0].OriginalSignatureId, fact.Candidates[1].OriginalSignatureId);
        parameters[0] = TypeInfo.Any.Shared;
        Assert.Contains("input: number", fact.Candidates[0].Declared.Label, StringComparison.Ordinal);
        Assert.Null(frozen.GetInvocation(document, equal));
        Assert.Null(frozen.GetInvocation(new(document.Path, document.Text), first));
        index.Clear();
        Assert.NotEqual(frozen.Generation, index.Generation);
        Assert.NotNull(frozen.GetInvocation(document, first));
    }

    [Fact]
    public void GlobalParentsAndUnavailableLocalsPreserveShadowingAndFacets()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("scopes.ts", new string(' ', 30));
        int global = index.RegisterScope(null, new object(), null, kind: EditorScopeKind.Global);
        index.BindLocal(global, "global", BindingNamespace.Value, null, TypeInfo.String.Shared);
        object owner = new();
        int source = index.RegisterScope(document, owner, new(0, 30), global, EditorScopeKind.Source);
        index.BindLocal(source, "name", BindingNamespace.Value, null, TypeInfo.String.Shared);
        int body = index.RegisterScope(document, owner, new(5, 25), source, EditorScopeKind.Function);
        int parameter = index.RegisterScope(document, owner, new(5, 25), body, EditorScopeKind.ParameterList);
        Assert.NotEqual(body, parameter);
        Assert.Equal(body, index.RegisterScope(document, owner, new(5, 25), body, EditorScopeKind.Function));
        index.BindLocal(body, "name", BindingNamespace.Value, null, TypeInfo.Primitive.Number, availableFrom: 20);
        index.BindLocal(body, "name", BindingNamespace.Type, null, TypeInfo.Any.Shared);

        var bindings = index.Freeze().GetVisibleBindings(document, 10);
        Assert.Contains(bindings, binding => binding.LocalName == "global");
        var local = Assert.Single(bindings, binding => binding.LocalName == "name" && binding.Facet == BindingNamespace.Value);
        Assert.Equal("number", local.Type.Text);
        Assert.Equal(EditorFactAvailability.Unavailable, local.Availability);
        Assert.Single(bindings, binding => binding.LocalName == "name" && binding.Facet == BindingNamespace.Type);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RecoveredOrHoleCallsRetainCandidatesWithoutSelection(bool recovered, bool holes)
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("repair.ts", "f(");
        var owner = new Expr.Call(new Expr.Variable(Name("f")), Name(")"), null, []);
        var signature = new TypeInfo.Function([], TypeInfo.Any.Shared);
        index.RecordInvocation(document, owner, EditorInvocationKind.Call, [new(0, signature)], 0, signature,
            EditorInvocationStatus.Selected, isRecovered: recovered, hasHoles: holes);
        var fact = index.Freeze().GetInvocation(document, owner)!;
        Assert.Single(fact.Candidates);
        Assert.Null(fact.SelectedOrdinal);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, fact.Status);
    }

    [Fact]
    public void ReceiverAndCandidateCapsAreExplicitAndDoNotSelectDuplicateOrdinals()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("caps.ts", "value");
        var owner = new Expr.Variable(Name("value"));
        var signature = new TypeInfo.Function([], TypeInfo.Any.Shared);
        index.SetReceiverMembers(document, owner, Enumerable.Range(0, 300).Select(i => new EditorReceiverCandidate("value" + i, TypeInfo.Any.Shared)).ToArray());
        index.RecordInvocation(document, owner, EditorInvocationKind.Call, [new(0, signature), new(0, signature)], 0, signature, EditorInvocationStatus.Selected);
        var frozen = index.Freeze();
        Assert.Equal(256, frozen.GetReceiverMembers(document, owner).Members.Count);
        Assert.True(frozen.GetReceiverMembers(document, owner).IsTruncated);
        Assert.False(frozen.GetReceiverMembers(document, owner).IsComplete);
        Assert.Null(frozen.GetInvocation(document, owner)!.SelectedOrdinal);
        Assert.False(frozen.GetInvocation(document, owner)!.IsComplete);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 33)]
    public void IncompleteOrTruncatedCandidateSetsCannotClaimSelection(bool complete, int count)
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("incomplete.ts", "value()");
        var owner = new Expr.Variable(Name("value"));
        var signature = new TypeInfo.Function([], TypeInfo.Void.Shared);
        var candidates = Enumerable.Range(0, count).Select(ordinal => new EditorInvocationCandidate(ordinal, signature)).ToArray();
        index.RecordInvocation(document, owner, EditorInvocationKind.Call, candidates, 0, signature,
            EditorInvocationStatus.Selected, isComplete: complete);
        var fact = index.Freeze().GetInvocation(document, owner)!;
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, fact.Status);
        Assert.False(fact.IsComplete);
        Assert.Null(fact.SelectedSignature);
        Assert.Null(fact.SelectedOrdinal);
    }

    [Fact]
    public void SharedTypeParametersKeepAnExplicitTruncationMarkerAfterInputMutation()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("generic-cap.ts", "value()");
        var owner = new Expr.Variable(Name("value"));
        var parameters = Enumerable.Range(0, 100).Select(number => new TypeInfo.TypeParameter("T" + number)).ToList();
        var signature = new TypeInfo.Function([], TypeInfo.Void.Shared);
        index.RecordInvocation(document, owner, EditorInvocationKind.Call, [new(0, signature, TypeParameters: parameters)]);
        parameters.Clear();
        var presentation = Assert.Single(index.Freeze().GetInvocation(document, owner)!.Candidates).Declared;
        Assert.True(presentation.IsTruncated);
        Assert.Contains("T31", presentation.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("T33", presentation.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureScopeDoesNotProveThatItsBodyWasEntered()
    {
        const string source = "const outer = 1; function read() { outer; }";
        var document = new SourceDocument("body-entry.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var outer = Assert.IsType<Stmt.Const>(statements[0]);
        var function = Assert.IsType<Stmt.Function>(statements[1]);
        var whole = Assert.Single(document.EditorSyntax!.GetRecords(function), record =>
            record.Kind == EditorSyntaxKind.Function && record.Role == EditorSyntaxRole.Whole).Span;
        var body = Assert.Single(document.EditorSyntax.GetRecords(function), record =>
            record.Kind == EditorSyntaxKind.Function && record.Role == EditorSyntaxRole.Body).Span;
        var index = new EditorSemanticIndex();
        int script = index.RegisterScope(document, document, new(0, source.Length), kind: EditorScopeKind.Source);
        index.BindLocal(script, "outer", BindingNamespace.Value, null, TypeInfo.Primitive.Number, outer);
        index.RegisterScope(document, function, whole, script, EditorScopeKind.Function);
        int offset = source.LastIndexOf("outer", StringComparison.Ordinal);
        Assert.Empty(index.Freeze().GetVisibleBindings(document, offset));
        index.MarkBodyEntered(document, function, body);
        var checkedBody = index.Freeze();
        Assert.Equal("outer", Assert.Single(checkedBody.GetVisibleBindings(document, offset)).LocalName);
        index.ClearBodyEntered(document, function);
        Assert.Empty(index.Freeze().GetVisibleBindings(document, offset));
        Assert.Single(checkedBody.GetVisibleBindings(document, offset));
    }

    [Fact]
    public void AnUnvisitedWrittenLocalCannotFallThroughToAnOuterSpelling()
    {
        const string source = "const local = 'outer'; { missing; const local = 1; local; }";
        var document = new SourceDocument("skipped-local.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var outer = Assert.IsType<Stmt.Const>(statements[0]);
        var block = Assert.IsType<Stmt.Block>(statements[1]);
        var local = Assert.IsType<Stmt.Const>(block.Statements[1]);
        var body = Assert.Single(document.EditorSyntax!.GetRecords(block), record => record.Kind == EditorSyntaxKind.Block).Span;
        var index = new EditorSemanticIndex();
        int script = index.RegisterScope(document, document, new(0, source.Length), kind: EditorScopeKind.Source);
        index.BindLocal(script, "local", BindingNamespace.Value, null, TypeInfo.String.Shared, outer);
        int inner = index.RegisterScope(document, block, body, script);
        int offset = source.LastIndexOf("local", StringComparison.Ordinal);
        Assert.Empty(index.Freeze().GetVisibleBindings(document, offset));
        index.BindLocal(inner, "local", BindingNamespace.Value, null, TypeInfo.Primitive.Number, local);
        Assert.Equal("number", Assert.Single(index.Freeze().GetVisibleBindings(document, offset)).Type.Text);
    }

    [Fact]
    public void AnUnvisitedLocalInAParentAlsoRefusesAnAlreadyVisitedChildQuery()
    {
        const string source = "const local = 'outer'; { { local; } missing; const local = 1; }";
        var document = new SourceDocument("skipped-parent-local.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var outer = Assert.IsType<Stmt.Const>(statements[0]);
        var parent = Assert.IsType<Stmt.Block>(statements[1]);
        var child = Assert.IsType<Stmt.Block>(parent.Statements[0]);
        var local = Assert.IsType<Stmt.Const>(parent.Statements[2]);
        SourceSpan Body(Stmt.Block block) => Assert.Single(document.EditorSyntax!.GetRecords(block),
            record => record.Kind == EditorSyntaxKind.Block).Span;
        var index = new EditorSemanticIndex();
        int script = index.RegisterScope(document, document, new(0, source.Length), kind: EditorScopeKind.Source);
        index.BindLocal(script, "local", BindingNamespace.Value, null, TypeInfo.String.Shared, outer);
        int parentScope = index.RegisterScope(document, parent, Body(parent), script);
        index.RegisterScope(document, child, Body(child), parentScope);
        int offset = source.IndexOf("{ local", StringComparison.Ordinal) + 2;
        Assert.Empty(index.Freeze().GetVisibleBindings(document, offset));
        index.BindLocal(parentScope, "local", BindingNamespace.Value, null, TypeInfo.Primitive.Number, local,
            availableFrom: local.Name.End);
        var binding = Assert.Single(index.Freeze().GetVisibleBindings(document, offset));
        Assert.Equal("number", binding.Type.Text);
        Assert.Equal(EditorFactAvailability.Unavailable, binding.Availability);
    }

    [Fact]
    public void ClearingOneDocumentPreservesOtherDocumentsAndFrozenPublications()
    {
        var index = new EditorSemanticIndex();
        var first = new SourceDocument("same.ts", "value");
        var second = new SourceDocument("same.ts", "value");
        var owner = new Expr.Variable(Name("value"));
        var signature = new TypeInfo.Function([], TypeInfo.Void.Shared);
        foreach (var document in new[] { first, second })
        {
            index.CompleteExpression(document, owner, TypeInfo.Primitive.Number);
            index.RecordDeclaration(document, owner, owner.Name, null, BindingNamespace.Value, TypeInfo.Primitive.Number);
            index.SetReceiverMembers(document, owner, [new("field", TypeInfo.Primitive.Number)]);
            index.RecordInvocation(document, owner, EditorInvocationKind.Call, [new(0, signature)]);
            int scope = index.RegisterScope(document, document, new(0, document.Text.Length), kind: EditorScopeKind.Source);
            index.BindLocal(scope, "value", BindingNamespace.Value, null, TypeInfo.Primitive.Number, owner);
        }
        var original = index.Freeze();
        index.ClearDocument(first);
        var current = index.Freeze();
        Assert.Equal(original.Generation, current.Generation);
        Assert.Null(current.GetOccurrence(first, owner));
        Assert.Empty(current.GetDeclarations(first, owner));
        Assert.Empty(current.GetReceiverMembers(first, owner).Members);
        Assert.Null(current.GetInvocation(first, owner));
        Assert.Empty(current.GetVisibleBindings(first, 0));
        Assert.NotNull(current.GetOccurrence(second, owner));
        Assert.Single(current.GetVisibleBindings(second, 0));
        Assert.NotNull(original.GetOccurrence(first, owner));
        Assert.Single(original.GetVisibleBindings(first, 0));
    }

    [Fact]
    public void FrozenPresentationsReleaseMutableTypesAndEnvironmentGraphs()
    {
        var (frozen, type, environment) = PublishWithoutBuildState();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(type.TryGetTarget(out _));
        Assert.False(environment.TryGetTarget(out _));
        Assert.Equal("typeof Mutable", Assert.Single(frozen.Occurrences).Type.Text);
        Assert.IsNotType<TypeEnvironment>(Assert.Single(frozen.Scopes).Owner);
        Assert.Null(Assert.Single(Assert.Single(frozen.Scopes).Bindings).DeclarationOwner);
        GC.KeepAlive(frozen);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (FrozenEditorSemanticIndex Frozen, WeakReference<TypeInfo> Type,
        WeakReference<TypeEnvironment> Environment) PublishWithoutBuildState()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("lifetime.ts", "value");
        var owner = new Expr.Variable(Name("value"));
        var type = new TypeInfo.MutableClass("Mutable");
        var environment = new TypeEnvironment();
        environment.Define("value", type);
        index.CompleteExpression(document, owner, type);
        index.SetReceiverMembers(document, owner, [new("value", type)]);
        int scope = index.RegisterScope(null, environment, null, kind: EditorScopeKind.Global);
        index.BindLocal(scope, "value", BindingNamespace.Value, null, type, environment);
        index.RecordInvocation(document, owner, EditorInvocationKind.Call,
            [new(0, new TypeInfo.Function([type], type))]);
        return (index.Freeze(), new(type), new(environment));
    }

    private static Token Name(string text) => new(TokenType.IDENTIFIER, text, null, 1, 0);
}
