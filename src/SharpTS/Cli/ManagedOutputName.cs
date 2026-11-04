using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Cli;

internal static class ManagedOutputName
{
    internal static void Validate(string assemblyName)
    {
        if (PEPacker.EmbeddedReferenceAssemblyIndex.Default.TryGetIdentity(assemblyName, out _) ||
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platformAssemblies &&
                platformAssemblies.Split(Path.PathSeparator).Any(path =>
                    assemblyName.Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))) ||
            assemblyName.Equals(typeof(Console).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals(typeof(object).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals(typeof(Uri).Assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
            throw new CompileException(
                $"Managed output assembly name '{assemblyName}' would shadow a framework assembly. Choose a different output file name.");
    }
}
