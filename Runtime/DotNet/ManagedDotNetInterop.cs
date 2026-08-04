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

    internal const string NativeCatalogRequiredMessage =
        ".NET interop type or member is not present in this native SharpTS catalog — " +
        "use a custom native host that registers it, or use the managed build.";

    private const string TrimJustification =
        "Open-world .NET interop is a Managed-SKU feature. The native build is rejected " +
        "before inspecting arbitrary BCL, embedder, or third-party types.";

    private const string DynamicCodeJustification =
        "Open-world .NET interop is a Managed-SKU feature. The native build is rejected " +
        "before constructing an array, generic, delegate, or other runtime-generated shape.";

    internal static void RequireManagedRuntime(Type? catalogType = null)
    {
        if (RuntimeFeature.IsDynamicCodeSupported)
            return;

        // Some runtime-provided MethodInfo shapes (notably closed generic BCL
        // helpers) report no declaring type. Entry into those operations has
        // already been gated by discovery on a cataloged owner; null must not
        // turn that closed member into an apparent open-world request.
        if (catalogType is null && NativeDotNetInterop.Catalog != null)
            return;

        if (catalogType != null && NativeDotNetInterop.IsAllowed(catalogType))
            return;

        throw new PlatformNotSupportedException(
            NativeDotNetInterop.Catalog == null
                ? ManagedBuildRequiredMessage
                : $"{NativeCatalogRequiredMessage} Requested runtime type: '{catalogType?.FullName ?? catalogType?.ToString() ?? "<unknown>"}'.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = TrimJustification)]
    internal static Type? ResolveType(string typeName)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            if (NativeDotNetInterop.Catalog is { } catalog &&
                catalog.TryResolveType(typeName, out Type? catalogType))
            {
                return catalogType;
            }
            throw new PlatformNotSupportedException(
                NativeDotNetInterop.Catalog == null
                    ? ManagedBuildRequiredMessage
                    : $"{NativeCatalogRequiredMessage} Requested type: '{typeName}'.");
        }
        return Type.GetType(typeName, throwOnError: false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = TrimJustification)]
    internal static Type? ResolveType(Assembly assembly, string typeName)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            if (NativeDotNetInterop.Catalog is { } catalog &&
                catalog.TryResolveType(typeName, out Type? catalogType))
            {
                return catalogType;
            }
            throw new PlatformNotSupportedException(
                $"{NativeCatalogRequiredMessage} Requested type: '{typeName}'.");
        }
        return assembly.GetType(typeName, throwOnError: false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo[] GetMethods(Type type, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetMethods(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetMethod(Type type, string name)
    {
        RequireManagedRuntime(type);
        return type.GetMethod(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetMethod(Type type, string name, Type[] parameterTypes)
    {
        RequireManagedRuntime(type);
        return type.GetMethod(name, parameterTypes);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static ConstructorInfo[] GetConstructors(Type type, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetConstructors(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static ConstructorInfo? GetConstructor(Type type, Type[] parameterTypes)
    {
        RequireManagedRuntime(type);
        return type.GetConstructor(parameterTypes);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static ConstructorInfo? GetConstructor(
        Type type,
        BindingFlags flags,
        Type[] parameterTypes)
    {
        RequireManagedRuntime(type);
        return type.GetConstructor(
            flags,
            binder: null,
            parameterTypes,
            modifiers: null);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo[] GetProperties(Type type, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetProperties(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo? GetProperty(Type type, string name, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetProperty(name, flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static FieldInfo? GetField(Type type, string name, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetField(name, flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static FieldInfo[] GetFields(Type type, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetFields(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static EventInfo? GetEvent(Type type, string name, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetEvent(name, flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static EventInfo[] GetEvents(Type type, BindingFlags flags)
    {
        RequireManagedRuntime(type);
        return type.GetEvents(flags);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static Type[] GetInterfaces(Type type)
    {
        RequireManagedRuntime(type);
        return type.GetInterfaces();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = TrimJustification)]
    internal static object? CreateInstance(Type type)
    {
        RequireManagedRuntime(type);
        return Activator.CreateInstance(type);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = DynamicCodeJustification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Type MakeGenericType(Type definition, params Type[] arguments)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            RequireManagedRuntime(definition);
            if (NativeDotNetInterop.Catalog is { } catalog &&
                catalog.TryGetConstructedGeneric(definition, arguments, out Type? constructed))
            {
                return constructed;
            }
            throw new PlatformNotSupportedException(NativeCatalogRequiredMessage);
        }
        return definition.MakeGenericType(arguments);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = DynamicCodeJustification)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static MethodInfo MakeGenericMethod(MethodInfo definition, params Type[] arguments)
    {
        RequireManagedRuntime(definition.DeclaringType);
        if (!RuntimeFeature.IsDynamicCodeSupported &&
            arguments.Any(argument => !NativeDotNetInterop.IsAllowed(argument)))
        {
            throw new PlatformNotSupportedException(NativeCatalogRequiredMessage);
        }
        return definition.MakeGenericMethod(arguments);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Type MakeArrayType(Type elementType)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            RequireManagedRuntime(elementType);
            if (TryGetIntrinsicArrayType(elementType, out Type? intrinsicArray))
                return intrinsicArray;
            if (NativeDotNetInterop.Catalog is { } catalog &&
                catalog.TryGetArrayType(elementType, out Type? arrayType))
            {
                return arrayType;
            }
            throw new PlatformNotSupportedException(NativeCatalogRequiredMessage);
        }
        return elementType.MakeArrayType();
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Type GetActionType(params Type[] parameterTypes)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            Type definition = parameterTypes.Length switch
            {
                0 => typeof(Action),
                1 => typeof(Action<>),
                2 => typeof(Action<,>),
                3 => typeof(Action<,,>),
                4 => typeof(Action<,,,>),
                _ => throw new PlatformNotSupportedException(NativeCatalogRequiredMessage)
            };
            if (parameterTypes.Length == 0)
                return NativeDotNetInterop.IsAllowed(definition)
                    ? definition
                    : throw new PlatformNotSupportedException(NativeCatalogRequiredMessage);
            return MakeGenericType(definition, parameterTypes);
        }
        return Expression.GetActionType(parameterTypes);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Type GetFuncType(params Type[] parameterAndReturnTypes)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            Type definition = parameterAndReturnTypes.Length switch
            {
                1 => typeof(Func<>),
                2 => typeof(Func<,>),
                3 => typeof(Func<,,>),
                4 => typeof(Func<,,,>),
                5 => typeof(Func<,,,,>),
                _ => throw new PlatformNotSupportedException(NativeCatalogRequiredMessage)
            };
            return MakeGenericType(definition, parameterAndReturnTypes);
        }
        return Expression.GetFuncType(parameterAndReturnTypes);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Array CreateArray(Type elementType, int length)
    {
        RequireManagedRuntime(elementType);
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            if (TryCreateIntrinsicArray(elementType, length, out Array? intrinsicArray))
                return intrinsicArray;
            INativeDotNetCatalog catalog = NativeDotNetInterop.Catalog
                ?? throw new PlatformNotSupportedException(ManagedBuildRequiredMessage);
            if (!catalog.TryGetArrayType(elementType, out _))
                throw new PlatformNotSupportedException(NativeCatalogRequiredMessage);
        }
        return Array.CreateInstance(elementType, length);
    }

    private static bool TryGetIntrinsicArrayType(
        Type elementType,
        [NotNullWhen(true)] out Type? arrayType)
    {
        arrayType = elementType == typeof(object) ? typeof(object[])
            : elementType == typeof(string) ? typeof(string[])
            : elementType == typeof(bool) ? typeof(bool[])
            : elementType == typeof(char) ? typeof(char[])
            : elementType == typeof(byte) ? typeof(byte[])
            : elementType == typeof(int) ? typeof(int[])
            : elementType == typeof(long) ? typeof(long[])
            : elementType == typeof(float) ? typeof(float[])
            : elementType == typeof(double) ? typeof(double[])
            : null;
        return arrayType != null;
    }

    private static bool TryCreateIntrinsicArray(
        Type elementType,
        int length,
        [NotNullWhen(true)] out Array? array)
    {
        array = elementType == typeof(object) ? new object?[length]
            : elementType == typeof(string) ? new string?[length]
            : elementType == typeof(bool) ? new bool[length]
            : elementType == typeof(char) ? new char[length]
            : elementType == typeof(byte) ? new byte[length]
            : elementType == typeof(int) ? new int[length]
            : elementType == typeof(long) ? new long[length]
            : elementType == typeof(float) ? new float[length]
            : elementType == typeof(double) ? new double[length]
            : null;
        return array != null;
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static NewArrayExpression NewArrayInit(
        Type elementType,
        params Expression[] expressions)
    {
        RequireManagedRuntime(elementType);
        return Expression.NewArrayInit(elementType, expressions);
    }

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = DynamicCodeJustification)]
    internal static Delegate CompileLambda(
        Type delegateType,
        Expression body,
        params ParameterExpression[] parameters)
    {
        RequireManagedRuntime(delegateType);
        return Expression.Lambda(delegateType, body, parameters).Compile();
    }
}
