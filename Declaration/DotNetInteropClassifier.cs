using System.Reflection;

namespace SharpTS.Declaration;

/// <summary>
/// Single source of truth for "can SharpTS's <c>@DotNetType</c> interop actually use this
/// today?" Classifies a .NET type slot (a parameter or return type) or a whole type as
/// <em>usable</em> or <em>unsupported (with a reason)</em>.
/// </summary>
/// <remarks>
/// The <c>--gen-decl</c> discovery tool (<see cref="DiscoveryEmitter"/>) and the runtime
/// interop marshaller (<c>Runtime/DotNet/DotNetMarshaller.cs</c>) must never disagree about
/// what is callable, so both classify against these rules. Method parameters passed by
/// reference are projected to ordinary TypeScript inputs/tuple outputs; by-ref returns,
/// pointers, ref structs (<c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c>), and open generic
/// parameters remain unsupported.
/// </remarks>
public static class DotNetInteropClassifier
{
    /// <summary>Human-readable reason a slot is unsupported: a by-ref return.</summary>
    public const string ReasonByRef = "by-ref returns cannot cross the interop boundary";

    /// <summary>Human-readable reason a by-ref constructor parameter is unsupported.</summary>
    public const string ReasonByRefConstructor =
        "by-ref constructor parameters cannot be tuple-lowered because new must return the constructed instance";

    /// <summary>Human-readable reason a slot is unsupported: a pointer type.</summary>
    public const string ReasonPointer = "pointer types are not marshalable";

    /// <summary>Human-readable reason a slot is unsupported: a ref struct (Span&lt;T&gt; etc.).</summary>
    public const string ReasonRefStruct = "ref struct (Span/ReadOnlySpan) cannot cross the interop boundary";

    /// <summary>Human-readable reason a slot is unsupported: an unbound generic parameter.</summary>
    public const string ReasonOpenGeneric = "open generic has no concrete runtime type";

    /// <summary>Human-readable reason a CLR indexer cannot use TypeScript bracket syntax.</summary>
    public const string ReasonMultiParameterIndexer =
        "indexers with multiple parameters cannot use TypeScript bracket syntax";

    /// <summary>
    /// Returns null if a single type slot (a parameter or a method/property return type) can
    /// be marshaled across the interop boundary today, or a human-readable reason if it can't.
    /// </summary>
    public static string? UnsupportedSlotReason(Type type)
    {
        // By-ref return/property slots cannot be materialized as a TypeScript value. Method
        // parameters are classified through UnsupportedParameterReason instead.
        if (type.IsByRef)
            return ReasonByRef;

        // Raw pointers can't be represented as a TS runtime value.
        if (type.IsPointer)
            return ReasonPointer;

        // Ref structs (Span<T>, ReadOnlySpan<T>, …) cannot be boxed, so they can never be
        // passed as an object across the interop boundary.
        if (type.IsByRefLike)
            return ReasonRefStruct;

        // An unbound generic parameter (T) or any type that still contains one has no
        // concrete runtime type to marshal to/from.
        if (type.IsGenericParameter || type.ContainsGenericParameters)
            return ReasonOpenGeneric;

        return null;
    }

    /// <summary>
    /// Returns null when a method parameter is supported. A by-ref parameter is usable when
    /// its element type is usable: the bridge supplies it as an input when appropriate and
    /// returns its updated value in a tuple.
    /// </summary>
    public static string? UnsupportedParameterReason(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType()!;
        return UnsupportedSlotReason(type);
    }

    /// <summary>
    /// Classifies a slot on a generic method definition. Open shapes are supported when every
    /// unbound parameter belongs to that method; invocation closes them before marshalling.
    /// </summary>
    public static string? UnsupportedGenericMethodSlotReason(
        Type type,
        IReadOnlyCollection<Type> methodGenericParameters,
        bool isParameter)
    {
        if (isParameter && type.IsByRef)
            type = type.GetElementType()!;

        string? reason = UnsupportedSlotReason(type);
        if (reason != ReasonOpenGeneric)
            return reason;
        return EnumerateGenericParameters(type).All(methodGenericParameters.Contains)
            ? null
            : reason;
    }

    /// <summary>Returns the first unsupported boundary slot on a reflected method.</summary>
    public static string? UnsupportedMethodReason(MethodInfo method)
    {
        var genericParameters = method.IsGenericMethodDefinition
            ? method.GetGenericArguments()
            : Type.EmptyTypes;

        string? reason = genericParameters.Length > 0
            ? UnsupportedGenericMethodSlotReason(
                method.ReturnType, genericParameters, isParameter: false)
            : UnsupportedSlotReason(method.ReturnType);
        if (reason != null)
            return reason;

        foreach (var parameter in method.GetParameters())
        {
            reason = genericParameters.Length > 0
                ? UnsupportedGenericMethodSlotReason(
                    parameter.ParameterType, genericParameters, isParameter: true)
                : UnsupportedParameterReason(parameter.ParameterType);
            if (reason != null)
                return reason;
        }
        return null;
    }

    /// <summary>Returns the first unsupported boundary slot on a CLR constructor.</summary>
    public static string? UnsupportedConstructorReason(ConstructorInfo constructor)
    {
        foreach (var parameter in constructor.GetParameters())
        {
            if (parameter.ParameterType.IsByRef)
                return ReasonByRefConstructor;
            string? reason = UnsupportedSlotReason(parameter.ParameterType);
            if (reason != null)
                return reason;
        }
        return null;
    }

    /// <summary>
    /// Returns null if the type itself can be imported/used as a whole, or a reason if it
    /// can't (an open generic type definition, a pointer, or a ref struct).
    /// </summary>
    public static string? UnsupportedTypeReason(Type type)
    {
        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            return ReasonOpenGeneric;
        if (type.IsPointer)
            return ReasonPointer;
        if (type.IsByRefLike)
            return ReasonRefStruct;
        return null;
    }

    private static IEnumerable<Type> EnumerateGenericParameters(Type type)
    {
        if (type.IsGenericParameter)
        {
            yield return type;
            yield break;
        }
        if (type.HasElementType)
        {
            foreach (var parameter in EnumerateGenericParameters(type.GetElementType()!))
                yield return parameter;
        }
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var parameter in EnumerateGenericParameters(argument))
                yield return parameter;
        }
    }
}
