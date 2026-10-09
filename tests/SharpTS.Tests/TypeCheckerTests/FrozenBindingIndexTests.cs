using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class FrozenBindingIndexTests
{
    [Fact]
    public void RenameDenialIsMonotonicAndCopiedAtPublication()
    {
        var document = new SourceDocument("parameter-property.ts", "value");
        var index = new BindingIndex();
        BindingSymbol mutable = index.Declare(Name("value", 0), document, BindingNamespace.Value);
        FrozenBindingIndex before = index.Freeze();

        mutable.DenyRename(BindingRenameEligibility.ParameterPropertyRequiresCoordinatedEdits);
        mutable.DenyRename(BindingRenameEligibility.AllowedLexical);
        FrozenBindingIndex after = index.Freeze();
        index.Clear();

        Assert.Equal(BindingRenameEligibility.AllowedLexical,
            Assert.Single(before.FindSymbols(document, 0)).RenameEligibility);
        Assert.Equal(BindingRenameEligibility.ParameterPropertyRequiresCoordinatedEdits,
            Assert.Single(after.FindSymbols(document, 0)).RenameEligibility);
        Assert.Single(after.FindDefinitions(document, 0));
    }

    [Fact]
    public void PublishedDeclarationsAndOccurrencesSurviveFurtherCheckingAndClear()
    {
        var document = new SourceDocument("frozen.ts", "value value value value");
        var index = new BindingIndex();
        Token declaration = Name("value", 0);
        Token use = Name("value", 6);
        BindingSymbol mutableSymbol = index.Declare(declaration, document, BindingNamespace.Value);
        index.Bind(use, document, mutableSymbol);
        FrozenBindingIndex frozen = index.Freeze();
        FrozenBindingSymbol frozenSymbol = Assert.Single(frozen.FindSymbols(document, use.Start));

        index.Declare(Name("value", 12), document, BindingNamespace.Value, mutableSymbol);
        index.Bind(Name("value", 18), document, mutableSymbol);
        index.Clear();

        Assert.Same(declaration, Assert.Single(frozenSymbol.Declarations).Name);
        Assert.Same(declaration, Assert.Single(frozen.FindDefinitions(document, use.Start)).Name);
        Assert.Equal([declaration, use], frozen.FindReferences(document, use.Start, true)
            .Select(occurrence => occurrence.Name));
        Assert.Same(use, Assert.Single(frozen.FindReferences(document, use.Start, false)).Name);
        Assert.Empty(index.FindSymbols(document, use.Start));
    }

    [Fact]
    public void ValueAndTypeFacetsPreserveUnionAndDeclarationFiltering()
    {
        var document = new SourceDocument("facets.ts", "Class Class Class");
        var index = new BindingIndex();
        Token declaration = Name("Class", 0);
        Token valueUse = Name("Class", 6);
        Token typeUse = Name("Class", 12);
        BindingSymbol value = index.Declare(declaration, document, BindingNamespace.Value);
        BindingSymbol type = index.Declare(declaration, document, BindingNamespace.Type);
        index.Bind(valueUse, document, value);
        index.Bind(typeUse, document, type);
        FrozenBindingIndex frozen = index.Freeze();

        Assert.Equal(2, frozen.FindSymbols(document, declaration.Start).Count);
        Assert.Equal(index.FindReferences(document, declaration.Start, true),
            frozen.FindReferences(document, declaration.Start, true));
        Assert.Equal(index.FindReferences(document, declaration.Start, false),
            frozen.FindReferences(document, declaration.Start, false));
        Assert.Equal(index.FindDefinitions(document, typeUse.Start, BindingNamespace.Type),
            frozen.FindDefinitions(document, typeUse.Start, BindingNamespace.Type));
        Assert.Empty(frozen.FindDefinitions(document, typeUse.Start, BindingNamespace.Value));
    }

    [Fact]
    public void TokenDeclaredInOnlyOneSelectedFacetRemainsAReference()
    {
        var document = new SourceDocument("mixed-facet.ts", "Name Name");
        var index = new BindingIndex();
        Token mixed = Name("Name", 0);
        Token typeDeclaration = Name("Name", 5);
        index.Declare(mixed, document, BindingNamespace.Value);
        BindingSymbol type = index.Declare(typeDeclaration, document, BindingNamespace.Type);
        index.Bind(mixed, document, type);
        FrozenBindingIndex frozen = index.Freeze();

        BindingOccurrence reference = Assert.Single(frozen.FindReferences(document, mixed.Start, false));

        Assert.Same(mixed, reference.Name);
        Assert.False(reference.IsDeclaration);
        Assert.Equal(index.FindReferences(document, mixed.Start, false),
            frozen.FindReferences(document, mixed.Start, false));
    }

    [Fact]
    public void NarrowestTokenAndCapturedDocumentIdentityControlSelection()
    {
        var document = new SourceDocument("overlap.ts", "longName");
        var index = new BindingIndex();
        Token wide = Name("longName", 0);
        Token narrow = Name("Name", 4);
        index.Declare(wide, document, BindingNamespace.Value);
        index.Declare(narrow, document, BindingNamespace.Value);
        FrozenBindingIndex frozen = index.Freeze();

        Assert.Same(narrow, Assert.Single(frozen.FindDefinitions(document, 4)).Name);
        Assert.Same(wide, Assert.Single(frozen.FindDefinitions(document, 3)).Name);
        Assert.Empty(frozen.FindDefinitions(document, 8));
        Assert.Empty(frozen.FindSymbols(new SourceDocument(document.Path, document.Text), 4));
    }

    [Fact]
    public void SymbolsAndCollectionsCannotMutateOrCrossSnapshotBoundaries()
    {
        var document = new SourceDocument("identity.ts", "name");
        var index = new BindingIndex();
        index.Declare(Name("name", 0), document, BindingNamespace.Value);
        FrozenBindingIndex first = index.Freeze();
        FrozenBindingIndex second = index.Freeze();
        FrozenBindingSymbol symbol = Assert.Single(first.FindSymbols(document, 0));

        Assert.Empty(second.FindReferences([symbol], includeDeclarations: true));
        var declarations = Assert.IsAssignableFrom<IList<BindingDeclaration>>(symbol.Declarations);
        Assert.Throws<NotSupportedException>(() => declarations.Clear());
        Assert.Single(first.FindDefinitions(document, 0));
    }

    private static Token Name(string text, int start) =>
        new(TokenType.IDENTIFIER, text, null, line: 1, start: start);
}
