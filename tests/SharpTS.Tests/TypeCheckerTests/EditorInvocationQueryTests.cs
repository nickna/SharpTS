using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using BindingFlags = System.Reflection.BindingFlags;
using TargetInvocationException = System.Reflection.TargetInvocationException;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorInvocationQueryTests
{
    private sealed record Checked(SourceDocument Document, TypeChecker Checker,
        TypeCheckDiagnosticResult Result, FrozenEditorSemanticIndex Facts);

    private static Checked Check(string source, bool capture = true)
    {
        var document = new SourceDocument("invocations.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        return Check(document, statements, capture);
    }

    private static Checked Check(SourceDocument document, List<Stmt> statements, bool capture = true)
    {
        var checker = new TypeChecker().WithEditorMetadata(capture);
        var result = checker.CheckWithRecovery(statements, document);
        return new(document, checker, result, checker.EditorFacts.Freeze());
    }

    private static EditorInvocationFact At(Checked value, string marker)
    {
        string comment = "/*" + marker + "*/";
        int offset = value.Document.Text.IndexOf(comment, StringComparison.Ordinal) + comment.Length;
        Assert.True(offset >= comment.Length, "Missing marker " + marker);
        var syntax = value.Document.EditorSyntax!.Invocations
            .Where(invocation => invocation.Span.Contains(offset))
            .OrderBy(invocation => invocation.Span.Length).First();
        return Assert.IsType<EditorInvocationFact>(value.Facts.GetInvocation(value.Document, syntax.Owner));
    }

    private static void Clean(Checked value) =>
        Assert.True(value.Result.IsSuccess, string.Join(Environment.NewLine, value.Result.Errors));

    private static void Selected(EditorInvocationFact fact, int ordinal)
    {
        Assert.Equal(EditorInvocationStatus.Selected, fact.Status);
        Assert.Equal(ordinal, fact.SelectedOrdinal);
        Assert.NotNull(fact.SelectedSignature);
        Assert.True(fact.SelectedSignature.IsAvailable);
    }

    [Fact]
    public void PublicOverloadsRetainActualOrdinalEvenWhenReturnTypesAreIdentical()
    {
        var value = Check("""
            function choose(value: number): boolean;
            function choose(value: string): boolean;
            function choose(value: any): boolean { return true; }
            /*number*/choose(1);
            /*string*/choose('s');
            """);
        Clean(value);
        var number = At(value, "number");
        var text = At(value, "string");
        Assert.Equal(2, number.Candidates.Count);
        Assert.Equal(2, text.Candidates.Count);
        Selected(number, 0);
        Selected(text, 1);
        Assert.Equal("number", number.Candidates[0].Declared.Parameters[0].Type.Text);
        Assert.Equal("string", text.Candidates[1].Declared.Parameters[0].Type.Text);
        Assert.Equal(number.Candidates[0].OriginalSignatureId, text.Candidates[0].OriginalSignatureId);
        Assert.DoesNotContain(text.Candidates, candidate => candidate.Declared.Parameters[0].Type.Text == "any");
    }

    [Fact]
    public void AnyArgumentsPreserveTheCheckersSubtypePassWinner()
    {
        var value = Check("""
            function choose(value: number): boolean;
            function choose(value: any): boolean;
            function choose(value: any): boolean { return true; }
            let loose: any = null;
            /*call*/choose(loose);
            """);
        Clean(value);
        Selected(At(value, "call"), 1);
    }

    [Fact]
    public void GenericCallsRetainDeclaredAndActualInstantiatedSignatures()
    {
        var value = Check("function identity<T>(value: T): T { return value; } /*call*/identity<number>(1);");
        Clean(value);
        var call = At(value, "call");
        Selected(call, 0);
        var candidate = Assert.Single(call.Candidates);
        Assert.Contains("T", candidate.Declared.Label);
        Assert.Equal("number", candidate.Instantiated!.Parameters[0].Type.Text);
        Assert.Equal("number", call.SelectedSignature!.ReturnType.Text);
    }

    [Fact]
    public void MixedGenericOverloadsKeepOriginalOrdinalAndInstantiatedWinner()
    {
        var value = Check("""
            function choose(value: number): boolean;
            function choose<T>(value: T[]): boolean;
            function choose(value: any): boolean { return true; }
            /*call*/choose<string>(['s']);
            """);
        Clean(value);
        var call = At(value, "call");
        Selected(call, 1);
        Assert.Equal(2, call.Candidates.Count);
        Assert.Contains("string", call.SelectedSignature!.Parameters[0].Type.Text);
    }

    [Theory]
    [InlineData("f()", "TS2554")]
    [InlineData("f('bad')", "TS2345")]
    [InlineData("f(missing)", "TS2304")]
    public void KnownCandidatesSurviveArityArgumentAndArgumentEvaluationFailures(string expression, string code)
    {
        var value = Check("function f(value: number): number { return value; } /*call*/" + expression + ";");
        Assert.Contains(value.Result.Diagnostics, diagnostic => diagnostic.TsCode == code);
        var call = At(value, "call");
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, call.Status);
        Assert.Single(call.Candidates);
        Assert.Null(call.SelectedOrdinal);
        Assert.Null(call.SelectedSignature);
    }

    [Fact]
    public void GenericConstraintFailureRetainsUninstantiatedPublicCandidate()
    {
        var value = Check("function f<T extends number>(value: T): T { return value; } /*call*/f<string>('bad');");
        Assert.False(value.Result.IsSuccess);
        var call = At(value, "call");
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, call.Status);
        Assert.Contains("T", Assert.Single(call.Candidates).Declared.Label);
        Assert.Null(call.SelectedSignature);
    }

    [Fact]
    public void NestedCallsPublishIndependentAttempts()
    {
        var value = Check("function inner(): number { return 1; } function outer(value: number): string { return 's'; } /*outer*/outer(/*inner*/inner());");
        Clean(value);
        Selected(At(value, "outer"), 0);
        Selected(At(value, "inner"), 0);
        Assert.Equal("number", At(value, "inner").SelectedSignature!.ReturnType.Text);
        Assert.Equal("string", At(value, "outer").SelectedSignature!.ReturnType.Text);
    }

    [Fact]
    public void ConstructorsKeepPublicOverloadsAndActualValidationWinner()
    {
        var value = Check("""
            declare class C {
                constructor(value: number);
                constructor(value: string);
            }
            /*new*/new C('s');
            """);
        Clean(value);
        var constructor = At(value, "new");
        Selected(constructor, 1);
        Assert.Equal(EditorInvocationKind.New, constructor.Kind);
        Assert.Equal(2, constructor.Candidates.Count);
        Assert.Equal("C", constructor.SelectedSignature!.ReturnType.Text);
        Assert.All(constructor.Candidates, candidate => Assert.Equal("C", candidate.ConstructedType!.Text));
    }

    [Fact]
    public void RuntimeConstructorKeepsPublicCandidatesWithoutInventingSelectionAfterBaselineFlattening()
    {
        const string source = """
            class C {
                constructor(value: number);
                constructor(value: string);
                constructor(value: any) {}
            }
            /*new*/new C('s');
            """;
        var enabled = Check(source);
        var disabled = Check(source, capture: false);
        Clean(enabled);
        Clean(disabled);
        // Ordinary checking currently replaces the constructor overload set while inferring
        // the implementation's return. Metadata must preserve the public declarations without
        // pretending that the actual implementation-only validation chose one of them.
        var ordinaryConstructor = Assert.IsType<TypeInfo.Function>(
            disabled.Result.TypeMap.GetClassType("C")!.Methods["constructor"]);
        Assert.IsType<TypeInfo.Any>(Assert.Single(ordinaryConstructor.ParamTypes));
        var capturedConstructor = Assert.IsType<TypeInfo.Function>(
            enabled.Result.TypeMap.GetClassType("C")!.Methods["constructor"]);
        Assert.IsType<TypeInfo.Any>(Assert.Single(capturedConstructor.ParamTypes));
        var constructor = At(enabled, "new");
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, constructor.Status);
        Assert.Null(constructor.SelectedOrdinal);
        Assert.Equal(2, constructor.Candidates.Count);
        Assert.Equal("number", constructor.Candidates[0].Declared.Parameters[0].Type.Text);
        Assert.Equal("string", constructor.Candidates[1].Declared.Parameters[0].Type.Text);
        Assert.DoesNotContain(constructor.Candidates,
            candidate => candidate.Declared.Parameters[0].Type.Text == "any");
    }

    [Fact]
    public void InheritedGenericConstructorPreservesSubstitutedParametersAndNewTarget()
    {
        var value = Check("class Base<T> { constructor(value: T) {} } class Derived extends Base<number> {} /*new*/new Derived(1);");
        Clean(value);
        var constructor = At(value, "new");
        Selected(constructor, 0);
        Assert.Equal("number", constructor.SelectedSignature!.Parameters[0].Type.Text);
        Assert.Equal("Derived", constructor.SelectedSignature.ReturnType.Text);
    }

    [Fact]
    public void GenericConstructorInferenceFailureStillRetainsCandidates()
    {
        var value = Check("class Box<T> { constructor(value: T) {} } /*new*/new Box();");
        Assert.False(value.Result.IsSuccess);
        var constructor = At(value, "new");
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, constructor.Status);
        Assert.Single(constructor.Candidates);
        Assert.Null(constructor.SelectedOrdinal);
    }

    [Fact]
    public void ImplicitConstructorIsExplicitlyMarkedRatherThanInventingASourceDeclaration()
    {
        var value = Check("class C {} /*new*/new C();");
        Clean(value);
        var constructor = At(value, "new");
        Selected(constructor, 0);
        var candidate = Assert.Single(constructor.Candidates);
        Assert.True(candidate.IsImplicit);
        Assert.Same(value.Document, candidate.Origin!.Document);
        Assert.IsType<Stmt.Class>(candidate.Origin.Owner);
        Assert.Equal(0, constructor.SelectedSignature!.MinimumArity);
    }

    [Fact]
    public void InterfaceAndRecordCallSignaturesAndInterfaceConstructSignaturesParticipate()
    {
        var value = Check("""
            interface Fn { (value: number): string; }
            interface Factory { new(value: number): { value: number }; }
            declare let fn: Fn;
            declare let record: { (value: string): number };
            declare let factory: Factory;
            /*interface*/fn(1);
            /*record*/record('s');
            /*new*/new factory(1);
            """);
        Clean(value);
        Selected(At(value, "interface"), 0);
        Selected(At(value, "record"), 0);
        Selected(At(value, "new"), 0);
        Assert.Equal("string", At(value, "interface").SelectedSignature!.ReturnType.Text);
        Assert.Equal("number", At(value, "record").SelectedSignature!.ReturnType.Text);
    }

    [Fact]
    public void PrivateCallsRequireTheProvenNominalOwnerAndFacet()
    {
        var value = Check("""
            class Other {}
            class C {
                #method(): number { return 1; }
                inspect(loose: any, other: Other): void {
                    /*valid*/this.#method();
                    /*any*/loose.#method();
                    /*unrelated*/other.#method();
                }
            }
            """);
        Clean(value);
        Selected(At(value, "valid"), 0);
        foreach (string marker in new[] { "any", "unrelated" })
        {
            var call = At(value, marker);
            Assert.Equal(EditorInvocationStatus.Unavailable, call.Status);
            Assert.Empty(call.Candidates);
        }
    }

    [Theory]
    [InlineData("function f(value: any): void {} /*call*/f(|")]
    [InlineData("function f(value: any): void {} /*call*/f(,|")]
    [InlineData("class C { constructor(value: any) {} } /*call*/new C(,|")]
    public void CursorRecoveryRetainsCandidatesButCannotProveASelection(string markedSource)
    {
        int cursor = markedSource.IndexOf('|');
        string source = markedSource.Remove(cursor, 1);
        var parsed = Parser.ParseForEditor(new("recovered.ts", source), cursor,
            EditorQueryKind.SignatureHelp, EditorRecoveryPolicy.Default);
        Assert.True(parsed.IsRecovered);
        var value = Check(parsed.Document, parsed.ParseResult.Statements);
        var call = At(value, "call");
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, call.Status);
        Assert.Single(call.Candidates);
        Assert.True(call.IsRecovered);
        Assert.Null(call.SelectedOrdinal);
    }

    [Fact]
    public void WrittenUndefinedIsNotARecoveryHole()
    {
        var value = Check("function f(value: any): void {} /*call*/f(undefined);");
        Clean(value);
        var call = At(value, "call");
        Selected(call, 0);
        Assert.False(call.HasHoles);
        Assert.False(call.IsRecovered);
    }

    [Fact]
    public void FailedSameAstRecheckCannotReuseSelectionFromAStaleTypeMap()
    {
        var value = Check("function f(value: number): boolean { return true; } /*call*/f(1);");
        Clean(value);
        var first = At(value, "call");
        Selected(first, 0);
        var call = Assert.IsType<Expr.Call>(first.Owner);
        var oldType = value.Result.TypeMap.Get(call);
        var environmentField = typeof(TypeChecker).GetField("_environment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = (TypeEnvironment)environmentField.GetValue(value.Checker)!;
        var later = new TypeEnvironment(original);
        later.Define("f", TypeInfo.Unknown.Shared);
        environmentField.SetValue(value.Checker, later);
        try
        {
            var method = typeof(TypeChecker).GetMethod("CheckCall", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var failure = Assert.Throws<TargetInvocationException>(() => method.Invoke(value.Checker, [call, null]));
            Assert.IsAssignableFrom<TypeCheckException>(failure.InnerException);
        }
        finally { environmentField.SetValue(value.Checker, original); }
        Assert.Same(oldType, value.Result.TypeMap.Get(call));
        var current = value.Checker.EditorFacts.Freeze().GetInvocation(value.Document, call)!;
        Assert.Equal(EditorInvocationStatus.Unavailable, current.Status);
        Assert.Null(current.SelectedSignature);
        Selected(first, 0);
    }

    [Fact]
    public void BoundedCandidateCollectionCannotClaimASelectionFromATruncatedOverloadSet()
    {
        string overloads = string.Join(Environment.NewLine, Enumerable.Range(0, 33)
            .Select(index => $"function choose(value: {index}): number;"));
        var value = Check(overloads + " function choose(value: any): number { return 1; } /*call*/choose(0);");
        Clean(value);
        var call = At(value, "call");
        Assert.Equal(32, call.Candidates.Count);
        Assert.False(call.IsComplete);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, call.Status);
        Assert.Null(call.SelectedOrdinal);
    }

    [Fact]
    public void BoundedConstructorCaptureDoesNotInventAnImplicitConstructorPastTheHierarchyLimit()
    {
        string classes = "class C0 { constructor(value: number) {} } " + string.Join(" ",
            Enumerable.Range(1, 33).Select(index => $"class C{index} extends C{index - 1} {{}}"));
        var value = Check(classes + " /*new*/new C33(1);");
        Clean(value);
        var constructor = At(value, "new");
        Assert.Empty(constructor.Candidates);
        Assert.False(constructor.IsComplete);
        Assert.Equal(EditorInvocationStatus.Unavailable, constructor.Status);
        Assert.Null(constructor.SelectedOrdinal);
    }

    [Fact]
    public void CaptureOffKeepsCheckerResultsAndBuiltinFallbacksRemainUnavailable()
    {
        const string source = "function f(value: number): number { return value; } /*call*/f('bad'); /*builtin*/parseInt('1');";
        var enabled = Check(source);
        var disabled = Check(source, capture: false);
        Assert.Equal(disabled.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)),
            enabled.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)));
        Assert.Empty(disabled.Facts.Invocations);
        Assert.Equal(EditorInvocationStatus.Unavailable, At(enabled, "builtin").Status);
    }
}
