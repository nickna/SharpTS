using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Centralized helper methods for union type operations.
/// Used by both C# runtime (RuntimeTypes) and IL emitter (RuntimeEmitter).
/// </summary>
public static class UnionTypeHelper
{
    /// <summary>
    /// Checks if a type is a generated union type.
    /// Uses marker interface for finalized types, falls back to name check for TypeBuilder.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUnionType(Type type)
    {
        // For TypeBuilder before CreateType(), we can't use IsAssignableFrom
        // Fall back to name-based check in that case
        if (type is TypeBuilder)
            return type.Name.StartsWith("Union_") && type.IsValueType;

        return typeof(IUnionType).IsAssignableFrom(type);
    }

    /// <summary>
    /// Checks if an object is a union type instance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUnionValue(object? value)
        => value is IUnionType;

    /// <summary>
    /// Unwraps the underlying value from a union type.
    /// Returns the original value if not a union.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? UnwrapValue(object? value)
        => value is IUnionType union ? union.Value : value;

    /// <summary>
    /// Converts all arguments for union type parameters in a method call.
    /// Modifies the args array in-place using implicit conversion operators.
    /// </summary>
    public static void ConvertArgsForUnionTypes(ParameterInfo[] parameters, object?[] args)
    {
        int count = Math.Min(args.Length, parameters.Length);
        for (int i = 0; i < count; i++)
        {
            var paramType = parameters[i].ParameterType;
            if (!IsUnionType(paramType) || args[i] == null)
                continue;

            var argType = args[i]!.GetType();
            if (argType == paramType)
                continue;

            // Find implicit conversion operator
            var implicitOp = paramType.GetMethod("op_Implicit",
                BindingFlags.Public | BindingFlags.Static,
                null, [argType], null);

            if (implicitOp != null)
                args[i] = implicitOp.Invoke(null, [args[i]]);
        }
    }
}
