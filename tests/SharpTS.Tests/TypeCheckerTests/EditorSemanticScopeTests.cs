using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
using BindingFlags = System.Reflection.BindingFlags;
using TargetInvocationException = System.Reflection.TargetInvocationException;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EditorSemanticScopeTests
{
    private sealed record Checked(SourceDocument Document, List<Stmt> Statements, TypeChecker Checker,
        TypeCheckDiagnosticResult Result, FrozenEditorSemanticIndex Facts);

    private static Checked Check(string source, bool capture = true)
    {
        var document = new SourceDocument("scopes.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata(capture);
        var result = checker.CheckWithRecovery(statements, document);
        return new(document, statements, checker, result, checker.EditorFacts.Freeze());
    }

    private static int At(Checked value, string marker)
    {
        string comment = "/*" + marker + "*/";
        int start = value.Document.Text.IndexOf(comment, StringComparison.Ordinal);
        Assert.True(start >= 0);
        return start + comment.Length;
    }

    private static EditorVisibleBinding Local(Checked value, string marker, string name,
        BindingNamespace facet = BindingNamespace.Value) =>
        Assert.Single(value.Facts.GetVisibleBindings(value.Document, At(value, marker)),
            binding => binding.LocalName == name && binding.Facet == facet);

    private static void Clean(Checked value) =>
        Assert.True(value.Result.IsSuccess, string.Join(Environment.NewLine, value.Result.Errors));

    [Fact]
    public void DeclarationTypeIsSeparateFromFlowNarrowedOccurrence()
    {
        var value = Check("let widened = 1; const declared: string | number = 2; /*use*/declared;");
        Clean(value);
        var widened = Assert.Single(value.Facts.Declarations, declaration => declaration.LocalName == "widened");
        Assert.Equal("number", widened.Type.Text);
        var declared = Assert.Single(value.Facts.Declarations, declaration => declaration.LocalName == "declared");
        Assert.Contains("string", declared.Type.Text);
        Assert.Contains("number", declared.Type.Text);
        var occurrence = Assert.IsType<EditorOccurrenceFact>(value.Facts.FindOccurrence(value.Document, At(value, "use")));
        Assert.Equal("number", occurrence.Type.Text);
    }

    [Fact]
    public void BlockLocalShadowsParameterBeforeItsInitializationWithoutLeakingOutside()
    {
        var value = Check("function run(item: string) { { /*before*/item; let item = 1; /*after*/item; } /*outside*/item; }");
        Clean(value);
        var before = Local(value, "before", "item");
        Assert.Equal(EditorFactAvailability.Unavailable, before.Availability);
        Assert.Equal("number", before.Type.Text);
        Assert.Equal(EditorFactAvailability.Available, Local(value, "after", "item").Availability);
        Assert.Equal("string", Local(value, "outside", "item").Type.Text);
        Assert.NotEqual(before.Binding!.Id, Local(value, "outside", "item").Binding!.Id);
    }

    [Fact]
    public void ParameterDefaultsSeeEarlierParametersAndLaterNamesShadowOuterBindings()
    {
        var value = Check("const later: string = 'outside'; function run(first: number = 1, second: number = /*earlier*/first, later: number = 2) { /*body*/later; }");
        Clean(value);
        Assert.Equal("number", Local(value, "earlier", "first").Type.Text);
        Assert.Equal(EditorFactAvailability.Available, Local(value, "earlier", "first").Availability);
        var later = Local(value, "earlier", "later");
        Assert.Equal(EditorFactAvailability.Unavailable, later.Availability);
        Assert.Equal("number", later.Type.Text);
        Assert.Equal("number", Local(value, "body", "later").Type.Text);
    }

    [Fact]
    public void GenericAndContextualArrowParametersHaveSourceOwnedScopeFacts()
    {
        var value = Check("function identity<T>(value: T): T { /*generic*/return value; } const callback: (item: number) => number = item => { /*arrow*/return item; };");
        Clean(value);
        Assert.Equal("T", Local(value, "generic", "T", BindingNamespace.Type).Type.Text);
        Assert.Equal("T", Local(value, "generic", "value").Type.Text);
        Assert.Equal("number", Local(value, "arrow", "item").Type.Text);
        Assert.All(value.Facts.Scopes, scope => Assert.IsNotType<TypeEnvironment>(scope.Owner));
        Assert.All(value.Facts.Scopes.SelectMany(scope => scope.Bindings),
            binding => Assert.IsNotType<TypeEnvironment>(binding.DeclarationOwner));
    }

    [Fact]
    public void CatchLoopAndSwitchBindingsStayInTheirWrittenLexicalScopes()
    {
        var value = Check("try { const tried = 1; /*try*/tried; } catch (caught: any) { /*catch*/caught; const caughtLocal = 1; } for (let index = 0; index < 1; index++) { /*loop*/index; } switch (1) { case 1: const choice = 1; /*case*/choice; break; } /*outside*/1;");
        Clean(value);
        Assert.Equal("1", Local(value, "try", "tried").Type.Text);
        Assert.Equal("any", Local(value, "catch", "caught").Type.Text);
        Assert.Equal("number", Local(value, "loop", "index").Type.Text);
        Assert.Equal("1", Local(value, "case", "choice").Type.Text);
        var outside = value.Facts.GetVisibleBindings(value.Document, At(value, "outside"));
        Assert.DoesNotContain(outside, binding => binding.LocalName is "tried" or "caught" or "caughtLocal" or "index" or "choice");
    }

    [Fact]
    public void FailedFinalInitializerHasUnavailableDeclarationAndEarlierIndependentFactsSurvive()
    {
        var value = Check("const safe = 1; const broken: number = 'bad'; /*safe*/safe;");
        Assert.False(value.Result.IsSuccess);
        Assert.Equal(EditorFactAvailability.Unavailable,
            Assert.Single(value.Facts.Declarations, declaration => declaration.LocalName == "broken").Availability);
        Assert.Equal(EditorFactAvailability.Available,
            Assert.Single(value.Facts.Declarations, declaration => declaration.LocalName == "safe").Availability);
        Assert.Equal("1", value.Facts.FindOccurrence(value.Document, At(value, "safe"))!.Type.Text);
    }

    [Fact]
    public void FailedSameAstExpressionAttemptClearsItsProofWithoutTrustingStaleTypeMap()
    {
        var value = Check("const model = { item: 1 }; /*get*/model.item;");
        Clean(value);
        var get = Assert.IsType<Expr.Get>(Assert.IsType<Stmt.Expression>(value.Statements[^1]).Expr);
        var first = value.Facts.GetOccurrence(value.Document, get)!;
        var oldType = value.Result.TypeMap.Get(get);
        var environmentField = typeof(TypeChecker).GetField("_environment", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var original = (TypeEnvironment)environmentField.GetValue(value.Checker)!;
        var later = new TypeEnvironment(original);
        later.Define("model", TypeInfo.Unknown.Shared);
        environmentField.SetValue(value.Checker, later);
        try
        {
            var check = typeof(TypeChecker).GetMethod("CheckExpr", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var failure = Assert.Throws<TargetInvocationException>(() => check.Invoke(value.Checker, [get]));
            Assert.IsAssignableFrom<TypeCheckException>(failure.InnerException);
        }
        finally { environmentField.SetValue(value.Checker, original); }
        Assert.Same(oldType, value.Result.TypeMap.Get(get));
        Assert.Equal(EditorFactAvailability.Unavailable, value.Checker.EditorFacts.Freeze().GetOccurrence(value.Document, get)!.Availability);
        Assert.Equal(EditorFactAvailability.Available, first.Availability);
    }

    [Fact]
    public void AnnotatedObjectInitializerKeepsOriginalSourceOwnerAcrossFreshnessClone()
    {
        var value = Check("let model: { item: number } = /*initializer*/{ item: 1 };");
        Clean(value);
        var declaration = Assert.IsType<Stmt.Var>(value.Statements[0]);
        var occurrence = value.Facts.GetOccurrence(value.Document, declaration.Initializer!);
        Assert.NotNull(occurrence);
        Assert.Equal(EditorFactAvailability.Available, occurrence.Availability);
        Assert.Contains("item", occurrence.Type.Text);
    }

    [Fact]
    public void ClassMembersAndSelfNameUseTheirActualSourceOwners()
    {
        var value = Check("class Model<T> { value: T; count = 1; #hidden: number = 1; method(input: T): T { /*self*/const owner = Model; return input; } get size(): number { return 1; } }");
        Clean(value);
        var declaration = Assert.IsType<Stmt.Class>(value.Statements[0]);
        var field = Assert.Single(declaration.Fields, field => field.Name.Lexeme == "value");
        var count = Assert.Single(declaration.Fields, field => field.Name.Lexeme == "count");
        var hidden = Assert.Single(declaration.Fields, field => field.Name.Lexeme == "#hidden");
        Assert.Equal("T", Assert.Single(value.Facts.GetDeclarations(value.Document, field)).Type.Text);
        var ordinary = new TypeChecker().CheckWithRecovery(value.Statements, value.Document);
        Assert.IsType<TypeInfo.Any>(ordinary.TypeMap.GetClassType(declaration)!.FieldTypes["count"]);
        Assert.Equal("any", Assert.Single(value.Facts.GetDeclarations(value.Document, count)).Type.Text);
        Assert.Equal("number", Assert.Single(value.Facts.GetDeclarations(value.Document, hidden)).Type.Text);
        Assert.Equal(EditorFactAvailability.Available, Local(value, "self", "Model").Availability);
        Assert.Equal("T", Local(value, "self", "T", BindingNamespace.Type).Type.Text);
    }

    [Fact]
    public void NamespaceAndTypeDeclarationParameterScopesDoNotLeakNames()
    {
        var value = Check("namespace Space { export const member = 1; /*inside*/member; } interface Box<T> { value: T; } type Alias<U> = U; /*outside*/1;");
        Clean(value);
        Assert.Equal("1", Local(value, "inside", "member").Type.Text);
        var namespaceDeclaration = Assert.Single(value.Statements.OfType<Stmt.Namespace>());
        Assert.Contains(value.Facts.GetDeclarations(value.Document, namespaceDeclaration), declaration =>
            declaration.LocalName == "Space" && declaration.Type.Text == "typeof Space" &&
            declaration.Availability == EditorFactAvailability.Available);
        Assert.DoesNotContain(value.Facts.GetVisibleBindings(value.Document, At(value, "outside")),
            binding => binding.LocalName is "member" or "T" or "U");
        var alias = Assert.Single(value.Facts.Declarations, declaration => declaration.LocalName == "Alias");
        Assert.Equal(EditorFactAvailability.Unavailable, alias.Availability);
        Assert.NotNull(alias.Binding);
        Assert.Equal("Alias", Local(value, "outside", "Alias", BindingNamespace.Type).LocalName);
    }

    [Fact]
    public void CaptureOffRetainsNoEditorFactsAndMetadataResetDoesNotMutateFrozenGeneration()
    {
        var ordinary = Check("const local = 1;", capture: false);
        Assert.False(ordinary.Checker.EditorFacts.IsEnabled);
        Assert.Equal(0, ordinary.Facts.Count);
        var value = Check("function run(item: number) { return item; }");
        long generation = value.Facts.Generation;
        int count = value.Facts.Count;
        value.Checker.CheckWithRecovery(value.Statements, value.Document);
        Assert.NotEqual(generation, value.Checker.EditorFacts.Freeze().Generation);
        Assert.Equal(count, value.Facts.Count);
        value.Checker.WithEditorMetadata(false);
        Assert.True(value.Checker.Members.IsEnabled);
        Assert.Equal(0, value.Checker.EditorFacts.Freeze().Count);
    }

    [Fact]
    public void ImportAliasKeepsLocalSpellingAndCanonicalTargetIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpts_editor_alias_" + Guid.NewGuid().ToString("N"));
        string entryPath = Path.GetFullPath(Path.Combine(directory, "main.ts"));
        string originalPath = Path.GetFullPath(Path.Combine(directory, "original.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entryPath] = "import { original as local } from './original'; /*use*/local;",
            [originalPath] = "export const original = 1;",
        };
        var resolver = new ModuleResolver(entryPath, files) { CaptureEditorSyntax = true };
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker().WithEditorMetadata();
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var facts = checker.EditorFacts.Freeze();
        var local = Assert.Single(facts.GetVisibleBindings(entry.Document!, files[entryPath].LastIndexOf("local", StringComparison.Ordinal)),
            binding => binding.LocalName == "local" && binding.Facet == BindingNamespace.Value);
        Assert.Equal("original", local.Binding!.CanonicalName);
        Assert.Equal("local", Assert.Single(facts.Declarations,
            declaration => ReferenceEquals(declaration.Source.Document, entry.Document) &&
                declaration.LocalName == "local").LocalName);
        Assert.Equal(originalPath, Assert.Single(local.Binding.Declarations).Document.Path, ignoreCase: true);
    }

    [Fact]
    public void UnvisitedCatchBodyDoesNotExposeAnOuterBindingOverItsWrittenLocal()
    {
        // Existing checking skips a parameterless catch body. Its syntax still proves that
        // a same-spelled local could hide the outer binding, so a query must refuse the guess.
        var value = Check("const local: string = 'outer'; try {} catch { const local = 1; /*inside*/local; }");
        Clean(value);
        Assert.Empty(value.Facts.GetVisibleBindings(value.Document, At(value, "inside")));
    }

    [Fact]
    public void ErrorLimitBeforeLocalDeclarationCannotExposeAnOuterSameSpelledBinding()
    {
        const string source = "const local: string = 'outer'; { missingFirst; const local = 1; /*inside*/local; } 1;";
        var document = new SourceDocument("limited.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(new TypeCheckerOptions { MaxErrors = 1 }).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.True(result.HitErrorLimit);
        int caret = source.IndexOf("/*inside*/", StringComparison.Ordinal) + "/*inside*/".Length;
        var facts = checker.EditorFacts.Freeze();
        Assert.Empty(facts.GetVisibleBindings(document, caret));
        Assert.Contains(facts.Declarations, declaration => declaration.LocalName == "local" &&
            declaration.Availability == EditorFactAvailability.Available);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparatoryModuleBodyCannotPublishAnAuthoritativelySkippedMemberUse(bool captureEditor)
    {
        const string source = "class Model { value: number = 1; } const model = new Model(); function run(): void { missing; model.value; }";
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts_editor_prep_" + Guid.NewGuid().ToString("N"), "main.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [path] = source };
        var resolver = new ModuleResolver(path, files, TypeScriptProgramOptions.Disabled) { CaptureEditorSyntax = true };
        var module = resolver.LoadModule(path);
        var document = module.Document!;
        var checker = new TypeChecker(new TypeCheckerOptions { MaxErrors = 1 }).WithMemberProvenance();
        if (captureEditor) checker.WithEditorMetadata();
        var typeMap = checker.CheckModules(resolver.GetModulesInOrder(module), resolver);
        Assert.Contains(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        int useOffset = source.LastIndexOf("value", StringComparison.Ordinal);
        var member = Assert.IsType<Expr.Get>(Assert.Single(document.EditorSyntax!.Members,
            context => context.Name.Start == useOffset).Owner);
        // The preparatory pass really visited this source use; TypeMap retains that old
        // successful result even though the error limit skips it in the final body pass.
        Assert.NotNull(typeMap.Get(member));
        var members = checker.Members.Freeze();
        Assert.Empty(members.FindResolution(document, useOffset).Candidates);
        Assert.False(members.FindResolution(document, useOffset).IsResolved);
        Assert.True(members.FindResolution(document, source.IndexOf("value", StringComparison.Ordinal)).IsResolved);
        if (captureEditor)
        {
            var facts = checker.EditorFacts.Freeze();
            Assert.True(facts.GetOccurrence(document, member) is null or { Availability: EditorFactAvailability.Unavailable });
            Assert.False(facts.GetReceiverMembers(document, member.Object).IsComplete);
        }
    }
}
