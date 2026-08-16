using SharpTS.Runtime.DotNet;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

public sealed class NativeDotNetCatalogTests
{
    [Fact]
    public void DefaultCatalog_ResolvesCuratedBclAndFriendlyGenericAliases()
    {
        INativeDotNetCatalog catalog = DefaultNativeDotNetCatalog.Instance;

        Assert.True(catalog.TryResolveType("System.Text.StringBuilder", out Type? builder));
        Assert.Equal(typeof(System.Text.StringBuilder), builder);
        Assert.True(catalog.TryResolveType(
            "System.Collections.Generic.List<number>", out Type? list));
        Assert.Equal(typeof(List<double>), list);
        Assert.False(catalog.TryResolveType("System.IO.FileInfo", out _));
    }

    [Fact]
    public void Builder_TracksAliasesClosedGenericsAndArrays()
    {
        INativeDotNetCatalog catalog = new NativeDotNetCatalogBuilder()
            .Add<Version>("SemVer")
            .Add<List<Version>>()
            .Add<Version[]>()
            .Build();

        Assert.True(catalog.TryResolveType("SemVer", out Type? version));
        Assert.Equal(typeof(Version), version);
        Assert.True(catalog.TryGetConstructedGeneric(
            typeof(List<>), [typeof(Version)], out Type? list));
        Assert.Equal(typeof(List<Version>), list);
        Assert.True(catalog.TryGetArrayType(typeof(Version), out Type? array));
        Assert.Equal(typeof(Version[]), array);
    }

    [Fact]
    public void EmptyPayloadCatalog_ExtractsNothing()
    {
        INativeDotNetCatalog catalog = new NativeDotNetCatalogBuilder()
            .Add<string>()
            .Build();

        Assert.Empty(catalog.ExtractAssemblyPayloads(Path.GetTempPath()));
    }
}
