using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Reflection boundary for Managed-SKU compatibility with arbitrary CLR objects
/// that expose a SharpTS-recognised structural member.
/// </summary>
/// <remarks>
/// These shapes are intentionally open ended so embedders and third-party assemblies
/// can provide callable and object-like values without implementing SharpTS interfaces.
/// Native AOT has a frozen type universe and cannot promise that arbitrary members were
/// retained, so the boundary rejects native execution before inspecting the type.
/// </remarks>
internal static class ManagedStructuralClrReflection
{
    private const string TrimJustification =
        "The Managed SKU intentionally supports open-world structural CLR objects from " +
        "embedders and third-party assemblies. Native AOT is rejected before reflection; " +
        "known SharpTS and compiler-emitted shapes are handled before this fallback.";

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetPublicMethodByName(Type type, string name)
    {
        RequireManagedRuntime();
        return type.GetMethod(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetPublicMethodBySignature(
        Type type,
        string name,
        Type[] parameterTypes)
    {
        RequireManagedRuntime();
        return type.GetMethod(name, parameterTypes);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetPublicInstanceMethodBySignature(
        Type type,
        string name,
        Type[] parameterTypes)
    {
        RequireManagedRuntime();
        return type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            parameterTypes,
            modifiers: null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo? GetPublicPropertyByName(Type type, string name)
    {
        RequireManagedRuntime();
        return type.GetProperty(name);
    }

    private static void RequireManagedRuntime()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                "Structural compatibility with arbitrary CLR objects is not available " +
                "in the native SharpTS build — use a SharpTS-owned value or the managed build.");
        }
    }
}
