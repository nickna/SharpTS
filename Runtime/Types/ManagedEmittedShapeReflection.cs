using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Reflection boundary for types emitted into a compiled SharpTS program.
/// Those types live in the output assembly and therefore cannot be referenced
/// directly from <c>SharpTS.dll</c>.
/// </summary>
/// <remarks>
/// A compiler-emitted object can only enter these hybrid runtime paths after
/// its output assembly has been loaded by CoreCLR. Native AOT cannot load that
/// managed assembly into the SharpTS process, so the boundary rejects native
/// execution before inspecting the emitted shape. The closed
/// <see cref="ManagedEmittedShape"/> set keeps these suppressions from becoming
/// a general-purpose escape hatch for arbitrary .NET interop reflection.
/// </remarks>
internal static class ManagedEmittedShapeReflection
{
    private const string TrimJustification =
        "The type is validated as a known SharpTS compiler-emitted managed shape, and the " +
        "boundary rejects Native AOT before reflection. Emitted output assemblies are loaded " +
        "only by the managed SharpTS SKU.";

    internal static bool IsShape(Type type, ManagedEmittedShape shape)
    {
        ArgumentNullException.ThrowIfNull(type);
        return shape switch
        {
            ManagedEmittedShape.Object => type.Name == "$Object",
            ManagedEmittedShape.Function => type.Name is "$TSFunction" or "$BoundTSFunction",
            ManagedEmittedShape.WritableStream => type.Name == "$WritableStream",
            ManagedEmittedShape.MessagePort => type.Name == "$MessagePort",
            _ => false
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static MethodInfo? GetPublicMethod(
        Type type,
        ManagedEmittedShape shape,
        string name,
        Type[] parameterTypes)
    {
        RequireManagedShape(type, shape);
        return type.GetMethod(name, parameterTypes);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static PropertyInfo? GetPublicProperty(
        Type type,
        ManagedEmittedShape shape,
        string name)
    {
        RequireManagedShape(type, shape);
        return type.GetProperty(name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = TrimJustification)]
    internal static FieldInfo? GetNonPublicInstanceField(
        Type type,
        ManagedEmittedShape shape,
        string name)
    {
        RequireManagedShape(type, shape);
        return type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
    }

    private static void RequireManagedShape(Type type, ManagedEmittedShape shape)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!IsShape(type, shape))
        {
            throw new ArgumentException(
                $"Type '{type.FullName}' is not the expected compiler-emitted {DisplayName(shape)} shape.",
                nameof(type));
        }

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new PlatformNotSupportedException(
                $"Bridging compiler-emitted {DisplayName(shape)} objects is not available " +
                "in the native SharpTS build — use the managed build.");
        }
    }

    private static string DisplayName(ManagedEmittedShape shape) => shape switch
    {
        ManagedEmittedShape.Object => "$Object",
        ManagedEmittedShape.Function => "$TSFunction/$BoundTSFunction",
        ManagedEmittedShape.WritableStream => "$WritableStream",
        ManagedEmittedShape.MessagePort => "$MessagePort",
        _ => shape.ToString()
    };
}

internal enum ManagedEmittedShape
{
    Object,
    Function,
    WritableStream,
    MessagePort
}
