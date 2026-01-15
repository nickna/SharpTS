using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits assert module helper methods.
    /// </summary>
    private void EmitAssertMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitAssertOk(typeBuilder, runtime);
        EmitAssertStrictEqual(typeBuilder, runtime);
        EmitAssertNotStrictEqual(typeBuilder, runtime);
        EmitAssertDeepStrictEqual(typeBuilder, runtime);
        EmitAssertNotDeepStrictEqual(typeBuilder, runtime);
        EmitAssertThrows(typeBuilder, runtime);
        EmitAssertDoesNotThrow(typeBuilder, runtime);
        EmitAssertFail(typeBuilder, runtime);
        EmitAssertEqual(typeBuilder, runtime);
        EmitAssertNotEqual(typeBuilder, runtime);
        EmitAssertMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits wrapper methods for assert functions that can be used as first-class values.
    /// </summary>
    private void EmitAssertMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ok(value, message?) - 2 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "ok", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.AssertOk);
            il.Emit(OpCodes.Ldnull);
        });

        // strictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "strictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // notStrictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "notStrictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertNotStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // deepStrictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "deepStrictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertDeepStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // notDeepStrictEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "notDeepStrictEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertNotDeepStrictEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // throws(fn, message?) - 2 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "throws", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.AssertThrows);
            il.Emit(OpCodes.Ldnull);
        });

        // doesNotThrow(fn, message?) - 2 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "doesNotThrow", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.AssertDoesNotThrow);
            il.Emit(OpCodes.Ldnull);
        });

        // fail(message?) - 1 param
        EmitAssertWrapperSimple(typeBuilder, runtime, "fail", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AssertFail);
            il.Emit(OpCodes.Ldnull);
        });

        // equal(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "equal", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertEqual);
            il.Emit(OpCodes.Ldnull);
        });

        // notEqual(actual, expected, message?) - 3 params
        EmitAssertWrapperSimple(typeBuilder, runtime, "notEqual", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.AssertNotEqual);
            il.Emit(OpCodes.Ldnull);
        });
    }

    private void EmitAssertWrapperSimple(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator> emitCall)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            $"Assert_{methodName}_Wrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes
        );

        var il = method.GetILGenerator();
        emitCall(il);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("assert", methodName, method);
    }

    /// <summary>
    /// Emits: public static void AssertOk(object? value, object? message)
    /// </summary>
    private void EmitAssertOk(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertOk",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.AssertOk = method;

        var il = method.GetILGenerator();

        // Call AssertHelpers.Ok(value, message) which handles the message correctly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("Ok", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertStrictEqual = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("StrictEqual", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertNotStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertNotStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertNotStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertNotStrictEqual = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("NotStrictEqual", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertDeepStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertDeepStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertDeepStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertDeepStrictEqual = method;

        var il = method.GetILGenerator();

        // For now, call the interpreter version via reflection
        // This is a placeholder - full deep equality comparison would be complex to emit in IL
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("DeepStrictEqual", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertNotDeepStrictEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertNotDeepStrictEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertNotDeepStrictEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertNotDeepStrictEqual = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("NotDeepStrictEqual", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertThrows(object? fn, object? message)
    /// </summary>
    private void EmitAssertThrows(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertThrows",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.AssertThrows = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("Throws", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertDoesNotThrow(object? fn, object? message)
    /// </summary>
    private void EmitAssertDoesNotThrow(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertDoesNotThrow",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.AssertDoesNotThrow = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("DoesNotThrow", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertFail(object? message)
    /// </summary>
    private void EmitAssertFail(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertFail",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.AssertFail = method;

        var il = method.GetILGenerator();

        // Get message or default
        var notNull = il.DefineLabel();
        var afterMsg = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Ldstr, "Failed");
        il.Emit(OpCodes.Br, afterMsg);
        il.MarkLabel(notNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterMsg);

        // throw new AssertionError(message)
        il.Emit(OpCodes.Ldnull); // actual
        il.Emit(OpCodes.Ldnull); // expected
        il.Emit(OpCodes.Ldstr, "fail"); // operator
        il.Emit(OpCodes.Newobj, typeof(AssertionError).GetConstructor([typeof(string), typeof(object), typeof(object), typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static void AssertEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertEqual = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("Equal", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AssertNotEqual(object? actual, object? expected, object? message)
    /// </summary>
    private void EmitAssertNotEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AssertNotEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.AssertNotEqual = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(AssertHelpers).GetMethod("NotEqual", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitThrowAssertionError(ILGenerator il, string defaultMessage, string @operator)
    {
        // Check if custom message provided (arg1 or arg2 depending on method)
        il.Emit(OpCodes.Ldstr, defaultMessage);
        il.Emit(OpCodes.Ldnull); // actual
        il.Emit(OpCodes.Ldnull); // expected
        il.Emit(OpCodes.Ldstr, @operator);
        il.Emit(OpCodes.Newobj, typeof(AssertionError).GetConstructor([typeof(string), typeof(object), typeof(object), typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }
}

/// <summary>
/// Static helper methods for assert module in compiled mode.
/// These are called from emitted IL for complex assertion logic.
/// </summary>
public static class AssertHelpers
{
    public static void Ok(object? value, object? message)
    {
        if (!IsTruthy(value))
        {
            var msg = message?.ToString() ?? "The expression evaluated to a falsy value";
            throw new AssertionError(msg, value, true, "ok");
        }
    }

    public static void StrictEqual(object? actual, object? expected, object? message)
    {
        if (!StrictEquals(actual, expected))
        {
            var msg = message?.ToString() ?? "Expected values to be strictly equal";
            throw new AssertionError(msg, actual, expected, "strictEqual");
        }
    }

    public static void NotStrictEqual(object? actual, object? expected, object? message)
    {
        if (StrictEquals(actual, expected))
        {
            var msg = message?.ToString() ?? "Expected values not to be strictly equal";
            throw new AssertionError(msg, actual, expected, "notStrictEqual");
        }
    }

    public static void DeepStrictEqual(object? actual, object? expected, object? message)
    {
        if (!DeepEquals(actual, expected))
        {
            var msg = message?.ToString() ?? $"Expected values to be deeply equal";
            throw new AssertionError(msg, actual, expected, "deepStrictEqual");
        }
    }

    public static void NotDeepStrictEqual(object? actual, object? expected, object? message)
    {
        if (DeepEquals(actual, expected))
        {
            var msg = message?.ToString() ?? $"Expected values not to be deeply equal";
            throw new AssertionError(msg, actual, expected, "notDeepStrictEqual");
        }
    }

    public static void Throws(object? fn, object? message)
    {
        if (fn == null)
        {
            throw new AssertionError(message?.ToString() ?? "Missing function to test", null, null, "throws");
        }

        bool threw = false;
        try
        {
            // Try to invoke the function
            if (fn is Delegate del)
            {
                del.DynamicInvoke([]);
            }
            else
            {
                // Try via reflection for TSFunction
                var invokeMethod = fn.GetType().GetMethod("Invoke");
                if (invokeMethod != null)
                {
                    invokeMethod.Invoke(fn, [Array.Empty<object>()]);
                }
                else
                {
                    throw new AssertionError("First argument must be a function", fn, null, "throws");
                }
            }
        }
        catch (AssertionError)
        {
            // Re-throw assertion errors from nested assertions
            throw;
        }
        catch (Exception ex) when (ex is not AssertionError)
        {
            threw = true;
        }

        if (!threw)
        {
            throw new AssertionError(message?.ToString() ?? "Missing expected exception", null, null, "throws");
        }
    }

    public static void DoesNotThrow(object? fn, object? message)
    {
        if (fn == null)
        {
            throw new AssertionError(message?.ToString() ?? "Missing function to test", null, null, "doesNotThrow");
        }

        try
        {
            if (fn is Delegate del)
            {
                del.DynamicInvoke([]);
            }
            else
            {
                var invokeMethod = fn.GetType().GetMethod("Invoke");
                if (invokeMethod != null)
                {
                    invokeMethod.Invoke(fn, [Array.Empty<object>()]);
                }
                else
                {
                    throw new AssertionError("First argument must be a function", fn, null, "doesNotThrow");
                }
            }
        }
        catch (Exception ex) when (ex is not AssertionError)
        {
            throw new AssertionError(message?.ToString() ?? $"Got unwanted exception: {ex.Message}", ex, null, "doesNotThrow");
        }
    }

    public static void Equal(object? actual, object? expected, object? message)
    {
        if (!LooseEquals(actual, expected))
        {
            var msg = message?.ToString() ?? "Expected values to be loosely equal";
            throw new AssertionError(msg, actual, expected, "equal");
        }
    }

    public static void NotEqual(object? actual, object? expected, object? message)
    {
        if (LooseEquals(actual, expected))
        {
            var msg = message?.ToString() ?? "Expected values not to be loosely equal";
            throw new AssertionError(msg, actual, expected, "notEqual");
        }
    }

    private static bool DeepEquals(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Primitives
        if (a is double || a is string || a is bool || b is double || b is string || b is bool)
        {
            return StrictEquals(a, b);
        }

        // Arrays (List<object?> in compiled mode)
        if (a is List<object?> listA && b is List<object?> listB)
        {
            if (listA.Count != listB.Count) return false;
            for (int i = 0; i < listA.Count; i++)
            {
                if (!DeepEquals(listA[i], listB[i])) return false;
            }
            return true;
        }

        // Objects (Dictionary<string, object?> in compiled mode)
        if (a is Dictionary<string, object?> dictA && b is Dictionary<string, object?> dictB)
        {
            if (dictA.Count != dictB.Count) return false;
            foreach (var kvp in dictA)
            {
                if (!dictB.TryGetValue(kvp.Key, out var valueB))
                    return false;
                if (!DeepEquals(kvp.Value, valueB))
                    return false;
            }
            return true;
        }

        return ReferenceEquals(a, b);
    }

    private static bool StrictEquals(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.GetType() != b.GetType()) return false;

        return a switch
        {
            double da when b is double db => da.Equals(db),
            string sa when b is string sb => sa == sb,
            bool ba when b is bool bb => ba == bb,
            _ => ReferenceEquals(a, b)
        };
    }

    private static bool LooseEquals(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        if (a.GetType() == b.GetType())
            return StrictEquals(a, b);

        // Try numeric coercion
        if (TryToNumber(a, out var numA) && TryToNumber(b, out var numB))
            return numA == numB;

        return a.ToString() == b.ToString();
    }

    private static bool TryToNumber(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;
        if (value is double d) { result = d; return true; }
        if (value is int i) { result = i; return true; }
        if (value is string s && double.TryParse(s, out result)) return true;
        if (value is bool b) { result = b ? 1 : 0; return true; }
        return false;
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            string s => s.Length > 0,
            _ => true
        };
    }
}
