using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpTS.Compilation.Bundling;

/// <summary>
/// Creates single-file executables using the official .NET SDK Bundler class
/// from Microsoft.NET.HostModel.dll via reflection.
/// </summary>
public class SdkBundler : IBundler
{
    private readonly Type _bundlerType;
    private readonly Assembly _hostModelAssembly;

    /// <summary>
    /// Creates a new SdkBundler using the detected SDK.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if SDK is not available.</exception>
    public SdkBundler()
    {
        var detection = SdkBundlerDetector.DetectionResult;
        if (!detection.IsAvailable || detection.BundlerType == null || detection.HostModelAssembly == null)
        {
            throw new InvalidOperationException(
                "SDK Bundler is not available. Use BundlerFactory to get the appropriate bundler.");
        }

        _bundlerType = detection.BundlerType;
        _hostModelAssembly = detection.HostModelAssembly;
    }

    /// <inheritdoc/>
    public BundleTechnique Technique => BundleTechnique.SdkBundler;

    /// <inheritdoc/>
    public BundleResult CreateSingleFileExecutable(string dllPath, string exePath, string assemblyName)
    {
        // Find the apphost template
        var (apphostPath, sdkVersion) = ManualBundler.FindAppHostTemplateWithVersion();
        if (apphostPath == null || sdkVersion == null)
        {
            throw new InvalidOperationException(
                "Could not find apphost template. Ensure the .NET SDK is installed.");
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(exePath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Create a temporary working directory for bundling
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"sharpts_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        try
        {
            // Copy the DLL to the bundle directory
            var bundleDllPath = Path.Combine(tempBundleDir, $"{assemblyName}.dll");
            File.Copy(dllPath, bundleDllPath);

            // Generate runtimeconfig.json
            var runtimeConfigContent = GenerateRuntimeConfigJson(sdkVersion);
            var runtimeConfigPath = Path.Combine(tempBundleDir, $"{assemblyName}.runtimeconfig.json");
            File.WriteAllText(runtimeConfigPath, runtimeConfigContent);

            // Use the SDK Bundler via reflection
            InvokeSdkBundler(apphostPath, exePath, assemblyName, tempBundleDir, sdkVersion);

            return new BundleResult(exePath, BundleTechnique.SdkBundler);
        }
        finally
        {
            // Clean up temp directory
            try
            {
                Directory.Delete(tempBundleDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Invokes the SDK Bundler via reflection.
    /// </summary>
    private void InvokeSdkBundler(string apphostPath, string outputPath, string assemblyName, string sourceDir, Version sdkVersion)
    {
        // First, patch the apphost template with the DLL name using HostWriter
        var patchedApphostPath = Path.Combine(sourceDir, $"{assemblyName}.exe");
        PatchAppHost(apphostPath, patchedApphostPath, $"{assemblyName}.dll");

        // Get the BundleOptions enum type
        var bundleOptionsType = _hostModelAssembly.GetType("Microsoft.NET.HostModel.Bundle.BundleOptions");
        if (bundleOptionsType == null)
        {
            throw new InvalidOperationException("Could not find BundleOptions type in SDK.");
        }

        // BundleOptions.None = 0
        var bundleOptionsNone = Enum.ToObject(bundleOptionsType, 0);

        // Get the OSPlatform for current platform
        var targetOS = GetTargetOSPlatform();

        // Get the Architecture for current platform
        var targetArch = RuntimeInformation.OSArchitecture;

        // Calculate TFM for .NET version
        var targetFrameworkVersion = new Version(sdkVersion.Major, sdkVersion.Minor);

        // Prepare output directory (must be different from source)
        var tempOutputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(tempOutputDir))
        {
            tempOutputDir = Directory.GetCurrentDirectory();
        }

        // Try to find and invoke a compatible constructor
        var constructors = _bundlerType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Exception? lastException = null;

        foreach (var constructor in constructors.OrderByDescending(c => c.GetParameters().Length))
        {
            var parameters = constructor.GetParameters();

            try
            {
                var args = BuildConstructorArgs(parameters, assemblyName, tempOutputDir, bundleOptionsNone,
                    targetOS, targetArch, targetFrameworkVersion, apphostPath);

                if (args != null)
                {
                    var bundler = constructor.Invoke(args);

                    // Get FileSpec type and create file specs
                    var fileSpecType = _hostModelAssembly.GetType("Microsoft.NET.HostModel.Bundle.FileSpec");
                    if (fileSpecType == null)
                    {
                        throw new InvalidOperationException("Could not find FileSpec type in SDK.");
                    }

                    var fileSpecs = CreateFileSpecList(fileSpecType, sourceDir, assemblyName, patchedApphostPath);

                    // Find and invoke GenerateBundle method
                    var generateBundleMethod = _bundlerType.GetMethod("GenerateBundle");
                    if (generateBundleMethod == null)
                    {
                        throw new InvalidOperationException("Could not find GenerateBundle method in SDK Bundler.");
                    }

                    generateBundleMethod.Invoke(bundler, [fileSpecs]);

                    // The SDK bundler creates the file in outputDir with name from hostName parameter
                    // hostName we passed was "{assemblyName}.exe"
                    var expectedExeName = $"{assemblyName}.exe";
                    var actualOutputPath = Path.Combine(tempOutputDir, expectedExeName);

                    // Move/copy to final output path if different
                    if (File.Exists(actualOutputPath))
                    {
                        // Normalize both paths to compare properly (handles relative vs absolute)
                        var normalizedActual = Path.GetFullPath(actualOutputPath);
                        var normalizedOutput = Path.GetFullPath(outputPath);

                        if (!string.Equals(normalizedActual, normalizedOutput, StringComparison.OrdinalIgnoreCase))
                        {
                            // Actually different paths - move the file
                            if (File.Exists(outputPath))
                            {
                                File.Delete(outputPath);
                            }
                            File.Move(actualOutputPath, outputPath);
                        }
                        // If same path, file is already in the right place
                        return; // Success!
                    }

                    // Also check for non-Windows name (no .exe extension)
                    var altOutputPath = Path.Combine(tempOutputDir, assemblyName);
                    if (File.Exists(altOutputPath))
                    {
                        var normalizedAlt = Path.GetFullPath(altOutputPath);
                        var normalizedOutput = Path.GetFullPath(outputPath);

                        if (!string.Equals(normalizedAlt, normalizedOutput, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(outputPath))
                            {
                                File.Delete(outputPath);
                            }
                            File.Move(altOutputPath, outputPath);
                        }
                        return; // Success!
                    }

                    throw new InvalidOperationException($"SDK Bundler did not create expected output file at {actualOutputPath}");
                }
            }
            catch (Exception ex)
            {
                // Unwrap TargetInvocationException to get the real error
                lastException = ex is System.Reflection.TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                // Continue trying other constructors
            }
        }

        throw new InvalidOperationException(
            $"Could not find compatible Bundler constructor in SDK. Last error: {lastException?.Message}",
            lastException);
    }

    /// <summary>
    /// Builds constructor arguments based on parameter types.
    /// </summary>
    private object?[]? BuildConstructorArgs(ParameterInfo[] parameters, string assemblyName, string outputDir,
        object bundleOptions, OSPlatform targetOS, Architecture targetArch, Version targetFrameworkVersion, string apphostPath)
    {
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramType = param.ParameterType;
            var paramName = param.Name?.ToLowerInvariant() ?? "";

            // Match by parameter type and name
            if (paramType == typeof(string))
            {
                if (paramName == "hostname" || i == 0)
                {
                    // hostName: the name of the single-file bundle
                    args[i] = $"{assemblyName}.exe";
                }
                else if (paramName == "outputdir" || i == 1)
                {
                    args[i] = outputDir;
                }
                else if (paramName == "appassemblyname")
                {
                    // appAssemblyName: the managed assembly name WITHOUT extension
                    // The bundler uses this to compute file names like "{appAssemblyName}.runtimeconfig.json"
                    args[i] = assemblyName;
                }
                else if (paramName.Contains("apphost") || paramName.Contains("source"))
                {
                    args[i] = apphostPath;
                }
                else
                {
                    // Unknown string parameter - try null if nullable
                    args[i] = null;
                }
            }
            else if (paramType == bundleOptions.GetType())
            {
                args[i] = bundleOptions;
            }
            else if (paramType == typeof(OSPlatform) || Nullable.GetUnderlyingType(paramType) == typeof(OSPlatform))
            {
                args[i] = targetOS;
            }
            else if (paramType == typeof(Architecture) || Nullable.GetUnderlyingType(paramType) == typeof(Architecture))
            {
                args[i] = targetArch;
            }
            else if (paramType == typeof(Version))
            {
                args[i] = targetFrameworkVersion;
            }
            else if (paramType == typeof(bool))
            {
                // diagnosticOutput or macosCodesign - both false
                args[i] = false;
            }
            else if (Nullable.GetUnderlyingType(paramType) != null)
            {
                args[i] = null; // Nullable parameter, use null
            }
            else
            {
                return null; // Unknown parameter type we can't satisfy
            }
        }

        return args;
    }

    /// <summary>
    /// Creates a List of FileSpec objects for the bundler.
    /// </summary>
    private object CreateFileSpecList(Type fileSpecType, string sourceDir, string assemblyName, string apphostPath)
    {
        // Find the FileSpec constructor
        var fileSpecCtor = fileSpecType.GetConstructor([
            typeof(string), // sourcePath
            typeof(string)  // bundleRelativePath
        ]);

        if (fileSpecCtor == null)
        {
            throw new InvalidOperationException("Could not find FileSpec constructor in SDK.");
        }

        // Create a List<FileSpec>
        var listType = typeof(List<>).MakeGenericType(fileSpecType);
        var list = Activator.CreateInstance(listType)!;
        var addMethod = listType.GetMethod("Add")!;

        // Add the apphost template (this is the host binary that bundler looks for)
        // The BundleRelativePath must match the hostName constructor parameter
        var hostName = $"{assemblyName}.exe";
        var hostSpec = fileSpecCtor.Invoke([apphostPath, hostName]);
        addMethod.Invoke(list, [hostSpec]);

        // Add the DLL
        var dllPath = Path.Combine(sourceDir, $"{assemblyName}.dll");
        var dllSpec = fileSpecCtor.Invoke([dllPath, $"{assemblyName}.dll"]);
        addMethod.Invoke(list, [dllSpec]);

        // Add the runtimeconfig.json
        var configPath = Path.Combine(sourceDir, $"{assemblyName}.runtimeconfig.json");
        var configSpec = fileSpecCtor.Invoke([configPath, $"{assemblyName}.runtimeconfig.json"]);
        addMethod.Invoke(list, [configSpec]);

        return list;
    }

    /// <summary>
    /// Gets the current OS platform.
    /// </summary>
    private static OSPlatform GetTargetOSPlatform()
    {
        if (OperatingSystem.IsWindows()) return OSPlatform.Windows;
        if (OperatingSystem.IsLinux()) return OSPlatform.Linux;
        if (OperatingSystem.IsMacOS()) return OSPlatform.OSX;
        return OSPlatform.Windows; // Default fallback
    }

    /// <summary>
    /// Patches the apphost template with the DLL name using HostWriter.
    /// </summary>
    private void PatchAppHost(string apphostSourcePath, string apphostDestPath, string appBinaryName)
    {
        // Get the HostWriter type
        var hostWriterType = _hostModelAssembly.GetType("Microsoft.NET.HostModel.AppHost.HostWriter");
        if (hostWriterType == null)
        {
            throw new InvalidOperationException("Could not find HostWriter type in SDK.");
        }

        // Find the CreateAppHost method
        // Looking for: CreateAppHost(string appHostSourceFilePath, string appHostDestinationFilePath, string appBinaryFilePath, ...)
        var createAppHostMethod = hostWriterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "CreateAppHost" && m.GetParameters().Length >= 3);

        if (createAppHostMethod == null)
        {
            throw new InvalidOperationException("Could not find CreateAppHost method in HostWriter.");
        }

        var parameters = createAppHostMethod.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramName = param.Name?.ToLowerInvariant() ?? "";
            var paramType = param.ParameterType;

            if (paramName.Contains("source") || i == 0)
            {
                args[i] = apphostSourcePath;
            }
            else if (paramName.Contains("destination") || i == 1)
            {
                args[i] = apphostDestPath;
            }
            else if (paramName.Contains("binary") || paramName.Contains("app") && paramType == typeof(string))
            {
                args[i] = appBinaryName;
            }
            else if (paramType == typeof(bool))
            {
                args[i] = false;
            }
            else if (paramType == typeof(string))
            {
                args[i] = null;
            }
            else if (Nullable.GetUnderlyingType(paramType) != null)
            {
                args[i] = null;
            }
            else if (param.HasDefaultValue)
            {
                args[i] = param.DefaultValue;
            }
            else
            {
                args[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
            }
        }

        createAppHostMethod.Invoke(null, args);
    }

    private static string GenerateRuntimeConfigJson(Version sdkVersion)
    {
        return $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{sdkVersion.Major}}.{{sdkVersion.Minor}}",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "{{sdkVersion.Major}}.{{sdkVersion.Minor}}.{{sdkVersion.Build}}"
                }
              }
            }
            """;
    }
}
