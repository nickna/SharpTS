using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Reflection boundary for runtime operations performed by compiled SharpTS output.
/// </summary>
/// <remarks>
/// Unlike <c>ManagedEmittedShapeReflection</c>, this boundary is intentionally open
/// ended: managed output can contain user-defined types and can interoperate with
/// third-party assemblies. Those operations execute under CoreCLR. A Native AOT
/// SharpTS host can produce the managed output, but it cannot load that output back
/// into its frozen process, so every entry point rejects native execution first.
/// </remarks>
internal static class ManagedOutputRuntimeReflection
{
    private const string TrimJustification =
        "The reflected type belongs to compiled SharpTS output or its managed CLR " +
        "interop closure. That code executes under CoreCLR, and this boundary rejects " +
        "Native AOT before reflection. The Managed SKU intentionally preserves open-world " +
        "third-party assembly interop.";

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetPublicMethodByName(Type type, string name)
    {
        RequireManagedOutputRuntime();
        return type.GetMethod(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetMethodByName(
        Type type,
        string name,
        BindingFlags bindingFlags)
    {
        RequireManagedOutputRuntime();
        return type.GetMethod(name, bindingFlags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetMethodBySignature(
        Type type,
        string name,
        BindingFlags bindingFlags,
        Type[] parameterTypes)
    {
        RequireManagedOutputRuntime();
        return type.GetMethod(name, bindingFlags, binder: null, parameterTypes, modifiers: null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static FieldInfo? GetFieldByName(
        Type type,
        string name,
        BindingFlags bindingFlags)
    {
        RequireManagedOutputRuntime();
        return type.GetField(name, bindingFlags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static ConstructorInfo? GetFirstPublicConstructor(Type type)
    {
        RequireManagedOutputRuntime();
        var constructors = type.GetConstructors();
        return constructors.Length > 0 ? constructors[0] : null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static FieldInfo[] GetNonPublicInstanceFieldsWithPrefix(
        Type type,
        string prefix)
    {
        RequireManagedOutputRuntime();
        return type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => field.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
    }

    private static void RequireManagedOutputRuntime()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                "Executing compiled SharpTS runtime reflection is not available in the " +
                "native SharpTS process — run the generated output under CoreCLR or use " +
                "the managed SharpTS build.");
        }
    }
}
