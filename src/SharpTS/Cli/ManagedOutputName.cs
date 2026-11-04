using System.Reflection;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Cli;

internal static class ManagedOutputName
{
    internal static void Validate(string assemblyName)
    {
        // Match the assembly identity that ILCompiler emits, including display
        // names and whitespace normalized by AssemblyName.
        string identity = new AssemblyName(assemblyName).Name!;
        if (PEPacker.EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(identity, out _) ||
            IsTrustedFrameworkAssembly(identity) ||
            identity.Equals(typeof(Console).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            identity.Equals(typeof(object).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            identity.Equals(typeof(Uri).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
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
