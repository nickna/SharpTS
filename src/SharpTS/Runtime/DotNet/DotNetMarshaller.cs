using System.Numerics;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.DotNet;

/// <summary>
/// Converts TypeScript runtime values to .NET parameter types and boxes results back.
/// Mirrors the rules in <c>ExternalMethodResolver.ScoreTypeConversion</c> and
/// <c>ILEmitter.EmitExternalTypeConversion</c> so interpreter and compiled modes
/// agree on which conversions are legal and their relative cost.
/// </summary>
internal static class DotNetMarshaller
{
    /// <summary>
    /// Converts a TypeScript runtime value to the requested .NET target type.
    /// Returns null when conversion is not possible and <paramref name="targetType"/>
    /// is a reference or Nullable type.
    /// </summary>
    /// <param name="interpreter">
    /// Optional interpreter reference, required only when <paramref name="targetType"/> is a
    /// delegate type and <paramref name="value"/> is an <see cref="ISharpTSCallable"/>.
    /// </param>
    /// <exception cref="InvalidCastException">
    /// When the value cannot be converted and the target is a non-nullable value type.
    /// </exception>
    public static object? Convert(object? value, Type targetType, Interpreter? interpreter = null)
    {
        // Unwrap TS undefined
        if (value is SharpTSUndefined) value = null;

        // null handling
        if (value == null)
        {
            if (!targetType.IsValueType) return null;
            if (Nullable.GetUnderlyingType(targetType) != null) return null;
            throw new InvalidCastException($"Cannot convert null to non-nullable value type '{targetType.FullName}'.");
        }

        // Unwrap Nullable<T>
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null) targetType = underlying;

        // TS callable → .NET delegate: build a shim (requires an interpreter reference).
        if (value is ISharpTSCallable callable && typeof(Delegate).IsAssignableFrom(targetType))
        {
            if (interpreter == null)
            {
                throw new InvalidCastException(
                    $"Cannot convert TS function to delegate '{targetType.FullName}' without an interpreter context.");
            }
            return DotNetDelegateShim.Create(targetType, callable, interpreter);
        }

        // Fast path: already the right type
        if (targetType.IsInstanceOfType(value)) return value;

        // Numeric: TS number is always double. Convert to any integral/float .NET type.
        if (value is double d) return ConvertNumber(d, targetType);
        if (value is int i) return ConvertNumber(i, targetType);
        if (value is long l) return ConvertNumber(l, targetType);
        if (value is float f) return ConvertNumber(f, targetType);

        // BigInt
        if (value is BigInteger big)
        {
            if (targetType == typeof(BigInteger)) return big;
            if (targetType == typeof(long)) return (long)big;
            if (targetType == typeof(int)) return (int)big;
            if (targetType == typeof(double)) return (double)big;
        }

        // Boolean
        if (value is bool b)
        {
            if (targetType == typeof(bool)) return b;
            if (targetType == typeof(object)) return b;
            throw new InvalidCastException($"Cannot convert boolean to '{targetType.FullName}'.");
        }

        // String
        if (value is string s)
        {
            if (targetType == typeof(string)) return s;
            if (targetType == typeof(char))
            {
                if (s.Length == 0) throw new InvalidCastException("Cannot convert empty string to char.");
                return s[0];
            }
            if (targetType == typeof(object)) return s;
            throw new InvalidCastException($"Cannot convert string to '{targetType.FullName}'.");
        }

        // DotNetInstance wraps a .NET object — unwrap for the underlying .NET call
        if (value is DotNetInstance dni)
        {
            var unwrapped = dni.Underlying;
            if (targetType.IsInstanceOfType(unwrapped)) return unwrapped;
            throw new InvalidCastException(
                $"Cannot convert .NET instance of '{unwrapped?.GetType().FullName}' to '{targetType.FullName}'.");
        }

        // TS array → .NET array (T[]): convert each element recursively.
        if (value is SharpTSArray tsArray && targetType.IsArray)
        {
            var elementType = targetType.GetElementType()!;
            var array = ManagedDotNetInterop.CreateArray(elementType, tsArray.Length);
            int idx = 0;
            foreach (var el in tsArray)
            {
                array.SetValue(Convert(el, elementType, interpreter), idx++);
            }
            return array;
        }

        // Object fallback
        if (targetType == typeof(object)) return value;

        throw new InvalidCastException(
            $"Cannot convert runtime value of type '{value.GetType().FullName}' to '{targetType.FullName}'.");
    }

    private static object ConvertNumber(double n, Type target)
    {
        if (target == typeof(double)) return n;
        if (target == typeof(float)) return (float)n;
        if (target == typeof(int)) return (int)n;
        if (target == typeof(long)) return (long)n;
        if (target == typeof(short)) return (short)n;
        if (target == typeof(byte)) return (byte)n;
        if (target == typeof(sbyte)) return (sbyte)n;
        if (target == typeof(ushort)) return (ushort)n;
        if (target == typeof(uint)) return (uint)n;
        if (target == typeof(ulong)) return (ulong)n;
        if (target == typeof(char)) return (char)(int)n;
        if (target == typeof(decimal)) return (decimal)n;
        if (target == typeof(BigInteger)) return new BigInteger(n);
        if (target == typeof(object)) return n;
        throw new InvalidCastException($"Cannot convert number to '{target.FullName}'.");
    }

    private static object ConvertNumber(int n, Type target) => ConvertNumber((double)n, target);
    private static object ConvertNumber(long n, Type target) => ConvertNumber((double)n, target);
    private static object ConvertNumber(float n, Type target) => ConvertNumber((double)n, target);

    /// <summary>
    /// Wraps a value returned from a .NET call so it is usable from TypeScript.
    /// Value types stay boxed, reference types pass through, and reference types
    /// that match a registered @DotNetType are wrapped in <see cref="DotNetInstance"/>
    /// so their members are reachable.
    /// </summary>
    public static object? WrapReturn(object? value, Type declaredReturnType)
    {
        if (value == null) return null;
        if (declaredReturnType == typeof(void)) return SharpTSUndefined.Instance;

        if (TryWrapManagedAwaitable(value, declaredReturnType, out SharpTSPromise? promise))
            return promise;

        // CLR arrays cross back as ordinary guest arrays, recursively preserving jagged
        // array shape and wrapping external element values through the same return seam.
        if (value is Array array)
        {
            Type elementType = declaredReturnType.IsArray
                ? declaredReturnType.GetElementType()!
                : value.GetType().GetElementType() ?? typeof(object);
            var elements = new object?[array.Length];
            int index = 0;
            foreach (object? element in array)
                elements[index++] = WrapReturn(element, elementType);
            return new SharpTSArray(elements);
        }

        // Primitives pass through unchanged — the interpreter treats double/string/bool natively.
        var actualType = value.GetType();
        if (actualType == typeof(double) || actualType == typeof(int) || actualType == typeof(long) ||
            actualType == typeof(float) || actualType == typeof(string) || actualType == typeof(bool))
        {
            // Normalize integral types to double so TS arithmetic sees a consistent number type.
            return value switch
            {
                int i => (double)i,
                long l => (double)l,
                float f => (double)f,
                short sh => (double)sh,
                byte by => (double)by,
                _ => value
            };
        }

        // Other numeric/primitive types get normalized similarly
        if (value is short sh2) return (double)sh2;
        if (value is byte by2) return (double)by2;
        if (value is sbyte sb) return (double)sb;
        if (value is ushort us) return (double)us;
        if (value is uint ui) return (double)ui;
        if (value is ulong ul) return (double)ul;
        if (value is char ch) return ch.ToString();
        if (value is decimal dc) return (double)dc;

        // Complex .NET objects get wrapped so future property/method access works.
        return new DotNetInstance(value, actualType);
    }

    private static bool TryWrapManagedAwaitable(
        object value,
        Type declaredReturnType,
        out SharpTSPromise? promise)
    {
        Task? task = null;
        Type? resultType = null;

        if (declaredReturnType == typeof(Task) && value is Task nonGenericTask)
        {
            task = nonGenericTask;
        }
        else if (declaredReturnType.IsGenericType &&
                 declaredReturnType.GetGenericTypeDefinition() == typeof(Task<>) &&
                 value is Task genericTask)
        {
            task = genericTask;
            resultType = declaredReturnType.GetGenericArguments()[0];
        }
        else if (declaredReturnType == typeof(ValueTask) && value is ValueTask valueTask)
        {
            task = valueTask.AsTask();
        }
        else if (declaredReturnType.IsGenericType &&
                 declaredReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            resultType = declaredReturnType.GetGenericArguments()[0];
            task = (Task?)declaredReturnType.GetMethod(nameof(ValueTask<int>.AsTask), Type.EmptyTypes)!
                .Invoke(value, null);
        }

        if (task is null)
        {
            promise = null;
            return false;
        }

        promise = new SharpTSPromise(CompleteManagedAwaitable(task, resultType));
        return true;
    }

    private static async Task<object?> CompleteManagedAwaitable(Task task, Type? resultType)
    {
        await task;
        if (resultType is null)
            return SharpTSUndefined.Instance;

        object? result = task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
        return WrapReturn(result, resultType);
    }
}
