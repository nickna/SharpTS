using SharpTS.Compilation.Bundling;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Tests for bundler factory selection logic.
/// </summary>
public class BundlerFactoryTests
{
    [Fact]
    public void GetBundler_ReturnsIBundler()
    {
        var bundler = BundlerFactory.GetBundler();

        Assert.NotNull(bundler);
        Assert.IsAssignableFrom<IBundler>(bundler);
    }

    [Fact]
    public void GetBundler_ReturnsCachedInstance()
    {
        var bundler1 = BundlerFactory.GetBundler();
        var bundler2 = BundlerFactory.GetBundler();

        // Should return the same cached instance
        Assert.Same(bundler1, bundler2);
    }

    [Fact]
    public void CreateBundler_ReturnsNewInstance()
    {
        var bundler1 = BundlerFactory.CreateBundler();
        var bundler2 = BundlerFactory.CreateBundler();

        // CreateBundler should return new instances each time
        Assert.NotSame(bundler1, bundler2);
    }

    [Fact]
    public void GetPreferredTechnique_MatchesBundlerTechnique()
    {
        var preferredTechnique = BundlerFactory.GetPreferredTechnique();
        var bundler = BundlerFactory.GetBundler();

        Assert.Equal(preferredTechnique, bundler.Technique);
    }

    [Fact]
    public void GetBundler_ByTechnique_ReturnsCorrectType()
    {
        var manualBundler = BundlerFactory.GetBundler(BundleTechnique.ManualBundler);
        Assert.IsType<ManualBundler>(manualBundler);
        Assert.Equal(BundleTechnique.ManualBundler, manualBundler.Technique);
    }

    [Fact]
    public void GetBundler_ByTechnique_SdkBundler_RequiresSdk()
    {
        if (SdkBundlerDetector.IsSdkAvailable)
        {
            var sdkBundler = BundlerFactory.GetBundler(BundleTechnique.SdkBundler);
            Assert.IsType<SdkBundler>(sdkBundler);
            Assert.Equal(BundleTechnique.SdkBundler, sdkBundler.Technique);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() =>
                BundlerFactory.GetBundler(BundleTechnique.SdkBundler));
        }
    }

    [Fact]
    public void GetPreferredTechnique_ReturnsValidTechnique()
    {
        var technique = BundlerFactory.GetPreferredTechnique();

        Assert.True(
            technique == BundleTechnique.SdkBundler ||
            technique == BundleTechnique.ManualBundler);
    }

    [Fact]
    public void BundlerTechnique_Description_IsReadable()
    {
        var bundler = BundlerFactory.GetBundler();
        var result = new BundleResult("test.exe", bundler.Technique);

        Assert.NotNull(result.TechniqueDescription);
        Assert.NotEmpty(result.TechniqueDescription);
        Assert.Contains("bundler", result.TechniqueDescription);
    }

    [Fact]
    public void SdkBundler_TechniqueDescription_IsSdkBundler()
    {
        var result = new BundleResult("test.exe", BundleTechnique.SdkBundler);
        Assert.Equal("SDK bundler", result.TechniqueDescription);
    }

    [Fact]
    public void ManualBundler_TechniqueDescription_IsBuiltIn()
    {
        var result = new BundleResult("test.exe", BundleTechnique.ManualBundler);
        Assert.Equal("built-in bundler", result.TechniqueDescription);
    }
}
