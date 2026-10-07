using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public sealed class MemberAnalysisTests
{
    [Fact]
    public async Task SharedSnapshotsPublishMemberOriginsWithoutChangingLexicalFacets()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json",
            """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts"]}""");
        const string source = "class Box { value: number = 1; } const box = new Box(); box.value;";
        string path = directory.CreateFile("box.ts", source);
        using var service = new SemanticAnalysisService();
        using var first = await service.GetDocumentAsync(path, source);
        using var repeated = await service.GetDocumentAsync(path, source);
        Assert.NotNull(first);
        Assert.NotNull(repeated);
        var original = first.Model;
        int offset = source.LastIndexOf("value", StringComparison.Ordinal);

        Assert.Same(original.Snapshot, repeated.Model.Snapshot);
        Assert.Same(original.Members, repeated.Model.Members);
        FrozenMemberResolution resolution = original.Members.FindResolution(original.Document, offset);
        Assert.True(resolution.IsResolved);
        FrozenSourceMemberSymbol symbol = Assert.Single(resolution.Candidates);
        Assert.Equal("value", symbol.Name);
        Assert.False(symbol.CanRename);
        Assert.Equal(source.IndexOf("value", StringComparison.Ordinal), Assert.Single(symbol.Declarations).Name.Start);
        Assert.Empty(original.Bindings.FindSymbols(original.Document, offset));

        const string prefix = "// edited capture\n";
        using var edited = await service.GetDocumentAsync(path, prefix + source);
        Assert.NotNull(edited);
        var changed = edited.Model;
        FrozenSourceMemberSymbol changedSymbol = Assert.Single(changed.Members
            .FindResolution(changed.Document, prefix.Length + offset).Candidates);

        Assert.NotSame(original.Members, changed.Members);
        Assert.NotSame(symbol, changedSymbol);
        Assert.Same(symbol, Assert.Single(original.Members.FindResolution(original.Document, offset).Candidates));
        Assert.Empty(changed.Members.FindResolution(original.Document, offset).Candidates);
        Assert.Equal(2, service.Statistics.Checks);
    }

    [Fact]
    public async Task ParameterPropertyDeclarationRetainsSeparateLexicalAndPropertyIdentities()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("tsconfig.json",
            """{"compilerOptions":{"noLib":true,"types":[]},"include":["*.ts"]}""");
        const string source = "class Box { constructor(public value: number) { value; } }";
        string path = directory.CreateFile("box.ts", source);
        using var service = new SemanticAnalysisService();
        using var analysis = await service.GetDocumentAsync(path, source);
        Assert.NotNull(analysis);
        var model = analysis.Model;
        int offset = source.IndexOf("value", StringComparison.Ordinal);

        FrozenBindingSymbol lexical = Assert.Single(model.Bindings.FindSymbols(model.Document, offset));
        FrozenMemberResolution property = model.Members.FindResolution(model.Document, offset);

        Assert.True(property.IsResolved);
        Assert.Equal(SourceMemberKind.ParameterProperty, Assert.Single(property.Candidates).Kind);
        Assert.True(model.Members.TryFindOccurrence(path, offset, out var declaration));
        Assert.Equal(MemberOperation.Declaration, declaration!.Operations);
        Assert.Equal(BindingRenameEligibility.ParameterPropertyRequiresCoordinatedEdits, lexical.RenameEligibility);
        Assert.Equal(2, model.Bindings.FindReferences([lexical], includeDeclarations: true).Count);
        Assert.False(Assert.Single(property.Candidates).CanRename);
    }
}
