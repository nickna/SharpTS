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
/// Every caller uses these lookups as a <em>probe</em> — "does this object happen to
/// expose Invoke/Fields/GetProperty?" — with null handled by a fall-through path that
/// raises the caller's own guest-level diagnostic. Under Native AOT the open structural
/// universe is empty by construction (arbitrary CLR objects can only enter through the
/// .NET interop boundary, which rejects native execution first), so the probes answer
/// <c>null</c> there instead of throwing: a plain-TypeScript program running natively
/// must see its ordinary "not a function"-style error, never a
/// <see cref="PlatformNotSupportedException"/> about CLR reflection.
/// </remarks>
internal static class ManagedStructuralClrReflection
{
    private const string TrimJustification =
        "The Managed SKU intentionally supports open-world structural CLR objects from " +
        "embedders and third-party assemblies. Native AOT never reaches the reflection: " +
        "the probes return null there, and known SharpTS and compiler-emitted shapes " +
        "are handled before this fallback.";

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? TryGetPublicMethodByName(Type type, string name)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;
        return type.GetMethod(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? TryGetPublicMethodBySignature(
        Type type,
        string name,
        Type[] parameterTypes)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;
        return type.GetMethod(name, parameterTypes);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? TryGetPublicInstanceMethodBySignature(
        Type type,
        string name,
        Type[] parameterTypes)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;
        return type.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            parameterTypes,
            modifiers: null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo? TryGetPublicPropertyByName(Type type, string name)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return null;
        return type.GetProperty(name);
    }
}
