using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Generates method overloads for functions with default parameters.
/// Instead of runtime null-checks, generates separate methods for each arity.
/// </summary>
/// <remarks>
/// For a function like: function foo(a: number, b: number = 10, c: string = "x")
/// Generates:
/// - foo(double a, double b, string c) - full implementation
/// - foo(double a, double b) => foo(a, b, "x") - forwards with default c
/// - foo(double a) => foo(a, 10.0, "x") - forwards with default b,c
/// </remarks>
public static class OverloadGenerator
{
    /// <summary>
    /// Gets the parameter type arrays for each overload that should be generated.
    /// Returns empty list if no overloads are needed (no default parameters).
    /// </summary>
    /// <param name="parameters">The function parameters from AST</param>
    /// <param name="fullParamTypes">The resolved types for all parameters</param>
    /// <returns>List of parameter type arrays, one per overload (excluding full signature)</returns>
    public static List<Type[]> GetOverloadSignatures(
        List<Stmt.Parameter> parameters,
        Type[] fullParamTypes)
    {
        var overloads = new List<Type[]>();

        // Find the index of the first parameter with a default value
        int firstDefaultIndex = -1;
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].DefaultValue != null)
            {
                firstDefaultIndex = i;
                break;
            }
        }

        // No default parameters = no overloads needed
        if (firstDefaultIndex < 0)
            return overloads;

        // Generate overloads for each arity from (firstDefaultIndex) down to (firstDefaultIndex)
        // i.e., for foo(a, b=1, c=2), generate foo(a, b) and foo(a)
        for (int arity = parameters.Count - 1; arity >= firstDefaultIndex; arity--)
        {
            var overloadTypes = new Type[arity];
            Array.Copy(fullParamTypes, overloadTypes, arity);
            overloads.Add(overloadTypes);
        }

        return overloads;
    }

    /// <summary>
    /// Emits the forwarding body for an overload method. Loads the provided arguments, supplies an
    /// <c>undefined</c> placeholder for the next default, then forwards to
    /// <paramref name="targetMethod"/> — the overload one arity higher (or the full implementation
    /// when this overload is one arity below it).
    /// </summary>
    /// <remarks>
    /// Forwarding is <b>cascading</b>, not direct-to-full: an overload of arity <c>k</c> fills exactly
    /// the default at index <c>k</c> and calls the arity-<c>k+1</c> method, which fills index <c>k+1</c>,
    /// and so on. The full implementation's ordered prologue evaluates every placeholder in the real
    /// function environment, so defaults can reference earlier parameters, direct eval bindings, and
    /// the function's display class. (#698)
    /// </remarks>
    /// <param name="il">IL generator for the overload method</param>
    /// <param name="targetMethod">The next-higher-arity method to forward to (overload or full)</param>
    /// <param name="parameters">All parameters from AST (for default value expressions)</param>
    /// <param name="overloadArity">Number of parameters in this overload</param>
    /// <param name="isStatic">Whether this is a static method</param>
    /// <param name="emitter">ILEmitter used only for a defensive value-type fallback</param>
    /// <param name="undefinedInstance">The emitted runtime's JavaScript undefined singleton</param>
    public static void EmitOverloadBody(
        ILGenerator il,
        MethodInfo targetMethod,
        List<Stmt.Parameter> parameters,
        int overloadArity,
        bool isStatic,
        ILEmitter emitter,
        FieldInfo undefinedInstance)
    {
        int argOffset = isStatic ? 0 : 1;
        var targetParams = targetMethod.GetParameters();

        // Load 'this' for instance methods
        if (!isStatic)
        {
            il.Emit(OpCodes.Ldarg_0);
        }

        // Load all provided arguments
        for (int i = 0; i < overloadArity; i++)
        {
            il.Emit(OpCodes.Ldarg, i + argOffset);
        }

        // Supply the defaults this overload adds to reach the target method's arity. Under cascading
        // forwarding that is the single parameter at index `overloadArity`, whose default may reference
        // any earlier parameter (all are real arguments here, so they resolve to `ldarg`). (#698)
        for (int i = overloadArity; i < targetParams.Length; i++)
        {
            var defaultExpr = parameters[i].DefaultValue;
            var targetType = targetParams[i].ParameterType;

            if (defaultExpr != null)
            {
                // Defaulted parameters are widened to object slots. Evaluate
                // the initializer only in the full function prologue, where
                // its parameter environment and display class both exist.
                if (targetType == typeof(object))
                {
                    il.Emit(OpCodes.Ldsfld, undefinedInstance);
                }
                else
                {
                    // Defensive fallback if a defaulted value-type signature
                    // escaped widening.
                    emitter.EmitExpression(defaultExpr);
                    emitter.EmitConversionForParameter(defaultExpr, targetType);
                }
            }
            else
            {
                // No explicit default - check if this is an optional parameter expecting null
                // (indicated by object type for what would otherwise be a value type)
                if (targetType == typeof(object))
                {
                    // Optional parameter with no default - pass null
                    il.Emit(OpCodes.Ldnull);
                }
                else
                {
                    // Required parameter or typed optional - emit type's default value
                    EmitDefaultValue(il, targetType);
                }
            }
        }

        // Forward to the next-higher-arity method (cascading).
        if (isStatic)
        {
            il.Emit(OpCodes.Call, targetMethod);
        }
        else
        {
            il.Emit(OpCodes.Callvirt, targetMethod);
        }

        il.Emit(OpCodes.Ret);
    }


    /// <summary>
    /// Emits the default value for a type (0 for numbers, false for bool, null for references).
    /// </summary>
    private static void EmitDefaultValue(ILGenerator il, Type type)
    {
        if (type == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else if (type == typeof(int))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(bool))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (type == typeof(float))
        {
            il.Emit(OpCodes.Ldc_R4, 0.0f);
        }
        else if (type == typeof(long))
        {
            il.Emit(OpCodes.Ldc_I8, 0L);
        }
        else if (type.IsValueType)
        {
            // For other value types, use initobj
            var local = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Initobj, type);
            il.Emit(OpCodes.Ldloc, local);
        }
        else
        {
            // Reference types default to null
            il.Emit(OpCodes.Ldnull);
        }
    }
}
