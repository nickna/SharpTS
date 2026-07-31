using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Managed-only boundary for open-world .NET type resolution, reflection, and
/// runtime-generated interop shapes.
/// </summary>
/// <remarks>
/// The managed SKU intentionally supports BCL and third-party types that were
/// unknown when SharpTS was built. Native AOT cannot retain or compile that
/// unbounded type universe. Until a separately rooted native BCL contract is
/// defined, every dynamic interop operation fails here before using metadata
/// or constructing a runtime shape.
/// </remarks>
internal static class ManagedDotNetInterop
{
    internal const string ManagedBuildRequiredMessage =
        ".NET interop is not available in the native SharpTS build — use the managed build.";

    private const string TrimJustification =
        "Open-world .NET interop is a Managed-SKU feature. The native build is rejected " +
        "before inspecting arbitrary BCL, embedder, or third-party types.";

    private const string DynamicCodeJustification =
        "Open-world .NET interop is a Managed-SKU feature. The native build is rejected " +
        "before constructing an array, generic, delegate, or other runtime-generated shape.";

    internal static void RequireManagedRuntime()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new PlatformNotSupportedException(ManagedBuildRequiredMessage);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = TrimJustification)]
    internal static Type? ResolveType(string typeName)
    {
        RequireManagedRuntime();
        return Type.GetType(typeName, throwOnError: false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimJustification)]
    internal static Type? ResolveType(Assembly assembly, string typeName)
    {
        RequireManagedRuntime();
        return assembly.GetType(typeName, throwOnError: false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo[] GetMethods(Type type, BindingFlags flags)
    {
        RequireManagedRuntime();
        return type.GetMethods(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetMethod(Type type, string name)
    {
        RequireManagedRuntime();
        return type.GetMethod(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetMethod(Type type, string name, Type[] parameterTypes)
    {
        RequireManagedRuntime();
        return type.GetMethod(name, parameterTypes);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static ConstructorInfo[] GetConstructors(Type type, BindingFlags flags)
    {
        RequireManagedRuntime();
        return type.GetConstructors(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo[] GetProperties(Type type, BindingFlags flags)
    {
        RequireManagedRuntime();
        return type.GetProperties(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo? GetProperty(Type type, string name, BindingFlags flags)
    {
        RequireManagedRuntime();
        return type.GetProperty(name, flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static FieldInfo? GetField(Type type, string name, BindingFlags flags)
    {
        RequireManagedRuntime();
        return type.GetField(name, flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static EventInfo? GetEvent(Type type, string name, BindingFlags flags)
    {
        RequireManagedRuntime();
        return type.GetEvent(name, flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static Type[] GetInterfaces(Type type)
    {
        RequireManagedRuntime();
        return type.GetInterfaces();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimJustification)]
    internal static object? CreateInstance(Type type)
    {
        RequireManagedRuntime();
        return Activator.CreateInstance(type);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = DynamicCodeJustification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Type MakeGenericType(Type definition, params Type[] arguments)
    {
        RequireManagedRuntime();
        return definition.MakeGenericType(arguments);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = DynamicCodeJustification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static MethodInfo MakeGenericMethod(MethodInfo definition, params Type[] arguments)
    {
        RequireManagedRuntime();
        return definition.MakeGenericMethod(arguments);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Type MakeArrayType(Type elementType)
    {
        RequireManagedRuntime();
        return elementType.MakeArrayType();
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Array CreateArray(Type elementType, int length)
    {
        RequireManagedRuntime();
        return Array.CreateInstance(elementType, length);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static NewArrayExpression NewArrayInit(
        Type elementType,
        params Expression[] expressions)
    {
        RequireManagedRuntime();
        return Expression.NewArrayInit(elementType, expressions);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Delegate CompileLambda(
        Type delegateType,
        Expression body,
        params ParameterExpression[] parameters)
    {
        RequireManagedRuntime();
        return Expression.Lambda(delegateType, body, parameters).Compile();
    }
}
