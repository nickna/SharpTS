using SharpTS.Compilation.Bundling;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Tests for SDK bundler detection and caching.
/// </summary>
public class SdkBundlerDetectorTests
{
    [Fact]
    public void IsSdkAvailable_ReturnsBool()
    {
        // The result should be deterministic for a given system
        var result1 = SdkBundlerDetector.IsSdkAvailable;
        var result2 = SdkBundlerDetector.IsSdkAvailable;

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void DetectionResult_IsCached()
    {
        // Getting the detection result multiple times should return the same instance
        var result1 = SdkBundlerDetector.DetectionResult;
        var result2 = SdkBundlerDetector.DetectionResult;

        // Same instance means caching is working
        Assert.Same(result1, result2);
    }

    [Fact]
    public void DetectionResult_HasConsistentState()
    {
        var result = SdkBundlerDetector.DetectionResult;

        if (result.IsAvailable)
        {
            // If available, should have valid path and types
            Assert.NotNull(result.HostModelPath);
            Assert.NotNull(result.HostModelAssembly);
            Assert.NotNull(result.BundlerType);
            Assert.True(File.Exists(result.HostModelPath));
        }
        else
        {
            // If not available, BundlerType should be null
            Assert.Null(result.BundlerType);
        }
    }

    [Fact]
    public void IsSdkAvailable_MatchesDetectionResult()
    {
        var isAvailable = SdkBundlerDetector.IsSdkAvailable;
        var detectionResult = SdkBundlerDetector.DetectionResult;

        Assert.Equal(isAvailable, detectionResult.IsAvailable);
    }
}
