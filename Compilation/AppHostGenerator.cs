using SharpTS.Compilation.Bundling;

namespace SharpTS.Compilation;

/// <summary>
/// Generates single-file executables by bundling managed assemblies with the .NET apphost.
/// This is a facade over the bundling infrastructure that provides backward compatibility
/// and returns information about which bundling technique was used.
/// </summary>
public static class AppHostGenerator
{
    /// <summary>
    /// Creates a single-file executable from a managed DLL.
    /// Automatically selects the best available bundler (SDK or manual).
    /// </summary>
    /// <param name="managedDllPath">Path to the managed assembly (.dll)</param>
    /// <param name="outputExePath">Path for the output executable (.exe)</param>
    /// <param name="assemblyName">Name of the assembly (without extension)</param>
    /// <returns>Result containing the output path and which bundling technique was used.</returns>
    public static BundleResult CreateSingleFileExecutable(string managedDllPath, string outputExePath, string assemblyName)
    {
        return CreateSingleFileExecutable(managedDllPath, outputExePath, assemblyName, BundlerMode.Auto);
    }

    /// <summary>
    /// Creates a single-file executable from a managed DLL using the specified bundler mode.
    /// </summary>
    /// <param name="managedDllPath">Path to the managed assembly (.dll)</param>
    /// <param name="outputExePath">Path for the output executable (.exe)</param>
    /// <param name="assemblyName">Name of the assembly (without extension)</param>
    /// <param name="mode">Bundler selection mode (auto, sdk, or builtin)</param>
    /// <returns>Result containing the output path and which bundling technique was used.</returns>
    public static BundleResult CreateSingleFileExecutable(string managedDllPath, string outputExePath, string assemblyName, BundlerMode mode)
    {
        var bundler = BundlerFactory.GetBundler(mode);
        return bundler.CreateSingleFileExecutable(managedDllPath, outputExePath, assemblyName);
    }

    /// <summary>
    /// Creates a single-file executable from a managed DLL.
    /// This method is provided for backward compatibility.
    /// </summary>
    /// <param name="managedDllPath">Path to the managed assembly (.dll)</param>
    /// <param name="outputExePath">Path for the output executable (.exe)</param>
    /// <param name="assemblyName">Name of the assembly (without extension)</param>
    [Obsolete("Use CreateSingleFileExecutable which returns BundleResult instead.")]
    public static void CreateSingleFileExecutableDirect(string managedDllPath, string outputExePath, string assemblyName)
    {
        CreateSingleFileExecutable(managedDllPath, outputExePath, assemblyName);
    }

    /// <summary>
    /// Finds the apphost template from the installed .NET SDK.
    /// </summary>
    public static string? FindAppHostTemplate() => FindAppHostTemplateWithVersion().Path;

    /// <summary>
    /// Finds the apphost template and returns both the path and the SDK version.
    /// </summary>
    public static (string? Path, Version? Version) FindAppHostTemplateWithVersion()
    {
        return ManualBundler.FindAppHostTemplateWithVersion();
    }

    /// <summary>
    /// Gets the bundling technique that will be used.
    /// </summary>
    public static BundleTechnique GetPreferredTechnique()
    {
        return BundlerFactory.GetPreferredTechnique();
    }
}
