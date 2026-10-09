using System.Reflection;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorHoverMetadataTests
{
    private sealed record Checked(SourceDocument Document, IReadOnlyList<Stmt> Statements,
        TypeChecker Checker, TypeCheckDiagnosticResult Result, FrozenEditorSemanticIndex Facts);

    private static Checked Check(string source, bool enabled = true)
    {
        var document = new SourceDocument("hover metadata.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata(enabled);
        var result = checker.CheckWithRecovery(statements, document);
        return new(document, statements, checker, result, checker.EditorFacts.Freeze());
    }

    private static int At(Checked value, string marker) =>
        value.Document.Text.IndexOf("/*" + marker + "*/", StringComparison.Ordinal) + marker.Length + 4;

    private static EditorTypeUseFact TypeAt(Checked value, string marker)
    {
        var name = value.Document.EditorSyntax!.FindNarrowest(At(value, marker), EditorSyntaxKind.Name);
        Assert.NotNull(name);
        var owner = Assert.IsAssignableFrom<TypeNode>(name.Node);
        return Assert.IsType<EditorTypeUseFact>(value.Facts.GetTypeUse(value.Document, owner));
    }

    private static void Clean(Checked value) => Assert.True(value.Result.IsSuccess,
        string.Join(Environment.NewLine, value.Result.Errors));

    [Fact]
    public void ActualNamedAnnotationUsesRetainGenericArgumentsAndExactSourceOwnership()
    {
        var value = Check("// 😀\r\nclass Box<T> { value: T; } let box: /*box*/Box<number>; let primitive: /*primitive*/string;");
        Clean(value);
        var generic = TypeAt(value, "box");
        Assert.Equal(EditorFactAvailability.Available, generic.Availability);
        Assert.Equal("Box<number>", generic.Type.Text);
        Assert.Equal(At(value, "box"), generic.Span!.Value.Start);
        Assert.Equal("Box<number>".Length, generic.Span.Value.Length);
        Assert.Equal("string", TypeAt(value, "primitive").Type.Text);
        Assert.Null(value.Facts.GetTypeUse(new(value.Document.Path, value.Document.Text), generic.Owner));
        Assert.Null(value.Facts.GetTypeUse(value.Document, generic.Owner with { }));
    }

    [Fact]
    public void UnknownNamedFallbacksRemainUnavailableWhileLegitimateAnyIsAvailable()
    {
        const string source = "let anyValue: /*any*/any; let missing: /*missing*/NotDeclared; let generic: /*generic*/NotDeclared<number>;";
        var value = Check(source);
        var ordinary = Check(source, enabled: false);
        Assert.Equal(ordinary.Result.Diagnostics.Select(error => (error.TsCode, error.Message)),
            value.Result.Diagnostics.Select(error => (error.TsCode, error.Message)));
        Assert.Equal("any", TypeAt(value, "any").Type.Text);
        Assert.Equal(EditorFactAvailability.Available, TypeAt(value, "any").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "missing").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "generic").Availability);
        Assert.Empty(ordinary.Facts.TypeUses);
    }

    [Fact]
    public void UnknownNestedTypeDoesNotBecomeAnAvailableCompositeAnnotation()
    {
        var value = Check("let values: /*array*/Array</*missing*/Missing>; let safe: /*safe*/Array<number>;");
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "array").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "missing").Availability);
        Assert.Equal(EditorFactAvailability.Available, TypeAt(value, "safe").Availability);
        Assert.Equal("Array<number>", TypeAt(value, "safe").Type.Text);
    }

    [Fact]
    public void AliasExpansionUsesStayExactWithoutPublishingSubstitutedDefinitionFacts()
    {
        var value = Check("class Box<T> {} type Alias<T> = Box</*definition*/T>; let box: /*alias*/Alias<number>; type Broken = Missing; function inferred(value: /*first*/Broken) { return value; } let later: /*cached*/Broken; type Legitimate = any; let legitimate: /*any*/Legitimate;");
        Clean(value);
        Assert.Equal("Box<number>", TypeAt(value, "alias").Type.Text);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "first").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "cached").Availability);
        Assert.Equal(EditorFactAvailability.Available, TypeAt(value, "any").Availability);
        Assert.Equal("any", TypeAt(value, "any").Type.Text);
        var definition = value.Document.EditorSyntax!.FindNarrowest(At(value, "definition"), EditorSyntaxKind.Name)!;
        Assert.Null(value.Facts.GetTypeUse(value.Document, Assert.IsType<NamedTypeNode>(definition.Node)));
    }

    [Fact]
    public void FailedTypeResolutionAndUnusedLazyAliasDoNotClaimResolvedTypes()
    {
        var value = Check("type Idle = { value: /*lazy*/Missing }; let broken: /*broken*/Array<number, string>; let safe: /*safe*/number;");
        Assert.False(value.Result.IsSuccess);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(value, "broken").Availability);
        Assert.Equal(EditorFactAvailability.Available, TypeAt(value, "safe").Availability);
        var lazy = value.Document.EditorSyntax!.FindNarrowest(At(value, "lazy"), EditorSyntaxKind.Name)!;
        Assert.Null(value.Facts.GetTypeUse(value.Document, Assert.IsType<NamedTypeNode>(lazy.Node)));
    }

    [Fact]
    public void SameSourceTypeReattemptClearsSuccessAndLeavesPreviouslyFrozenValuesIntact()
    {
        var value = Check("class Box<T> {} let value: /*type*/Box<number>;");
        Clean(value);
        var original = TypeAt(value, "type");
        var environment = typeof(TypeChecker).GetField("_environment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = environment.GetValue(value.Checker);
        environment.SetValue(value.Checker, new TypeEnvironment());
        try
        {
            var resolve = typeof(TypeChecker).GetMethod("TryToTypeInfo", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.IsType<TypeInfo.Any>(resolve.Invoke(value.Checker, [original.Owner]));
        }
        finally { environment.SetValue(value.Checker, previous); }
        var latest = value.Checker.EditorFacts.Freeze().GetTypeUse(value.Document, original.Owner)!;
        Assert.Equal(EditorFactAvailability.Unavailable, latest.Availability);
        Assert.Equal(EditorFactAvailability.Available, original.Availability);
        Assert.Equal("Box<number>", original.Type.Text);
    }

    [Fact]
    public void UnprovenAnnotationsVetoTheirDeclarationAndBoundUseButKeepActualAnyIndependent()
    {
        var value = Check("let broken: Missing; /*broken*/broken; let valid: any; /*valid*/valid; function run(parameter: Missing) { /*parameter*/parameter; } class Model { #hidden: Missing; exposed: Missing; read() { return this./*private*/#hidden; } } const model = new Model(); model./*member*/exposed;");
        foreach (string name in new[] { "broken", "parameter", "run", "#hidden", "exposed" })
            Assert.All(value.Facts.Declarations.Where(declaration => declaration.LocalName == name),
                declaration => Assert.Equal(EditorFactAvailability.Unavailable, declaration.Availability));
        foreach (string marker in new[] { "broken", "parameter" })
            Assert.Equal(EditorFactAvailability.Unavailable, value.Facts.FindOccurrence(value.Document, At(value, marker))!.Availability);
        Assert.Equal(EditorFactAvailability.Available, value.Facts.FindOccurrence(value.Document, At(value, "valid"))!.Availability);
        foreach (string marker in new[] { "private", "member" })
        {
            var member = value.Document.EditorSyntax!.FindMember(At(value, marker))!;
            var presentation = Assert.Single(value.Facts.GetReceiverMembers(value.Document, member.Receiver).Members,
                candidate => candidate.Name == member.Name.Lexeme);
            Assert.False(presentation.Type.IsAvailable);
        }
    }

    [Fact]
    public void ImportedAliasPreservesParserOwnerAndInheritsCanonicalAnnotationUnavailability()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpts_hover_metadata_" + Guid.NewGuid().ToString("N"));
        string entryPath = Path.GetFullPath(Path.Combine(directory, "main.ts"));
        string originalPath = Path.GetFullPath(Path.Combine(directory, "original.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entryPath] = "import { original as local } from './original'; local;",
            [originalPath] = "export let original: Missing;",
        };
        var resolver = new ModuleResolver(entryPath, files) { CaptureEditorSyntax = true };
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker().WithEditorMetadata();
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        var facts = checker.EditorFacts.Freeze();
        int localOffset = files[entryPath].IndexOf("local", StringComparison.Ordinal);
        var source = entry.Document!.EditorSyntax!.FindNarrowest(localOffset, EditorSyntaxKind.Name)!;
        var declaration = Assert.Single(facts.GetDeclarations(entry.Document, source.Node));
        Assert.Equal("local", declaration.LocalName);
        Assert.Equal("original", declaration.Binding!.CanonicalName);
        Assert.Equal(EditorFactAvailability.Unavailable, declaration.Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, facts.FindOccurrence(entry.Document, files[entryPath].LastIndexOf("local", StringComparison.Ordinal))!.Availability);
    }

    [Fact]
    public void ContextualImportNamesUseExactParserAliasesAndCanonicalIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpts_hover_keywords_" + Guid.NewGuid().ToString("N"));
        string entryPath = Path.GetFullPath(Path.Combine(directory, "main.ts"));
        string originalPath = Path.GetFullPath(Path.Combine(directory, "original.ts"));
        const string source = "import { type as from } from './original'; from;";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entryPath] = source,
            [originalPath] = "export let item: number = 1; export { item as type };",
        };
        var resolver = new ModuleResolver(entryPath, files) { CaptureEditorSyntax = true };
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker().WithEditorMetadata();
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        var facts = checker.EditorFacts.Freeze();
        int sourceOffset = source.IndexOf("type", StringComparison.Ordinal);
        int localOffset = source.IndexOf("from", StringComparison.Ordinal);
        var sourceName = entry.Document!.EditorSyntax!.FindNarrowest(sourceOffset, EditorSyntaxKind.Name)!;
        var localName = entry.Document.EditorSyntax.FindNarrowest(localOffset, EditorSyntaxKind.Name)!;
        Assert.Same(sourceName.Node, localName.Node);
        var declaration = Assert.Single(facts.GetDeclarations(entry.Document, localName.Node));
        Assert.Equal(localOffset, declaration.Source.Name!.Start);
        Assert.Equal("from", declaration.LocalName);
        Assert.Equal("number", declaration.Type.Text);
        Assert.Equal(EditorFactAvailability.Available, declaration.Availability);
        Assert.Equal("item", declaration.Binding!.CanonicalName);
        Assert.Equal(declaration.Binding.Id, Assert.Single(checker.Bindings.FindSymbols(entry.Document, sourceOffset)).Id);
        Assert.Equal(declaration.Binding.Id, Assert.Single(checker.Bindings.FindSymbols(entry.Document, localOffset)).Id);
        Assert.Equal("number", facts.FindOccurrence(entry.Document, source.LastIndexOf("from", StringComparison.Ordinal))!.Type.Text);
    }

    [Fact]
    public void UnsupportedMethodLocalGenericAnnotationsRemainRawAnyButUnprovenForEditors()
    {
        const string source = "class Base<T> { method<U>(input: /*parameter*/U): /*return*/U { return input; } } const value = new Base<number>(); value./*member*/method;";
        var captured = Check(source);
        var ordinary = Check(source, enabled: false);
        Clean(captured); Clean(ordinary);
        var declaration = Assert.IsType<Stmt.Class>(captured.Statements[0]);
        var raw = captured.Result.TypeMap.GetClassType(declaration)!.Methods["method"];
        var ordinaryDeclaration = Assert.IsType<Stmt.Class>(ordinary.Statements[0]);
        var ordinaryRaw = ordinary.Result.TypeMap.GetClassType(ordinaryDeclaration)!.Methods["method"];
        Assert.Equal("(input: any) => any", EditorTypeRenderer.Render(ordinaryRaw, EditorTypeRenderContext.Value).Text);
        Assert.Equal(EditorTypeRenderer.Render(ordinaryRaw).Text, EditorTypeRenderer.Render(raw).Text);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(captured, "parameter").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, TypeAt(captured, "return").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable,
            Assert.Single(captured.Facts.GetDeclarations(captured.Document, declaration.Methods[0])).Availability);
        var member = captured.Document.EditorSyntax!.FindMember(At(captured, "member"))!;
        Assert.False(Assert.Single(captured.Facts.GetReceiverMembers(captured.Document, member.Receiver).Members,
            candidate => candidate.Name == "method").Type.IsAvailable);
    }

    [Fact]
    public void DirectUnprovenAssertionInitializerDoesNotBecomeAProvenInferredAnyBinding()
    {
        const string source = "const broken = (1 as Missing); /*broken*/broken; const valid = 1 as any; /*valid*/valid;";
        var value = Check(source);
        var ordinary = Check(source, enabled: false);
        Assert.Equal(ordinary.Result.Diagnostics.Select(error => (error.TsCode, error.Message)),
            value.Result.Diagnostics.Select(error => (error.TsCode, error.Message)));
        Assert.Equal(EditorFactAvailability.Unavailable,
            Assert.Single(value.Facts.Declarations, declaration => declaration.LocalName == "broken").Availability);
        Assert.Equal(EditorFactAvailability.Unavailable, value.Facts.FindOccurrence(value.Document, At(value, "broken"))!.Availability);
        Assert.Equal(EditorFactAvailability.Available, value.Facts.FindOccurrence(value.Document, At(value, "valid"))!.Availability);
        Assert.Equal("any", value.Facts.FindOccurrence(value.Document, At(value, "valid"))!.Type.Text);
    }

    [Fact]
    public void PublicOverloadSurfaceSurvivesInferenceForDeclarationsUsesAndNestedCallableTypes()
    {
        var value = Check("function choose(value: number): number; function choose(value: string): string; function choose(value: any) { return value; } const holder = { choose: choose }; /*use*/choose; /*holder*/holder;");
        Clean(value);
        var implementation = value.Statements.OfType<Stmt.Function>().Last();
        var declaration = Assert.Single(value.Facts.GetDeclarations(value.Document, implementation));
        var use = value.Facts.FindOccurrence(value.Document, At(value, "use"))!;
        var nested = value.Facts.FindOccurrence(value.Document, At(value, "holder"))!;
        foreach (string text in new[] { declaration.Type.Text, use.Type.Text, nested.Type.Text })
        {
            Assert.Contains("value: number", text, StringComparison.Ordinal);
            Assert.Contains("value: string", text, StringComparison.Ordinal);
            Assert.DoesNotContain("value: any", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClassMethodReceiverAndImplementationDeclarationRenderOnlyPublicOverloads()
    {
        var value = Check("class Model { choose(value: number): number; choose(value: string): string; choose(value: any) { return value; } } const model = new Model(); model./*member*/choose;");
        Clean(value);
        var member = value.Document.EditorSyntax!.FindMember(At(value, "member"))!;
        var presentation = Assert.Single(value.Facts.GetReceiverMembers(value.Document, member.Receiver).Members,
            candidate => candidate.Name == "choose");
        var implementation = Assert.IsType<Stmt.Class>(value.Statements[0]).Methods.Last();
        var declaration = Assert.Single(value.Facts.GetDeclarations(value.Document, implementation));
        foreach (string text in new[] { presentation.Type.Text, declaration.Type.Text })
        {
            Assert.Contains("value: number", text, StringComparison.Ordinal);
            Assert.Contains("value: string", text, StringComparison.Ordinal);
            Assert.DoesNotContain("value: any", text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("method", true)]
    [InlineData("missing", false)]
    public void SuperKeywordTypeIsSeparateFromMemberResultAndSurvivesAMissingMember(string memberName, bool success)
    {
        var value = Check("class Base { method(): number { return 1; } } class Derived extends Base { run() { return /*super*/super." + memberName + "(); } }");
        Assert.Equal(success, value.Result.IsSuccess);
        var owner = Assert.IsType<Expr.Super>(value.Document.EditorSyntax!.FindNarrowest(At(value, "super"), EditorSyntaxKind.Name)!.Node);
        var receiver = value.Facts.GetReceiverMembers(value.Document, owner);
        Assert.Equal("Base", receiver.ReceiverType!.Text);
        Assert.True(receiver.ReceiverType.IsAvailable);
        var occurrence = value.Facts.GetOccurrence(value.Document, owner)!;
        if (success) Assert.Contains("number", occurrence.Type.Text, StringComparison.Ordinal);
        else Assert.Equal(EditorFactAvailability.Unavailable, occurrence.Availability);
    }

    [Fact]
    public void PublicSurfacesRemainBoundedReferenceBasedAndFrozenWithGenericShadowing()
    {
        var index = new EditorSemanticIndex();
        var document = new SourceDocument("surfaces.ts", "model");
        var owner = new Expr.Variable(new(TokenType.IDENTIFIER, "model", null, 1, 0));
        var parameter = new TypeInfo.TypeParameter("T");
        var arguments = new List<TypeInfo> { parameter };
        var signature = new TypeInfo.Function(arguments, parameter, ParamNames: ["value"]);
        var actual = new TypeInfo.Function([TypeInfo.Any.Shared], TypeInfo.Any.Shared);
        index.RegisterCallableSurface(actual, Enumerable.Repeat<TypeInfo>(signature, 33).ToArray(), [parameter]);
        index.SetReceiverMembers(document, owner, [new("method", actual, Substitutions: new Dictionary<string, TypeInfo> { ["T"] = TypeInfo.String.Shared })]);
        var before = index.Freeze();
        var type = Assert.Single(before.GetReceiverMembers(document, owner).Members).Type;
        Assert.Contains("<T>(value: T): T", type.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("value: string", type.Text, StringComparison.Ordinal);
        Assert.True(type.IsTruncated);
        arguments[0] = TypeInfo.Primitive.Number;
        index.Clear();
        Assert.Contains("value: T", type.Text, StringComparison.Ordinal);
        Assert.Empty(index.Freeze().GetReceiverMembers(document, owner).Members);

        var recursive = new TypeInfo.Function([], TypeInfo.Primitive.Number);
        recursive.ParamTypes.Add(recursive);
        var surfaces = new Dictionary<TypeInfo, EditorCallableSurface>(ReferenceEqualityComparer.Instance)
        { [recursive] = new([recursive]) };
        var bounded = EditorTypeRenderer.Render(recursive, callableSurfaces: surfaces);
        Assert.True(bounded.IsTruncated);
        Assert.True(bounded.Text.Length <= 4096);
        var equal = recursive with { };
        var unmapped = EditorTypeRenderer.Render(equal, callableSurfaces: surfaces);
        Assert.StartsWith("(", unmapped.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearDocumentRemovesTypeUsesAndSuperReceiverProofWithoutMutatingFrozenFacts()
    {
        var value = Check("class Base { method(): number { return 1; } } class Derived extends Base { run(): /*type*/number { return /*super*/super.method(); } }");
        Clean(value);
        var annotation = TypeAt(value, "type");
        var owner = Assert.IsType<Expr.Super>(value.Document.EditorSyntax!.FindNarrowest(At(value, "super"), EditorSyntaxKind.Name)!.Node);
        value.Checker.EditorFacts.ClearDocument(value.Document);
        var after = value.Checker.EditorFacts.Freeze();
        Assert.Empty(after.TypeUses);
        Assert.Null(after.GetReceiverMembers(value.Document, owner).ReceiverType);
        Assert.Equal("number", annotation.Type.Text);
        Assert.Equal("Base", value.Facts.GetReceiverMembers(value.Document, owner).ReceiverType!.Text);
    }
}
