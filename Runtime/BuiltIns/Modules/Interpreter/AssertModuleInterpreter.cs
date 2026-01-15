using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'assert' module.
/// </summary>
/// <remarks>
/// Provides assertion functions for testing.
/// Throws AssertionError when assertions fail.
/// </remarks>
public static class AssertModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the assert module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = new BuiltInMethod("ok", 1, 2, Ok),
            ["strictEqual"] = new BuiltInMethod("strictEqual", 2, 3, StrictEqual),
            ["notStrictEqual"] = new BuiltInMethod("notStrictEqual", 2, 3, NotStrictEqual),
            ["deepStrictEqual"] = new BuiltInMethod("deepStrictEqual", 2, 3, DeepStrictEqual),
            ["notDeepStrictEqual"] = new BuiltInMethod("notDeepStrictEqual", 2, 3, NotDeepStrictEqual),
            ["throws"] = new BuiltInMethod("throws", 1, 2, Throws),
            ["doesNotThrow"] = new BuiltInMethod("doesNotThrow", 1, 2, DoesNotThrow),
            ["fail"] = new BuiltInMethod("fail", 0, 1, Fail),
            ["equal"] = new BuiltInMethod("equal", 2, 3, Equal),
            ["notEqual"] = new BuiltInMethod("notEqual", 2, 3, NotEqual)
        };
    }

    /// <summary>
    /// assert.ok(value, message?) - throws if value is falsy.
    /// </summary>
    private static object? Ok(Interp interpreter, object? receiver, List<object?> args)
    {
        var value = args.Count > 0 ? args[0] : null;
        var message = args.Count > 1 ? args[1]?.ToString() : null;

        if (!IsTruthy(value))
        {
            ThrowAssertionError(
                message ?? "The expression evaluated to a falsy value",
                value,
                true,
                "ok"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.strictEqual(actual, expected, message?) - throws if actual !== expected.
    /// </summary>
    private static object? StrictEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        var actual = args.Count > 0 ? args[0] : null;
        var expected = args.Count > 1 ? args[1] : null;
        var message = args.Count > 2 ? args[2]?.ToString() : null;

        if (!StrictEquals(actual, expected))
        {
            ThrowAssertionError(
                message ?? $"Expected values to be strictly equal:\n{Stringify(actual)}\nshould equal\n{Stringify(expected)}",
                actual,
                expected,
                "strictEqual"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.notStrictEqual(actual, expected, message?) - throws if actual === expected.
    /// </summary>
    private static object? NotStrictEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        var actual = args.Count > 0 ? args[0] : null;
        var expected = args.Count > 1 ? args[1] : null;
        var message = args.Count > 2 ? args[2]?.ToString() : null;

        if (StrictEquals(actual, expected))
        {
            ThrowAssertionError(
                message ?? $"Expected values to be strictly unequal: {Stringify(actual)}",
                actual,
                expected,
                "notStrictEqual"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.deepStrictEqual(actual, expected, message?) - deep comparison.
    /// </summary>
    private static object? DeepStrictEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        var actual = args.Count > 0 ? args[0] : null;
        var expected = args.Count > 1 ? args[1] : null;
        var message = args.Count > 2 ? args[2]?.ToString() : null;

        if (!DeepEquals(actual, expected))
        {
            ThrowAssertionError(
                message ?? $"Expected values to be deeply equal:\n{Stringify(actual)}\nshould equal\n{Stringify(expected)}",
                actual,
                expected,
                "deepStrictEqual"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.notDeepStrictEqual(actual, expected, message?) - throws if deep equal.
    /// </summary>
    private static object? NotDeepStrictEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        var actual = args.Count > 0 ? args[0] : null;
        var expected = args.Count > 1 ? args[1] : null;
        var message = args.Count > 2 ? args[2]?.ToString() : null;

        if (DeepEquals(actual, expected))
        {
            ThrowAssertionError(
                message ?? $"Expected values not to be deeply equal: {Stringify(actual)}",
                actual,
                expected,
                "notDeepStrictEqual"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.throws(fn, message?) - throws if fn doesn't throw.
    /// </summary>
    private static object? Throws(Interp interpreter, object? receiver, List<object?> args)
    {
        var fn = args.Count > 0 ? args[0] : null;
        var message = args.Count > 1 ? args[1]?.ToString() : null;

        if (fn == null)
        {
            ThrowAssertionError(message ?? "Missing function to test", null, null, "throws");
            return null;
        }

        bool threw = false;
        try
        {
            if (fn is SharpTSFunction tsFunc)
            {
                tsFunc.Call(interpreter, []);
            }
            else if (fn is BuiltInMethod builtIn)
            {
                builtIn.Call(interpreter, []);
            }
            else
            {
                ThrowAssertionError("First argument must be a function", fn, null, "throws");
            }
        }
        catch
        {
            threw = true;
        }

        if (!threw)
        {
            ThrowAssertionError(
                message ?? "Missing expected exception",
                null,
                null,
                "throws"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.doesNotThrow(fn, message?) - throws if fn throws.
    /// </summary>
    private static object? DoesNotThrow(Interp interpreter, object? receiver, List<object?> args)
    {
        var fn = args.Count > 0 ? args[0] : null;
        var message = args.Count > 1 ? args[1]?.ToString() : null;

        if (fn == null)
        {
            ThrowAssertionError(message ?? "Missing function to test", null, null, "doesNotThrow");
            return null;
        }

        try
        {
            if (fn is SharpTSFunction tsFunc)
            {
                tsFunc.Call(interpreter, []);
            }
            else if (fn is BuiltInMethod builtIn)
            {
                builtIn.Call(interpreter, []);
            }
            else
            {
                ThrowAssertionError("First argument must be a function", fn, null, "doesNotThrow");
            }
        }
        catch (Exception ex)
        {
            ThrowAssertionError(
                message ?? $"Got unwanted exception: {ex.Message}",
                ex,
                null,
                "doesNotThrow"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.fail(message?) - always throws.
    /// </summary>
    private static object? Fail(Interp interpreter, object? receiver, List<object?> args)
    {
        var message = args.Count > 0 ? args[0]?.ToString() : null;
        ThrowAssertionError(message ?? "Failed", null, null, "fail");
        return null;
    }

    /// <summary>
    /// assert.equal(actual, expected, message?) - loose equality (==).
    /// </summary>
    private static object? Equal(Interp interpreter, object? receiver, List<object?> args)
    {
        var actual = args.Count > 0 ? args[0] : null;
        var expected = args.Count > 1 ? args[1] : null;
        var message = args.Count > 2 ? args[2]?.ToString() : null;

        if (!LooseEquals(actual, expected))
        {
            ThrowAssertionError(
                message ?? $"Expected values to be loosely equal:\n{Stringify(actual)}\nshould equal\n{Stringify(expected)}",
                actual,
                expected,
                "equal"
            );
        }

        return null;
    }

    /// <summary>
    /// assert.notEqual(actual, expected, message?) - loose inequality (!=).
    /// </summary>
    private static object? NotEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        var actual = args.Count > 0 ? args[0] : null;
        var expected = args.Count > 1 ? args[1] : null;
        var message = args.Count > 2 ? args[2]?.ToString() : null;

        if (LooseEquals(actual, expected))
        {
            ThrowAssertionError(
                message ?? $"Expected values not to be loosely equal: {Stringify(actual)}",
                actual,
                expected,
                "notEqual"
            );
        }

        return null;
    }

    // Helper methods

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
        if (a == null || b == null)
        {
            // In JS, null == undefined
            return false;
        }

        // Same type - use strict equality
        if (a.GetType() == b.GetType())
        {
            return StrictEquals(a, b);
        }

        // Try numeric coercion
        if (TryToNumber(a, out var numA) && TryToNumber(b, out var numB))
        {
            return numA == numB;
        }

        // String comparison
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

    private static bool DeepEquals(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // Primitives
        if (a is double || a is string || a is bool || b is double || b is string || b is bool)
        {
            return StrictEquals(a, b);
        }

        // Arrays
        if (a is List<object?> listA && b is List<object?> listB)
        {
            if (listA.Count != listB.Count) return false;
            for (int i = 0; i < listA.Count; i++)
            {
                if (!DeepEquals(listA[i], listB[i])) return false;
            }
            return true;
        }

        if (a is SharpTSArray arrA && b is SharpTSArray arrB)
        {
            if (arrA.Elements.Count != arrB.Elements.Count) return false;
            for (int i = 0; i < arrA.Elements.Count; i++)
            {
                if (!DeepEquals(arrA.Elements[i], arrB.Elements[i])) return false;
            }
            return true;
        }

        // Objects
        if (a is SharpTSObject objA && b is SharpTSObject objB)
        {
            if (objA.Fields.Count != objB.Fields.Count) return false;
            foreach (var kvp in objA.Fields)
            {
                if (!objB.Fields.TryGetValue(kvp.Key, out var valueB))
                    return false;
                if (!DeepEquals(kvp.Value, valueB))
                    return false;
            }
            return true;
        }

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

        // Fallback to reference equality
        return ReferenceEquals(a, b);
    }

    private static string Stringify(object? value)
    {
        if (value == null) return "null";
        if (value is string s) return $"\"{s}\"";
        if (value is bool b) return b ? "true" : "false";
        if (value is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value is List<object?> list)
            return $"[{string.Join(", ", list.Select(Stringify))}]";
        if (value is SharpTSArray arr)
            return $"[{string.Join(", ", arr.Elements.Select(Stringify))}]";
        if (value is SharpTSObject obj)
            return $"{{{string.Join(", ", obj.Fields.Select(kvp => $"{kvp.Key}: {Stringify(kvp.Value)}"))}}}";
        if (value is Dictionary<string, object?> dict)
            return $"{{{string.Join(", ", dict.Select(kvp => $"{kvp.Key}: {Stringify(kvp.Value)}"))}}}";
        return value.ToString() ?? "undefined";
    }

    private static void ThrowAssertionError(string message, object? actual, object? expected, string @operator)
    {
        throw new AssertionError(message, actual, expected, @operator);
    }
}

/// <summary>
/// Custom error type for assertion failures.
/// </summary>
public class AssertionError : Exception
{
    public object? Actual { get; }
    public object? Expected { get; }
    public string Operator { get; }

    public AssertionError(string message, object? actual, object? expected, string @operator)
        : base($"AssertionError: {message}")
    {
        Actual = actual;
        Expected = expected;
        Operator = @operator;
    }
}
