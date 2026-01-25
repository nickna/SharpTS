namespace SharpTS.Compilation.Bundling;

/// <summary>
/// A bundler that tries a primary bundler first, then falls back to a secondary bundler if the primary fails.
/// </summary>
public class FallbackBundler : IBundler
{
    private readonly IBundler _primary;
    private readonly IBundler _fallback;
    private BundleTechnique? _lastUsedTechnique;

    /// <summary>
    /// Creates a new fallback bundler.
    /// </summary>
    /// <param name="primary">The primary bundler to try first.</param>
    /// <param name="fallback">The fallback bundler to use if primary fails.</param>
    public FallbackBundler(IBundler primary, IBundler fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    /// <inheritdoc/>
    public BundleTechnique Technique => _lastUsedTechnique ?? _primary.Technique;

    /// <inheritdoc/>
    public BundleResult CreateSingleFileExecutable(string dllPath, string exePath, string assemblyName)
    {
        try
        {
            var result = _primary.CreateSingleFileExecutable(dllPath, exePath, assemblyName);
            _lastUsedTechnique = result.Technique;
            return result;
        }
        catch
        {
            // Primary failed, try fallback
            var result = _fallback.CreateSingleFileExecutable(dllPath, exePath, assemblyName);
            _lastUsedTechnique = result.Technique;
            return result;
        }
    }
}
