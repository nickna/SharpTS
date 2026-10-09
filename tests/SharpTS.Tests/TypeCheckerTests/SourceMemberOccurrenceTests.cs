using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using BindingFlags = System.Reflection.BindingFlags;
using TargetInvocationException = System.Reflection.TargetInvocationException;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class SourceMemberOccurrenceTests
{
    private sealed record Checked(SourceDocument Document, TypeChecker Checker,
        TypeCheckDiagnosticResult Result, FrozenMemberIndex Members);

    private static Checked Check(string source, bool capture = true)
    {
        var document = new SourceDocument("source-members.ts", source);
        // Keep written syntax in both runs so baseline receiver types can be inspected
        // independently of the checker's optional member capture.
        var parser = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax();
        var statements = parser.ParseOrThrow();
        var checker = new TypeChecker().WithMemberProvenance(capture);
        var result = checker.CheckWithRecovery(statements, document);
        return new(document, checker, result, checker.Members.Freeze());
    }

    private static int Offset(Checked value, string marker) =>
        Offset(value.Document.Text, marker);

    private static int Offset(string source, string marker)
    {
        string comment = "/*" + marker + "*/";
        int start = source.IndexOf(comment, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing marker " + marker);
        return start + comment.Length;
    }

    private static FrozenMemberOccurrence Occurrence(Checked value, string marker) =>
        Assert.Single(value.Members.Occurrences, occurrence =>
            ReferenceEquals(occurrence.Document, value.Document) && occurrence.Name.Start == Offset(value, marker));

    private static FrozenSourceMemberSymbol Symbol(Checked value, string marker)
    {
        var resolution = value.Members.FindResolution(value.Document, Offset(value, marker));
        Assert.True(resolution.IsResolved, "Expected a complete source member at " + marker + "; " +
            string.Join("; ", value.Members.Symbols.Select(symbol => symbol.Name + ":" + symbol.Facet + "#" + symbol.DeclaringClassId)) +
            "; receivers=" + string.Join("; ", value.Document.EditorSyntax!.Members.Select(member =>
                member.Name.Lexeme + "@" + member.Name.Start + "=" + Describe(value.Result.TypeMap.Get(member.Receiver)))));
        var symbol = Assert.Single(resolution.Candidates);
        Assert.False(symbol.CanRename);
        return symbol;
    }

    private static string Describe(TypeInfo? type) => type switch
    {
        TypeInfo.Instance instance => "Instance(" + Describe(instance.ResolvedClassType) + ")",
        TypeInfo.InstantiatedGeneric generic => "Instantiated(" + Describe(generic.GenericDefinition) + ")",
        TypeInfo.Class @class => @class.Name + "#" + @class.Core.DeclarationId,
        TypeInfo.GenericClass generic => generic.Name + "#" + generic.Core.DeclarationId,
        TypeInfo.MutableClass mutable => mutable.Name + "#" + mutable.DeclarationId,
        _ => type?.ToString() ?? "null",
    };

    private static void AssertClean(Checked value) =>
        Assert.True(value.Result.IsSuccess, string.Join(Environment.NewLine, value.Result.Errors));

    [Fact]
    public void ReadsWritesCallsAndThisKeepDistinctInstanceAndStaticFacets()
    {
        var value = Check("""
            class C {
                value: number = 1;
                static value: number = 2;
                method(): number { return this./*this*/value; }
                static inspect(): number { return this./*staticThis*/value; }
            }
            const object = new C();
            object./*read*/value;
            object./*write*/value = 3;
            C./*static*/value;
            object./*call*/method();
            """);
        AssertClean(value);
        var instance = Symbol(value, "read");
        Assert.Same(instance, Symbol(value, "write"));
        Assert.Same(instance, Symbol(value, "this"));
        var @static = Symbol(value, "static");
        Assert.Same(@static, Symbol(value, "staticThis"));
        Assert.NotSame(instance, @static);
        Assert.Equal(MemberFacet.Instance, instance.Facet);
        Assert.Equal(MemberFacet.Static, @static.Facet);
        Assert.Equal(MemberOperation.Read, Occurrence(value, "read").Operations);
        Assert.Equal(MemberOperation.Write, Occurrence(value, "write").Operations);
        Assert.Equal(MemberOperation.Read | MemberOperation.Call, Occurrence(value, "call").Operations);
    }

    [Fact]
    public void GenericInstantiationInheritanceOverrideAndSuperUseActualDeclaringOrigin()
    {
        var value = Check("""
            class Base<T> { field: T; method(): number { return 1; } }
            class Derived extends Base<number> {
                inspect(): number { return super./*super*/method(); }
            }
            class Override extends Base<number> { method(): number { return 2; } }
            const first = new Base<number>();
            const second = new Base<string>();
            const derived = new Derived();
            first./*first*/field;
            second./*second*/field;
            derived?./*optional*/field;
            first./*baseMethod*/method();
            new Override()./*override*/method();
            """);
        AssertClean(value);
        Assert.Same(Symbol(value, "first"), Symbol(value, "second"));
        Assert.Same(Symbol(value, "first"), Symbol(value, "optional"));
        Assert.Same(Symbol(value, "baseMethod"), Symbol(value, "super"));
        Assert.NotSame(Symbol(value, "baseMethod"), Symbol(value, "override"));
        Assert.Equal(MemberOperation.Read | MemberOperation.Call, Occurrence(value, "super").Operations);
    }

    [Fact]
    public void GetterSetterPairHasOneOriginForBothSelectedOperations()
    {
        var value = Check("""
            class C {
                get value(): number { return 1; }
                set value(next: number) {}
            }
            const object = new C();
            object./*read*/value;
            object./*write*/value = 2;
            """);
        AssertClean(value);
        var symbol = Symbol(value, "read");
        Assert.Same(symbol, Symbol(value, "write"));
        Assert.Equal(SourceMemberKind.Accessor, symbol.Kind);
        Assert.Equal(2, symbol.Declarations.Count);
    }

    [Fact]
    public void PrivateReadsWritesCallsAndPresenceUseLexicalBrandAndNominalAccessProof()
    {
        var value = Check("""
            class C {
                #value: number = 1;
                #method(input: number): number { return input; }
                inspect(other: C, candidate: object, loose: any): boolean {
                    other./*read*/#value;
                    other./*write*/#value = 2;
                    other./*call*/#method(1);
                    #value in candidate;
                    return /*presence*/#value in loose;
                }
            }
            """);
        AssertClean(value);
        var symbol = Symbol(value, "read");
        Assert.Same(symbol, Symbol(value, "write"));
        Assert.Same(symbol, Symbol(value, "presence"));
        Assert.Equal(MemberFacet.PrivateInstance, symbol.Facet);
        Assert.Equal(MemberOperation.Presence, Occurrence(value, "presence").Operations);
        Assert.Equal(MemberOperation.Read | MemberOperation.Call, Occurrence(value, "call").Operations);
    }

    [Fact]
    public void GenericAndDerivedPrivateInstancesRetainTheirDeclaringBrand()
    {
        var value = Check("""
            class Base<T> {
                #value: number = 1;
                inspect(): number {
                    const generic = new Base<T>();
                    class Derived extends Base<T> {}
                    const derived = new Derived();
                    generic./*generic*/#value;
                    return derived./*derived*/#value;
                }
            }
            """);
        AssertClean(value);
        Assert.Same(Symbol(value, "generic"), Symbol(value, "derived"));
    }

    [Fact]
    public void BaselineAnyGenericSelfSignatureAndForwardClassCannotProvePrivateReceivers()
    {
        const string source = """
            class Base<T> {
                #value: number = 1;
                inspect(other: Base<T>, derived: Derived): number {
                    other./*generic*/#value;
                    return derived./*derived*/#value;
                }
            }
            class Derived extends Base<number> {}
            """;
        var enabled = Check(source);
        var baseline = Check(source, capture: false);
        AssertClean(enabled);
        AssertClean(baseline);
        foreach (string marker in new[] { "generic", "derived" })
        {
            var capturedMember = Assert.Single(enabled.Document.EditorSyntax!.Members,
                member => member.Name.Start == Offset(enabled, marker));
            var baselineMember = Assert.Single(baseline.Document.EditorSyntax!.Members,
                member => member.Name.Start == Offset(baseline, marker));
            Assert.IsType<TypeInfo.Any>(enabled.Result.TypeMap.Get(capturedMember.Receiver));
            Assert.IsType<TypeInfo.Any>(baseline.Result.TypeMap.Get(baselineMember.Receiver));
            Assert.False(enabled.Members.FindResolution(enabled.Document, Offset(enabled, marker)).IsResolved);
        }
    }

    [Fact]
    public void StaticPrivateAccessRequiresExactConstructorAndCorrectFacet()
    {
        var value = Check("""
            class C {
                static #value: number = 1;
                #instance: number = 2;
                static inspect(other: C, loose: any): number {
                    C./*valid*/#value;
                    C./*wrongFacet*/#instance;
                    other./*instanceReceiver*/#value;
                    return loose./*anyReceiver*/#value;
                }
            }
            """);
        Assert.Equal(MemberFacet.PrivateStatic, Symbol(value, "valid").Facet);
        foreach (string marker in new[] { "wrongFacet", "instanceReceiver", "anyReceiver" })
            Assert.False(value.Members.FindResolution(value.Document, Offset(value, marker)).IsResolved);
    }

    [Fact]
    public void AnyAndUnrelatedSameNamedPrivateReceiversCannotAcquireLexicalClassOrigin()
    {
        var value = Check("""
            class Other { #value: number = 1; }
            class C {
                #value: number = 1;
                inspect(loose: any, other: Other): number {
                    loose./*any*/#value;
                    return other./*unrelated*/#value;
                }
            }
            """);
        foreach (string marker in new[] { "any", "unrelated" })
            Assert.False(value.Members.FindResolution(value.Document, Offset(value, marker)).IsResolved);
    }

    [Fact]
    public void FailedPublicAndPrivateCallsKeepReadProofWithoutCallEligibility()
    {
        var value = Check("""
            class C {
                method(input: number): number { return input; }
                #private(input: number): number { return input; }
                inspect(other: C): void { other./*private*/#private('wrong'); }
            }
            new C()./*public*/method('wrong');
            """);
        Assert.False(value.Result.IsSuccess);
        foreach (string marker in new[] { "public", "private" })
        {
            Symbol(value, marker);
            Assert.Equal(MemberOperation.Read, Occurrence(value, marker).Operations);
        }
    }

    [Fact]
    public void UnionSameOriginResolvesWhileDistinctAndUnsupportedConstituentsRemainExplicit()
    {
        var value = Check("""
            class Box<T> { value: T; }
            class A { value: number = 1; }
            class B { value: number = 2; }
            function shared(object: Box<number> | Box<string>) { return object./*same*/value; }
            function distinct(object: A | B) { return object./*distinct*/value; }
            function mixed(object: A | { value: number }) { return object./*mixed*/value; }
            function intersection(object: A & B) { return object./*intersection*/value; }
            function optional(object: A | null) { return object?./*nullish*/value; }
            """);
        AssertClean(value);
        Symbol(value, "same");
        var distinct = value.Members.FindResolution(value.Document, Offset(value, "distinct"));
        Assert.True(distinct.IsAmbiguous);
        Assert.Equal(2, distinct.Candidates.Count);
        var mixed = value.Members.FindResolution(value.Document, Offset(value, "mixed"));
        Assert.False(mixed.IsComplete);
        Assert.Single(mixed.Candidates);
        Assert.False(value.Members.FindResolution(value.Document, Offset(value, "intersection")).IsComplete);
        Assert.False(value.Members.FindResolution(value.Document, Offset(value, "nullish")).IsComplete);
    }

    [Fact]
    public void WidenedUnionWriteNeverSelectsOnlyItsFlowNarrowedOrigin()
    {
        var value = Check("""
            class A { value: number = 1; }
            class B { value: number = 2; }
            let object: A | B = new A();
            object./*write*/value = 3;
            """);
        Assert.False(value.Members.FindResolution(value.Document, Offset(value, "write")).IsResolved);
    }

    [Fact]
    public void UpdatesKeepReadProofWithoutInventingWriteSelection()
    {
        var value = Check("""
            class C { value: number = 1; }
            const object = new C();
            ++object./*prefix*/value;
            object./*postfix*/value++;
            """);
        AssertClean(value);
        Assert.Same(Symbol(value, "prefix"), Symbol(value, "postfix"));
        Assert.Equal(MemberOperation.Read, Occurrence(value, "prefix").Operations);
        Assert.Equal(MemberOperation.Read, Occurrence(value, "postfix").Operations);
    }

    [Theory]
    [InlineData("object./*use*/value += 1;")]
    [InlineData("object./*use*/value ||= 1;")]
    [InlineData("object[/*use*/'value'];")]
    [InlineData("object[/*use*/'value'] = 2;")]
    public void ExistingClassFallbacksDoNotFabricateASelection(string expression)
    {
        var value = Check("class C { value: number = 1; } const object = new C(); " + expression);
        AssertClean(value);
        Assert.False(value.Members.FindResolution(value.Document, Offset(value, "use")).IsResolved);
    }

    [Theory]
    [InlineData("function read(object: any) { return object./*use*/value; }")]
    [InlineData("function read(object: { value: number }) { return object./*use*/value; }")]
    [InlineData("interface Shape { value: number; } function read(object: Shape) { return object./*use*/value; }")]
    [InlineData("function read(object: unknown) { return object./*use*/value; }")]
    public void NonNominalReceiversNeverAcquireSourceMemberIdentity(string source)
    {
        var value = Check(source);
        Assert.False(value.Members.FindResolution(value.Document, Offset(value, "use")).IsResolved);
    }

    [Fact]
    public void ContextualKeywordMemberUsesParserWrittenAlias()
    {
        var value = Check("class C { get: number = 1; } new C()./*use*/get;");
        AssertClean(value);
        Assert.Equal("get", Symbol(value, "use").Name);
        Assert.Equal("get", Occurrence(value, "use").Name.Lexeme);
    }

    [Fact]
    public void CaptureIsOptInAndDiagnosticsStayUnchanged()
    {
        const string source = "class C { value: number = 1; method(x: number) {} } const object = new C(); object.value = 'wrong'; object.method('wrong');";
        var enabled = Check(source);
        var disabled = Check(source, capture: false);
        Assert.False(disabled.Checker.Members.IsEnabled);
        Assert.Equal(0, disabled.Members.Count);
        Assert.Equal(disabled.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)),
            enabled.Result.Diagnostics.Select(diagnostic => (diagnostic.TsCode, diagnostic.Message)));
    }

    [Fact]
    public void GeneratedParameterPropertyPrologueDoesNotBecomeAWrittenPropertyUse()
    {
        var value = Check("class C { constructor(public /*parameter*/value: number) {} } new C(1);");
        AssertClean(value);
        var declaration = Occurrence(value, "parameter");
        Assert.True(declaration.IsDeclaration);
        Assert.Equal(MemberOperation.Declaration, declaration.Operations);
    }

    [Fact]
    public void FailedRecheckRemovesOldCallEligibilityWhileKeepingFreshReadProof()
    {
        var value = Check("class C { method(input: number): number { return input; } } new C()./*use*/method(1);");
        AssertClean(value);
        Assert.Equal(MemberOperation.Read | MemberOperation.Call, Occurrence(value, "use").Operations);
        var call = Assert.IsType<Expr.Call>(Assert.Single(value.Document.EditorSyntax!.Invocations, invocation => !invocation.IsNew).Owner);
        call.Arguments[0] = new Expr.Literal("wrong");
        var checkCall = typeof(TypeChecker).GetMethod("CheckCall", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var failure = Assert.Throws<TargetInvocationException>(() => checkCall.Invoke(value.Checker, [call, null]));
        Assert.IsAssignableFrom<TypeCheckException>(failure.InnerException);
        var current = value with { Members = value.Checker.Members.Freeze() };
        Symbol(current, "use");
        Assert.Equal(MemberOperation.Read, Occurrence(current, "use").Operations);
    }

    [Theory]
    [InlineData(true, "TS18046")]
    [InlineData(false, "TS2322")]
    public void FailedPresenceRecheckRemovesEarlierSuccessfulProof(bool unknown, string expectedCode)
    {
        var value = Check("class C { #value: number = 1; probe(candidate: object) { return /*use*/#value in candidate; } }");
        AssertClean(value);
        Assert.Equal(MemberOperation.Presence, Occurrence(value, "use").Operations);
        Symbol(value, "use");
        var probe = Assert.IsType<Expr.PrivateIn>(value.Document.EditorSyntax!
            .FindNarrowest(Offset(value, "use"), EditorSyntaxKind.Expression)!.Node);
        var lexicalOwner = value.Result.TypeMap.GetPrivateInOwner(probe.Name);
        Assert.NotNull(lexicalOwner);

        // Revisit the identical AST/token with a later receiver type. The private brand owner
        // remains valid; only successful RHS eligibility from the previous pass must disappear.
        var environmentField = typeof(TypeChecker).GetField("_environment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var classField = typeof(TypeChecker).GetField("_currentClass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalEnvironment = (TypeEnvironment)environmentField.GetValue(value.Checker)!;
        object? originalClass = classField.GetValue(value.Checker);
        var laterEnvironment = new TypeEnvironment(originalEnvironment);
        laterEnvironment.Define("candidate", unknown ? TypeInfo.Unknown.Shared : TypeInfo.Primitive.Number);
        environmentField.SetValue(value.Checker, laterEnvironment);
        classField.SetValue(value.Checker, lexicalOwner);
        try
        {
            var failure = Assert.Throws<TypeCheckException>(() => value.Checker.VisitPrivateIn(probe));
            Assert.Equal(expectedCode, failure.Diagnostic.TsCode);
        }
        finally
        {
            environmentField.SetValue(value.Checker, originalEnvironment);
            classField.SetValue(value.Checker, originalClass);
        }
        Assert.Same(lexicalOwner, value.Result.TypeMap.GetPrivateInOwner(probe.Name));
        var current = value.Checker.Members.Freeze();
        Assert.DoesNotContain(current.Occurrences, occurrence => occurrence.Name.Start == Offset(value, "use"));
        Assert.Empty(current.FindResolution(value.Document, Offset(value, "use")).Candidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedClassBodiesOnlyBindReceiverTypesProvenByTheFinalCheckerPass(bool forward)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpts-member-forward-" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(directory, "main.ts");
        string model = Path.Combine(directory, "model.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entry] = "import { Consumer, Model } from './model'; new Consumer().read(new Model());",
            [model] = forward
                ? "export class Consumer { read(model: Model) { return model./*use*/value; } } export class Model { value: number = 1; }"
                : "export class Model { value: number = 1; } export class Consumer { read(model: Model) { return model./*use*/value; } }",
        };
        var resolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Disabled) { CaptureEditorSyntax = true };
        var program = resolver.LoadProgram(entry);
        var checker = new TypeChecker().WithMemberProvenance();
        TypeMap types = checker.CheckModules(resolver.GetModulesInOrder(program), resolver);
        var document = resolver.GetCachedModule(model)!.Document;
        Assert.NotNull(document);
        var frozen = checker.Members.Freeze();
        var resolution = frozen.FindResolution(document!, Offset(files[model], "use"));
        var member = Assert.Single(document!.EditorSyntax!.Members,
            item => item.Name.Start == Offset(files[model], "use"));
        if (forward)
        {
            Assert.IsType<TypeInfo.Any>(types.Get(member.Receiver));
            Assert.False(resolution.IsResolved);
            var baselineResolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Disabled) { CaptureEditorSyntax = true };
            var baselineProgram = baselineResolver.LoadProgram(entry);
            var baselineChecker = new TypeChecker();
            var baselineTypes = baselineChecker.CheckModules(baselineResolver.GetModulesInOrder(baselineProgram), baselineResolver);
            var baselineDocument = baselineResolver.GetCachedModule(model)!.Document!;
            var baselineMember = Assert.Single(baselineDocument.EditorSyntax!.Members,
                item => item.Name.Start == Offset(files[model], "use"));
            Assert.IsType<TypeInfo.Any>(baselineTypes.Get(baselineMember.Receiver));
            Assert.False(baselineChecker.Members.IsEnabled);
        }
        else
        {
            Assert.IsType<TypeInfo.Instance>(types.Get(member.Receiver));
            Assert.True(resolution.IsResolved);
            Assert.Equal("value", Assert.Single(resolution.Candidates).Name);
        }
    }

    [Fact]
    public void TsxPrivateUsesShareTheirCanonicalDeclarationAcrossCheckerPasses()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
            "sharpts-member-private-" + Guid.NewGuid().ToString("N"), "main.tsx"));
        const string source = """
            class Model {
                #value: number = 1;
                #method(): number { return this.#value; }
                static #static: number = 2;
                inspect(): number {
                    const model = new Model();
                    model./*write*/#value = 3;
                    model./*call*/#method();
                    return model./*read*/#value;
                }
                static inspect(): number { return Model./*static*/#static; }
            }
            new Model().inspect();
            """;
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            TypeScriptProgramOptions.Disabled) { CaptureEditorSyntax = true };
        var program = resolver.LoadProgram(path);
        var checker = new TypeChecker().WithMemberProvenance();
        checker.CheckModules(resolver.GetModulesInOrder(program), resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var document = resolver.GetCachedModule(path)!.Document!;
        var frozen = checker.Members.Freeze();
        foreach (var (marker, operations, facet) in new[]
        {
            ("read", MemberOperation.Read, MemberFacet.PrivateInstance),
            ("write", MemberOperation.Write, MemberFacet.PrivateInstance),
            ("call", MemberOperation.Read | MemberOperation.Call, MemberFacet.PrivateInstance),
            ("static", MemberOperation.Read, MemberFacet.PrivateStatic),
        })
        {
            var occurrence = Assert.Single(frozen.Occurrences, item => item.Name.Start == Offset(source, marker));
            Assert.Equal(operations, occurrence.Operations);
            Assert.True(occurrence.Resolution.IsResolved);
            var origin = Assert.Single(occurrence.Resolution.Candidates);
            Assert.Equal(facet, origin.Facet);
            var declaration = Assert.Single(origin.Declarations);
            Assert.Same(origin, Assert.Single(frozen.FindResolution(document, declaration.Name.Start).Candidates));
        }
    }
}
