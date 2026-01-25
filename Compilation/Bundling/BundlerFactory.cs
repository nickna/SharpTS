namespace SharpTS.Compilation.Bundling;

/// <summary>
/// Factory for creating the appropriate bundler based on SDK availability.
/// Uses SDK bundler when available, falls back to manual bundler otherwise.
/// </summary>
public static class BundlerFactory
{
    private static readonly Lazy<IBundler> _cachedBundler = new(CreateBundler, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets a bundler instance, preferring the SDK bundler when available.
    /// The bundler instance is cached for reuse.
    /// </summary>
    /// <returns>An IBundler implementation.</returns>
    public static IBundler GetBundler() => _cachedBundler.Value;

    /// <summary>
    /// Creates a new bundler without caching.
    /// Useful for testing or when you need a fresh instance.
    /// </summary>
    /// <returns>An IBundler implementation.</returns>
    public static IBundler CreateBundler()
    {
        if (SdkBundlerDetector.IsSdkAvailable)
        {
            try
            {
                // Return a bundler that tries SDK first, falls back to manual on failure
                return new FallbackBundler(new SdkBundler(), new ManualBundler());
            }
            catch
            {
                // If SDK bundler creation fails, fall back to manual
                return new ManualBundler();
            }
        }

        return new ManualBundler();
    }

    /// <summary>
    /// Gets a specific bundler type, ignoring the automatic selection.
    /// </summary>
    /// <param name="technique">The desired bundling technique.</param>
    /// <returns>An IBundler for the specified technique.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the requested technique is not available.</exception>
    public static IBundler GetBundler(BundleTechnique technique)
    {
        return technique switch
        {
            BundleTechnique.SdkBundler => new SdkBundler(),
            BundleTechnique.ManualBundler => new ManualBundler(),
            _ => throw new ArgumentOutOfRangeException(nameof(technique))
        };
    }

    /// <summary>
    /// Gets a bundler based on the specified mode.
    /// </summary>
    /// <param name="mode">The bundler selection mode.</param>
    /// <returns>An IBundler for the specified mode.</returns>
    /// <exception cref="InvalidOperationException">Thrown if SDK bundler is requested but not available.</exception>
    public static IBundler GetBundler(BundlerMode mode)
    {
        return mode switch
        {
            BundlerMode.Sdk => SdkBundlerDetector.IsSdkAvailable
                ? new SdkBundler()
                : throw new InvalidOperationException(
                    "SDK bundler is not available on this system.\n" +
                    "Ensure the .NET SDK is installed, or use '--bundler builtin' for the built-in bundler."),
            BundlerMode.BuiltIn => new ManualBundler(),
            BundlerMode.Auto => CreateBundler(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Gets information about which bundler would be selected.
    /// </summary>
    /// <returns>The technique that would be used by GetBundler().</returns>
    public static BundleTechnique GetPreferredTechnique()
    {
        return SdkBundlerDetector.IsSdkAvailable
            ? BundleTechnique.SdkBundler
            : BundleTechnique.ManualBundler;
    }
}
