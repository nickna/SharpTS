using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Cli;

internal static class ManagedOutputName
{
    internal static void Validate(string assemblyName)
    {
        if (PEPacker.EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(assemblyName, out _) ||
            IsTrustedFrameworkAssembly(assemblyName) ||
            assemblyName.Equals(typeof(Console).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals(typeof(object).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals(typeof(Uri).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            throw new CompileException(
                $"Managed output assembly name '{assemblyName}' would shadow a framework assembly. Choose a different output file name.");
    }

    private static bool IsTrustedFrameworkAssembly(string assemblyName)
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string platformAssemblies)
            return false;
        string[] paths = platformAssemblies.Split(Path.PathSeparator);
        string? coreLibrary = paths.FirstOrDefault(path => string.Equals(
            Path.GetFileNameWithoutExtension(path), typeof(object).Assembly.GetName().Name,
            StringComparison.OrdinalIgnoreCase));
        string? frameworkDirectory = Path.GetDirectoryName(coreLibrary);
        if (frameworkDirectory is null) return false;
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // TPA also includes the compiler's application dependencies. Only the
        // shared framework directory can reserve names for a generated process.
        return paths.Any(path =>
            string.Equals(Path.GetDirectoryName(path), frameworkDirectory, pathComparison) &&
            assemblyName.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));
    }
}
