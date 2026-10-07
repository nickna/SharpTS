using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class MemberIndexTests
{
    [Fact]
    public void ClassFacetAndPrivateDomainsHaveIndependentCanonicalIdentities()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("classes.ts", "value value value value value");
        Register(index, document, 1, MemberFacet.Instance, Name(0));
        Register(index, document, 2, MemberFacet.Instance, Name(6));
        Register(index, document, 1, MemberFacet.Static, Name(12));
        Register(index, document, 1, MemberFacet.PrivateInstance, Name(18));
        Register(index, document, 1, MemberFacet.PrivateStatic, Name(24));

        var frozen = index.Freeze();

        Assert.Equal(5, frozen.Symbols.Count);
        Assert.Equal(5, frozen.Symbols.Select(symbol => symbol.Id).Distinct().Count());
        Assert.All(frozen.Symbols, symbol => Assert.False(symbol.CanRename));
        Assert.True(index.ResolveSelected(1, MemberFacet.Instance, "value").IsResolved);
        Assert.False(index.ResolveSelected(3, MemberFacet.Instance, "value").IsComplete);
    }

    [Fact]
    public void LegalGroupDeclarationsMergeWithoutCollapsingTheirSourceLocations()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("accessors.ts", "value value");
        var first = Name(0);
        var second = Name(6);
        index.RegisterSourceClass(1, document, new object(),
        [new("value", MemberFacet.Instance, SourceMemberKind.Accessor,
            [new(document, first, SourceMemberKind.Accessor), new(document, second, SourceMemberKind.Accessor)])]);
        var frozen = index.Freeze();
        var symbol = Assert.Single(frozen.Symbols);

        Assert.Equal([first, second], symbol.Declarations.Select(declaration => declaration.Name));
        Assert.Equal(2, frozen.GetOccurrences(symbol).Count);
        Assert.Equal(2, frozen.FindDefinitions(document, first.Start).Count);
    }

    [Fact]
    public void RequiredSelectionsKeepFiniteAmbiguityAndUnsupportedBranchesSeparate()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("union.ts", "value value");
        Register(index, document, 1, MemberFacet.Instance, Name(0));
        Register(index, document, 2, MemberFacet.Instance, Name(6));
        var first = index.ResolveSelected(1, MemberFacet.Instance, "value");
        var second = index.ResolveSelected(2, MemberFacet.Instance, "value");

        Assert.True(MemberResolution.CombineRequired([first, first]).IsResolved);
        var ambiguous = MemberResolution.CombineRequired([first, second]);
        Assert.True(ambiguous.IsAmbiguous);
        Assert.Equal(2, ambiguous.Candidates.Count);
        var partial = MemberResolution.CombineRequired([first, MemberResolution.Unavailable]);
        Assert.False(partial.IsComplete);
        Assert.False(partial.IsResolved);
        Assert.Single(partial.Candidates);
        Assert.False(MemberResolution.CombineRequired([]).IsComplete);
        var incompleteAmbiguity = MemberResolution.CombineRequired([first, second, MemberResolution.Unavailable]);
        Assert.True(incompleteAmbiguity.IsAmbiguous);
        Assert.False(incompleteAmbiguity.IsComplete);
    }

    [Fact]
    public void SameTokenDistinctOwnersCombineAndOperationsRemainIndependent()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("uses.ts", "value value value");
        Register(index, document, 1, MemberFacet.Instance, Name(0));
        Register(index, document, 2, MemberFacet.Instance, Name(6));
        var use = Name(12);
        var first = index.ResolveSelected(1, MemberFacet.Instance, "value");
        var second = index.ResolveSelected(2, MemberFacet.Instance, "value");
        index.Bind(document, use, first, MemberOperation.Read, owner: new object());
        index.Bind(document, use, second, MemberOperation.Read, owner: new object());
        index.Bind(document, use, first, MemberOperation.Write, owner: new object());

        var occurrence = Assert.Single(index.Freeze().Occurrences, occurrence => ReferenceEquals(occurrence.Name, use));

        Assert.True(occurrence.Resolution.IsAmbiguous);
        Assert.True(occurrence.Selections[MemberOperation.Read].IsAmbiguous);
        Assert.True(occurrence.Selections[MemberOperation.Write].IsResolved);
        Assert.Equal(MemberOperation.Read | MemberOperation.Write, occurrence.Operations);
        Assert.Empty(index.Freeze().FindDefinitions(document, use.Start));
    }

    [Fact]
    public void AuthoritativeRecheckReplacesOnlyTheSameOwnerAndOperation()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("passes.ts", "value value");
        Register(index, document, 1, MemberFacet.Instance, Name(0));
        var selected = index.ResolveSelected(1, MemberFacet.Instance, "value");
        var use = Name(6);
        var owner = new object();
        index.Bind(document, use, MemberResolution.Unavailable, MemberOperation.Read, owner: owner);
        index.Bind(document, use, selected, MemberOperation.Read, owner: owner);
        Assert.True(index.GetResolution(document, use).IsResolved);

        index.Bind(document, use, MemberResolution.Unavailable, MemberOperation.Read, owner: new object());
        Assert.False(index.GetResolution(document, use).IsComplete);
        Assert.Single(index.GetResolution(document, use).Candidates);
    }

    [Fact]
    public void NominalAliasesRequireTheExactDocumentAndAstOwner()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("aliases.ts", "value");
        var owner = new Expr.Variable(Name(0));
        var equalOwner = owner with { };
        Assert.Equal(owner, equalOwner);
        var token = Name(0);
        SourceMemberGroup[] group = [new("value", MemberFacet.Instance, SourceMemberKind.Field,
            [new(document, token, SourceMemberKind.Field, owner)])];
        index.RegisterSourceClass(-1, document, owner, group);
        index.RegisterSourceClass(-3, document, owner, group, containsNestedClass: true);
        var canonical = Assert.Single(index.Symbols);
        Assert.Equal(-1, canonical.DeclaringClassId);
        Assert.Single(canonical.Declarations);
        Assert.Same(canonical, Assert.Single(index.ResolveSelected(-3, MemberFacet.Instance, "value").Candidates));
        Assert.True(index.IsSameSourceClass(-1, -3));
        Assert.False(index.IsSameSourceClass(99, 99));

        index.RegisterSourceClass(2, document, equalOwner,
            [new("value", MemberFacet.Instance, SourceMemberKind.Field,
                [new(document, Name(0), SourceMemberKind.Field, equalOwner)])]);
        var otherDocument = new SourceDocument(document.Path, document.Text);
        index.RegisterSourceClass(3, otherDocument, owner,
            [new("value", MemberFacet.Instance, SourceMemberKind.Field,
                [new(otherDocument, Name(0), SourceMemberKind.Field, owner)])]);
        Assert.False(index.IsSameSourceClass(-1, 2));
        Assert.False(index.IsSameSourceClass(-1, 3));
        // Reusing an already registered nominal ID cannot relabel it as another source owner.
        index.RegisterSourceClass(-1, document, equalOwner, group);
        Assert.Equal(3, index.Symbols.Count);

        var frozen = index.Freeze();
        Assert.Equal(3, frozen.Classes.Count);
        Assert.Same(frozen.GetClassInfo(-1), frozen.GetClassInfo(-3));
        Assert.Same(owner, frozen.GetClassInfo(-3)!.Owner);
        Assert.True(frozen.GetClassInfo(-3)!.ContainsNestedClass);
        Assert.True(frozen.IsSameSourceClass(-1, -3));
        Assert.False(frozen.IsSameSourceClass(-1, 2));
        index.Clear();
        Assert.False(index.IsSameSourceClass(-1, -3));
        Assert.True(frozen.IsSameSourceClass(-1, -3));
    }

    [Fact]
    public void RemovingAStaleCallPreservesReadAndOtherOwners()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("call-passes.ts", "value value");
        Register(index, document, 1, MemberFacet.Instance, Name(0));
        var selected = index.ResolveSelected(1, MemberFacet.Instance, "value");
        var use = Name(6);
        var owner = new object();
        var otherOwner = new object();
        index.Bind(document, use, selected, MemberOperation.Read | MemberOperation.Call, owner: owner);
        index.Bind(document, use, selected, MemberOperation.Call, owner: otherOwner);

        index.RemoveOperation(document, use, MemberOperation.Call, owner);
        Assert.True(index.GetResolution(document, use, MemberOperation.Call).IsResolved);
        index.RemoveOperation(document, use, MemberOperation.Call, otherOwner);

        var occurrence = Assert.Single(index.Freeze().Occurrences, occurrence => ReferenceEquals(occurrence.Name, use));
        Assert.Equal(MemberOperation.Read, occurrence.Operations);
        Assert.True(occurrence.Resolution.IsResolved);
        Assert.False(index.GetResolution(document, use, MemberOperation.Call).IsComplete);
    }

    [Fact]
    public void PublicationSurvivesFurtherRegistrationBindingAndGenerationClear()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("frozen-members.ts", "value value value");
        var declaration = Name(0);
        var use = Name(6);
        Register(index, document, 1, MemberFacet.Instance, declaration);
        index.Bind(document, use, index.ResolveSelected(1, MemberFacet.Instance, "value"), MemberOperation.Read);
        var frozen = index.Freeze();
        var symbol = Assert.Single(frozen.Symbols);
        var oldResolution = index.ResolveSelected(1, MemberFacet.Instance, "value");

        Register(index, document, 1, MemberFacet.Instance, Name(12));
        index.Clear();
        Register(index, document, 1, MemberFacet.Instance, declaration);
        index.Bind(document, use, oldResolution, MemberOperation.Read);

        Assert.Single(symbol.Declarations);
        Assert.Equal(2, frozen.GetOccurrences(symbol).Count);
        Assert.True(frozen.GetResolution(use).IsResolved);
        Assert.False(index.GetResolution(use).IsComplete);
        var fresh = index.Freeze();
        Assert.NotEqual(frozen.Generation, fresh.Generation);
        Assert.NotEqual(symbol.Generation, Assert.Single(fresh.Symbols).Generation);
        Assert.Empty(fresh.GetOccurrences(symbol));
    }

    [Fact]
    public void NestingFactsAndParameterPropertyDeclarationsAreExplicit()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("nested.ts", "value");
        index.RegisterSourceClass(1, document, new object(),
            [new("value", MemberFacet.Instance, SourceMemberKind.ParameterProperty,
                [new(document, Name(0), SourceMemberKind.ParameterProperty)])],
            isNestedPrivateEnvironment: true, containsNestedClass: true);
        var frozen = index.Freeze();

        Assert.True(frozen.GetClassInfo(1)!.IsNestedPrivateEnvironment);
        Assert.True(frozen.GetClassInfo(1)!.ContainsNestedClass);
        Assert.Equal(SourceMemberKind.ParameterProperty, Assert.Single(frozen.Symbols).Kind);
        Assert.False(Assert.Single(frozen.Symbols).CanRename);
        Assert.True(frozen.EstimatedBytes > 0);
    }

    [Fact]
    public void HiddenNamesAndForeignDocumentsCannotFabricateOccurrences()
    {
        var index = new MemberIndex();
        var document = new SourceDocument("real.ts", "value");
        Register(index, document, 1, MemberFacet.Instance, new(TokenType.IDENTIFIER, "value", null, 1));
        Assert.Empty(index.Freeze().Symbols);
        Register(index, document, 1, MemberFacet.Instance, Name(0));
        var frozen = index.Freeze();

        Assert.False(frozen.FindResolution(new SourceDocument("other.ts", document.Text), 1).IsComplete);
        Assert.False(frozen.TryFindOccurrence("other.ts", 1, out _));
        Assert.False(frozen.FindResolution(document, document.Text.Length).IsComplete);
    }

    [Fact]
    public void DefaultCheckerPublishesSharedEmptyIndexAndOptInCanBeDisabled()
    {
        var checker = new TypeChecker();
        Assert.False(checker.Members.IsEnabled);
        Assert.Same(FrozenMemberIndex.Empty, checker.Members.Freeze());
        checker.WithMemberProvenance();
        Assert.True(checker.Members.IsEnabled);
        checker.WithMemberProvenance(false);
        Assert.False(checker.Members.IsEnabled);
        Assert.Same(FrozenMemberIndex.Empty, checker.Members.Freeze());
    }

    private static Token Name(int start) => new(TokenType.IDENTIFIER, "value", null, 1, start);
    private static void Register(MemberIndex index, SourceDocument document, int classId, MemberFacet facet, Token declaration)
    {
        object owner = index.Freeze().GetClassInfo(classId)?.Owner ?? new object();
        index.RegisterSourceClass(classId, document, owner,
            [new("value", facet, SourceMemberKind.Field, [new(document, declaration, SourceMemberKind.Field, owner)])]);
    }
}
