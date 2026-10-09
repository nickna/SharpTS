using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class SourceMemberDeclarationTests
{
    private static (TypeChecker Checker, List<Stmt> Statements, SourceDocument Document, TypeCheckDiagnosticResult Result) Check(
        string source, string path = "members.ts", bool isVirtual = false, bool expectSuccess = true)
    {
        var document = new SourceDocument(path, source, isVirtual);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document)
            .WithEditorSyntax().AsDeclarationFile(path.EndsWith(".d.ts", StringComparison.Ordinal)).ParseOrThrow();
        var checker = new TypeChecker().WithMemberProvenance();
        var result = checker.CheckWithRecovery(statements, document);
        if (expectSuccess) Assert.True(result.IsSuccess, string.Join("\n", result.Diagnostics));
        return (checker, statements, document, result);
    }

    [Fact]
    public void SameSpelledClassesAndStaticInstanceFacetsHaveDistinctOrigins()
    {
        var checkedProgram = Check("class First { value: number = 1; static value: string = 'one'; } class Second { value: number = 2; }");
        var symbols = checkedProgram.Checker.Members.Symbols.Where(symbol => symbol.Name == "value").ToArray();
        Assert.Equal(3, symbols.Length);
        Assert.Equal(3, symbols.Select(symbol => symbol.Id).Distinct().Count());
        Assert.Equal(2, symbols.Select(symbol => symbol.DeclaringClassId).Distinct().Count());
        Assert.Equal(2, symbols.Count(symbol => symbol.Facet == MemberFacet.Instance));
        Assert.Single(symbols, symbol => symbol.Facet == MemberFacet.Static);
        Assert.All(symbols, symbol => Assert.False(symbol.CanRename));
        Assert.All(symbols, symbol => Assert.Same(checkedProgram.Document, Assert.Single(symbol.Declarations).Document));
    }

    [Fact]
    public void OverloadsAccessorPairsAndAutoAccessorsKeepCanonicalSourceGroups()
    {
        const string source = "class C { map(value: number): number; map(value: string): string; map(value: any): any { return value; } get size(): number { return 1; } set size(value: number) {} accessor count: number = 0; }";
        var checkedProgram = Check(source);
        var symbols = checkedProgram.Checker.Members.Symbols;
        var overload = Assert.Single(symbols, symbol => symbol.Name == "map");
        Assert.Equal(SourceMemberKind.Method, overload.Kind);
        Assert.Equal(3, overload.Declarations.Count);
        var accessor = Assert.Single(symbols, symbol => symbol.Name == "size");
        Assert.Equal(SourceMemberKind.Accessor, accessor.Kind);
        Assert.Equal(2, accessor.Declarations.Count);
        Assert.Equal(SourceMemberKind.AutoAccessor, Assert.Single(symbols, symbol => symbol.Name == "count").Kind);
        Assert.All(overload.Declarations.Concat(accessor.Declarations), declaration =>
            Assert.Equal(declaration.Name.Lexeme, source[declaration.Span.Start..declaration.Span.End]));
    }

    [Fact]
    public void ParameterPropertyHasSeparateDeniedLexicalAndPropertyFacets()
    {
        const string source = "class C { constructor(public readonly token: number) { const local = token; } }";
        var checkedProgram = Check(source);
        var property = Assert.Single(checkedProgram.Checker.Members.Symbols, symbol => symbol.Name == "token");
        Assert.Equal(SourceMemberKind.ParameterProperty, property.Kind);
        Assert.IsType<Stmt.Parameter>(Assert.Single(property.Declarations).Owner);
        int declarationOffset = source.IndexOf("token:", StringComparison.Ordinal);
        var lexical = Assert.Single(checkedProgram.Checker.Bindings.FindSymbols(checkedProgram.Document, declarationOffset));
        Assert.Equal(BindingRenameEligibility.ParameterPropertyRequiresCoordinatedEdits, lexical.RenameEligibility);
        var use = Assert.Single(checkedProgram.Checker.Bindings.FindSymbols(checkedProgram.Document, source.LastIndexOf("token", StringComparison.Ordinal)));
        Assert.Same(lexical, use);
        Assert.False(property.CanRename);
    }

    [Fact]
    public void OrdinaryParameterRetainsLexicalRenameEligibility()
    {
        const string source = "function f(value: number): number { return value; }";
        var checkedProgram = Check(source);
        var parameter = Assert.Single(checkedProgram.Checker.Bindings.FindSymbols(checkedProgram.Document, source.IndexOf("value:", StringComparison.Ordinal)));
        Assert.Equal(BindingRenameEligibility.AllowedLexical, parameter.RenameEligibility);
        Assert.Empty(checkedProgram.Checker.Members.Symbols);
    }

    [Fact]
    public void ClassExpressionsAmbientClassesAndPrivateFacetsHaveRealSourceOrigins()
    {
        var expression = Check("const value = class Named { #secret: number = 1; static #staticSecret: string = 's'; #read(): number { return this.#secret; } field: number = 2; }; ");
        Assert.Equal(4, expression.Checker.Members.Symbols.Count);
        Assert.Contains(expression.Checker.Members.Symbols, symbol => symbol.Name == "#secret" && symbol.Facet == MemberFacet.PrivateInstance);
        Assert.Contains(expression.Checker.Members.Symbols, symbol => symbol.Name == "#staticSecret" && symbol.Facet == MemberFacet.PrivateStatic);
        var classExpression = Assert.IsType<Expr.ClassExpr>(Assert.IsType<Stmt.Const>(Assert.Single(expression.Statements)).Initializer);
        var sourceClass = Assert.Single(expression.Checker.Members.Freeze().Classes);
        Assert.Same(classExpression, sourceClass.Owner);

        var ambient = Check("declare class Ambient<T> { value: T; map(value: T): T; map(value: number): T; }", "ambient.d.ts");
        var method = Assert.Single(ambient.Checker.Members.Symbols, symbol => symbol.Name == "map");
        Assert.Equal(2, method.Declarations.Count);
        Assert.All(method.Declarations, declaration => Assert.Equal("ambient.d.ts", declaration.Document.Path));
    }

    [Fact]
    public void CanonicalClassIdsSurviveGenericFreezingAndInheritedBaseTypes()
    {
        var checkedProgram = Check("class Base<T> { value: T; } class Derived<T> extends Base<T> {} class Override extends Base<number> { value: number = 1; }");
        var baseClass = Assert.IsType<Stmt.Class>(checkedProgram.Statements[0]);
        var derivedClass = Assert.IsType<Stmt.Class>(checkedProgram.Statements[1]);
        var overrideClass = Assert.IsType<Stmt.Class>(checkedProgram.Statements[2]);
        var baseType = checkedProgram.Result.TypeMap.GetClassType(baseClass)!;
        var derivedType = checkedProgram.Result.TypeMap.GetClassType(derivedClass)!;
        var overrideType = checkedProgram.Result.TypeMap.GetClassType(overrideClass)!;
        var inheritedBase = Assert.IsType<TypeInfo.InstantiatedGeneric>(derivedType.Superclass);
        Assert.Equal(baseType.Core.DeclarationId, Assert.IsType<TypeInfo.GenericClass>(inheritedBase.GenericDefinition).Core.DeclarationId);
        var baseOrigin = Assert.Single(checkedProgram.Checker.Members.ResolveSelected(baseType.Core.DeclarationId, MemberFacet.Instance, "value").Candidates);
        var overrideOrigin = Assert.Single(checkedProgram.Checker.Members.ResolveSelected(overrideType.Core.DeclarationId, MemberFacet.Instance, "value").Candidates);
        Assert.NotSame(baseOrigin, overrideOrigin);
        Assert.Empty(checkedProgram.Checker.Members.ResolveSelected(derivedType.Core.DeclarationId, MemberFacet.Instance, "value").Candidates);
    }

    [Fact]
    public void ContextualNamesUseOriginalTokensWhileQuotedNumericAndComputedDeclarationsAreExcluded()
    {
        const string source = "class C { type: number = 1; delete(): number { return 1; } 'quoted': number = 2; 42: number = 3; ['computed']: number = 4; }";
        var checkedProgram = Check(source, expectSuccess: false);
        Assert.Equal(new[] { "type", "delete" }, checkedProgram.Checker.Members.Symbols.Select(symbol => symbol.Name));
        Assert.All(checkedProgram.Checker.Members.Symbols, symbol =>
        {
            var declaration = Assert.Single(symbol.Declarations);
            Assert.Equal(symbol.Name, source[declaration.Span.Start..declaration.Span.End]);
        });
    }

    [Fact]
    public void VirtualLibraryDeclarationsAndOptOutCheckingProduceNoSourceMembers()
    {
        const string source = "class C { value: number; }";
        Assert.Empty(Check(source, "library.d.ts", isVirtual: true).Checker.Members.Symbols);
        var document = new SourceDocument("ordinary.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var ordinary = new TypeChecker();
        ordinary.CheckWithRecovery(statements, document);
        Assert.False(ordinary.Members.IsEnabled);
        Assert.Empty(ordinary.Members.Symbols);
    }

    [Fact]
    public void SourcePrivateEnvironmentFactsCoverNestedDeclarationsAndExpressions()
    {
        var checkedProgram = Check("class Outer { #value: number = 1; method() { class Inner { #value: number = 2; } return class { #value: number = 3; }; } }");
        var classes = checkedProgram.Checker.Members.Freeze().Classes;
        Assert.Equal(3, classes.Count);
        var outer = Assert.Single(classes, source => source.Owner is Stmt.Class { Name.Lexeme: "Outer" });
        Assert.False(outer.IsNestedPrivateEnvironment);
        Assert.True(outer.ContainsNestedClass);
        Assert.All(classes.Where(source => !ReferenceEquals(source, outer)), source => Assert.True(source.IsNestedPrivateEnvironment));
    }

    [Fact]
    public void RecheckingCreatesFreshIdentitiesWithoutMutatingFrozenDeclarations()
    {
        var checkedProgram = Check("class C { value: number = 1; }");
        var frozen = checkedProgram.Checker.Members.Freeze();
        var before = Assert.Single(frozen.Symbols);
        checkedProgram.Checker.CheckWithRecovery(checkedProgram.Statements, checkedProgram.Document);
        var after = Assert.Single(checkedProgram.Checker.Members.Symbols);
        Assert.NotEqual(before.Generation, after.Generation);
        Assert.Single(before.Declarations);
        Assert.Single(after.Declarations);
        Assert.Equal(before.Name, after.Name);
    }

    [Fact]
    public void NamedAndAnonymousGenericClassExpressionsHaveSeparateSourceIdentities()
    {
        var checkedProgram = Check("const Named = class Local<T> { value: T; }; const Anonymous = class<T> { value: T; };");
        var values = checkedProgram.Checker.Members.Symbols.Where(symbol => symbol.Name == "value").ToArray();
        Assert.Equal(2, values.Length);
        Assert.NotEqual(values[0].DeclaringClassId, values[1].DeclaringClassId);
        Assert.Equal(2, checkedProgram.Checker.Members.Freeze().Classes.Count);
        Assert.Contains(checkedProgram.Checker.Members.Freeze().Classes, source => source.Owner is Expr.ClassExpr { Name: null });
        Assert.Contains(checkedProgram.Checker.Members.Freeze().Classes, source => source.Owner is Expr.ClassExpr { Name.Lexeme: "Local" });
    }

    [Fact]
    public void ImportedGenericBaseRetainsSourceOwnerAndRepeatedModulePassesDoNotDuplicateDeclarations()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sharpts_member_origins_" + Guid.NewGuid().ToString("N"));
        string entryPath = Path.GetFullPath(Path.Combine(directory, "main.ts"));
        string basePath = Path.GetFullPath(Path.Combine(directory, "base.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entryPath] = "import { Base as Renamed } from './base'; export class Derived extends Renamed<number> {} const value = new Derived(); value.value;",
            [basePath] = "export class Base<T> { value: T; map(value: T): T; map(value: number): T; map(value: any): T { return this.value; } }",
        };
        var resolver = new ModuleResolver(entryPath, files) { CaptureEditorSyntax = true };
        var entry = resolver.LoadModule(entryPath);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker().WithMemberProvenance();
        var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var field = Assert.Single(checker.Members.Symbols, symbol => symbol.Name == "value");
        Assert.Single(field.Declarations);
        Assert.Equal(basePath, field.Declarations[0].Document.Path, ignoreCase: true);
        Assert.Equal(3, Assert.Single(checker.Members.Symbols, symbol => symbol.Name == "map").Declarations.Count);
        var derived = Assert.IsType<Stmt.Class>(Assert.IsType<Stmt.Export>(entry.Statements[1]).Declaration);
        var inherited = Assert.IsType<TypeInfo.InstantiatedGeneric>(types.GetClassType(derived)!.Superclass);
        Assert.Equal(field.DeclaringClassId, Assert.IsType<TypeInfo.GenericClass>(inherited.GenericDefinition).Core.DeclarationId);
    }

    [Fact]
    public void TsxPreregisteredClassRetainsItsActualNominalSourceIdentity()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts_members_" + Guid.NewGuid().ToString("N"), "main.tsx"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [path] = "class Model { value: number = 1; } const model = new Model(); model.value;",
        };
        var resolver = new ModuleResolver(path, files, TypeScriptProgramOptions.Disabled) { CaptureEditorSyntax = true };
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker().WithMemberProvenance();
        var types = checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var declaration = Assert.Single(entry.Statements.OfType<Stmt.Class>());
        var origin = Assert.Single(checker.Members.Symbols, symbol => symbol.Name == "value");
        int actualClassId = types.GetClassType(declaration)!.Core.DeclarationId;
        Assert.Same(origin, Assert.Single(checker.Members.ResolveSelected(actualClassId, MemberFacet.Instance, "value").Candidates));
        var frozen = checker.Members.Freeze();
        Assert.Same(declaration, frozen.GetClassInfo(actualClassId)!.Owner);
        Assert.Single(frozen.Classes);
        var use = frozen.FindResolution(entry.Document!, files[path].LastIndexOf("value", StringComparison.Ordinal));
        Assert.True(use.IsResolved);
        Assert.Equal(origin.Id, Assert.Single(use.Candidates).Id);
        Assert.Single(origin.Declarations);
        Assert.Same(entry.Document, origin.Declarations[0].Document);
    }
}
