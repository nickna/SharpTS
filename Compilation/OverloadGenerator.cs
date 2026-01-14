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
    /// Emits the forwarding body for an overload method.
    /// Loads provided arguments, emits default values for missing arguments, then calls full method.
    /// </summary>
    /// <param name="il">IL generator for the overload method</param>
    /// <param name="fullMethod">The full method to call</param>
    /// <param name="parameters">All parameters from AST (for default value expressions)</param>
    /// <param name="overloadArity">Number of parameters in this overload</param>
    /// <param name="isStatic">Whether this is a static method</param>
    /// <param name="emitter">ILEmitter for emitting default value expressions</param>
    public static void EmitOverloadBody(
        ILGenerator il,
        MethodInfo fullMethod,
        List<Stmt.Parameter> parameters,
        int overloadArity,
        bool isStatic,
        ILEmitter emitter)
    {
        int argOffset = isStatic ? 0 : 1;

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

        // Emit default values for missing arguments
        for (int i = overloadArity; i < parameters.Count; i++)
        {
            var defaultExpr = parameters[i].DefaultValue;
            var targetType = fullMethod.GetParameters()[i].ParameterType;

            if (defaultExpr != null)
            {
                emitter.EmitExpression(defaultExpr);
                // Box if needed based on target type
                EmitConversionIfNeeded(il, emitter, defaultExpr, targetType);
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

        // Call the full method
        if (isStatic)
        {
            il.Emit(OpCodes.Call, fullMethod);
        }
        else
        {
            il.Emit(OpCodes.Callvirt, fullMethod);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits conversion from stack value to target type if needed.
    /// </summary>
    private static void EmitConversionIfNeeded(
        ILGenerator il,
        ILEmitter emitter,
        Expr sourceExpr,
        Type targetType)
    {
        // If target is object, box value types
        if (targetType == typeof(object))
        {
            emitter.EmitBoxIfNeeded(sourceExpr);
            return;
        }

        // If source is a literal that might need boxing
        if (sourceExpr is Expr.Literal lit)
        {
            if (lit.Value is double && targetType == typeof(double))
            {
                // Already correct type, no conversion needed
                return;
            }
            if (lit.Value is bool && targetType == typeof(bool))
            {
                return;
            }
            if (lit.Value is string && targetType == typeof(string))
            {
                return;
            }
        }

        // For non-literal expressions that return object, unbox if target is value type
        if (targetType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, targetType);
        }
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

    /// <summary>
    /// Emits the forwarding body for a constructor overload.
    /// </summary>
    public static void EmitConstructorOverloadBody(
        ILGenerator il,
        ConstructorInfo fullConstructor,
        List<Stmt.Parameter> parameters,
        int overloadArity,
        ILEmitter emitter)
    {
        // Load 'this'
        il.Emit(OpCodes.Ldarg_0);

        // Load all provided arguments
        for (int i = 0; i < overloadArity; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1); // +1 for 'this'
        }

        // Emit default values for missing arguments
        for (int i = overloadArity; i < parameters.Count; i++)
        {
            var defaultExpr = parameters[i].DefaultValue;
            if (defaultExpr != null)
            {
                emitter.EmitExpression(defaultExpr);
                var targetType = fullConstructor.GetParameters()[i].ParameterType;
                EmitConversionIfNeeded(il, emitter, defaultExpr, targetType);
            }
            else
            {
                var targetType = fullConstructor.GetParameters()[i].ParameterType;
                EmitDefaultValue(il, targetType);
            }
        }

        // Call the full constructor
        il.Emit(OpCodes.Call, fullConstructor);
        il.Emit(OpCodes.Ret);
    }
}
