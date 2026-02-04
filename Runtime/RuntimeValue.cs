using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

/// <summary>
/// Classifies the kind of value stored in a <see cref="RuntimeValue"/>.
/// </summary>
/// <remarks>
/// The enum is ordered so that the most common primitives (boolean, number) have low values
/// for potential branch prediction optimization. The byte-sized type minimizes struct size.
/// </remarks>
public enum ValueKind : byte
{
    /// <summary>JavaScript undefined value.</summary>
    Undefined = 0,

    /// <summary>JavaScript null value.</summary>
    Null = 1,

    /// <summary>Boolean primitive (true/false). Stored inline without boxing.</summary>
    Boolean = 2,

    /// <summary>Numeric primitive (IEEE 754 double). Stored inline without boxing.</summary>
    Number = 3,

    /// <summary>String primitive. Stored as reference in object slot.</summary>
    String = 4,

    /// <summary>Reference type object (arrays, class instances, functions, etc.).</summary>
    Object = 5,

    /// <summary>Symbol primitive. Stored as reference in object slot.</summary>
    Symbol = 6,

    /// <summary>BigInt value. Stored as reference in object slot.</summary>
    BigInt = 7
}

/// <summary>
/// A discriminated union struct that holds TypeScript runtime values without boxing primitives.
/// </summary>
/// <remarks>
/// <para>
/// This struct is the core building block for eliminating boxing overhead in the interpreter.
/// Numeric values (double) and booleans are stored inline in the struct's fields, avoiding
/// heap allocation entirely. Reference types (strings, objects, arrays) are stored in the
/// <c>_objectValue</c> field.
/// </para>
/// <para>
/// The struct uses explicit layout with overlapping fields to minimize size while maintaining
/// type safety through the <see cref="Kind"/> discriminator.
/// </para>
/// <para>
/// <b>Memory Layout (24 bytes on 64-bit):</b>
/// <code>
/// Offset  Size  Field
/// 0       1     Kind (ValueKind enum, byte)
/// 1-7     7     (padding)
/// 8       8     _numberValue (double) / _boolValue conceptually overlaps but kept separate for clarity
/// 16      8     _objectValue (object reference)
/// </code>
/// </para>
/// <para>
/// <b>Thread Safety:</b> RuntimeValue is immutable and safe to share across threads.
/// However, the objects referenced by <c>_objectValue</c> may not be thread-safe.
/// </para>
/// </remarks>
/// <seealso cref="RuntimeEnvironment"/>
/// <seealso cref="ISharpTSCallable"/>
[StructLayout(LayoutKind.Auto)]
public readonly struct RuntimeValue : IEquatable<RuntimeValue>
{
    #region Fields

    /// <summary>
    /// Discriminator indicating which type of value is stored.
    /// </summary>
    public readonly ValueKind Kind;

    /// <summary>
    /// Stores numeric values (when Kind == Number).
    /// Also used to store boolean as 0.0/1.0 to share the same field (optimization).
    /// </summary>
    private readonly double _numberValue;

    /// <summary>
    /// Stores reference types: strings, objects, arrays, functions, symbols, bigints.
    /// </summary>
    private readonly object? _objectValue;

    #endregion

    #region Singleton Instances

    /// <summary>
    /// The JavaScript undefined value.
    /// </summary>
    public static readonly RuntimeValue Undefined = new(ValueKind.Undefined);

    /// <summary>
    /// The JavaScript null value.
    /// </summary>
    public static readonly RuntimeValue Null = new(ValueKind.Null);

    /// <summary>
    /// The boolean true value.
    /// </summary>
    public static readonly RuntimeValue True = new(true);

    /// <summary>
    /// The boolean false value.
    /// </summary>
    public static readonly RuntimeValue False = new(false);

    /// <summary>
    /// The numeric zero value.
    /// </summary>
    public static readonly RuntimeValue Zero = new(0.0);

    /// <summary>
    /// The numeric one value.
    /// </summary>
    public static readonly RuntimeValue One = new(1.0);

    /// <summary>
    /// The numeric NaN value.
    /// </summary>
    public static readonly RuntimeValue NaN = new(double.NaN);

    /// <summary>
    /// The empty string value.
    /// </summary>
    public static readonly RuntimeValue EmptyString = new(string.Empty);

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a RuntimeValue for null or undefined.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RuntimeValue(ValueKind kind)
    {
        Kind = kind;
        _numberValue = 0;
        _objectValue = null;
    }

    /// <summary>
    /// Creates a RuntimeValue holding a number.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RuntimeValue(double value)
    {
        Kind = ValueKind.Number;
        _numberValue = value;
        _objectValue = null;
    }

    /// <summary>
    /// Creates a RuntimeValue holding a boolean.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RuntimeValue(bool value)
    {
        Kind = ValueKind.Boolean;
        // Store boolean as 1.0 (true) or 0.0 (false) in the number field
        _numberValue = value ? 1.0 : 0.0;
        _objectValue = null;
    }

    /// <summary>
    /// Creates a RuntimeValue holding a string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RuntimeValue(string value)
    {
        Kind = ValueKind.String;
        _numberValue = 0;
        _objectValue = value;
    }

    /// <summary>
    /// Creates a RuntimeValue holding an object reference.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private RuntimeValue(ValueKind kind, object? value)
    {
        Kind = kind;
        _numberValue = 0;
        _objectValue = value;
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a RuntimeValue from a double.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromNumber(double value) => new(value);

    /// <summary>
    /// Creates a RuntimeValue from a boolean.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromBoolean(bool value) => value ? True : False;

    /// <summary>
    /// Creates a RuntimeValue from a string.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromString(string? value)
    {
        if (value is null) return Null;
        if (value.Length == 0) return EmptyString;
        return new RuntimeValue(value);
    }

    /// <summary>
    /// Creates a RuntimeValue from a Symbol.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromSymbol(SharpTSSymbol symbol)
        => new(ValueKind.Symbol, symbol);

    /// <summary>
    /// Creates a RuntimeValue from a BigInt.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromBigInt(SharpTSBigInt bigInt)
        => new(ValueKind.BigInt, bigInt);

    /// <summary>
    /// Creates a RuntimeValue from a BigInteger.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromBigInt(BigInteger value)
        => new(ValueKind.BigInt, new SharpTSBigInt(value));

    /// <summary>
    /// Creates a RuntimeValue from a reference type object.
    /// </summary>
    /// <remarks>
    /// This is the general-purpose factory for objects like SharpTSArray, SharpTSInstance,
    /// SharpTSFunction, etc. The object is stored directly without any transformation.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RuntimeValue FromObject(object? value)
    {
        if (value is null) return Null;
        return new RuntimeValue(ValueKind.Object, value);
    }

    /// <summary>
    /// Creates a RuntimeValue from any boxed value, performing type detection.
    /// </summary>
    /// <remarks>
    /// This method is used at interop boundaries when receiving boxed values from
    /// external .NET code or when migrating from the old object?-based system.
    /// It has overhead from type checking, so prefer specific factory methods when
    /// the type is known at compile time.
    /// </remarks>
    public static RuntimeValue FromBoxed(object? boxed)
    {
        return boxed switch
        {
            null => Null,
            SharpTSUndefined => Undefined,
            double d => new RuntimeValue(d),
            bool b => FromBoolean(b),
            string s => FromString(s),
            int i => new RuntimeValue((double)i),
            long l => new RuntimeValue((double)l),
            float f => new RuntimeValue((double)f),
            SharpTSSymbol sym => FromSymbol(sym),
            SharpTSBigInt bi => FromBigInt(bi),
            BigInteger bigInt => FromBigInt(bigInt),
            RuntimeValue rv => rv, // Already a RuntimeValue (e.g., from nested call)
            _ => FromObject(boxed)
        };
    }

    #endregion

    #region Type-Safe Accessors

    /// <summary>
    /// Gets the numeric value. Throws if Kind is not Number.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Kind is not Number.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double AsNumber()
    {
        if (Kind != ValueKind.Number)
            ThrowInvalidKind(ValueKind.Number);
        return _numberValue;
    }

    /// <summary>
    /// Gets the numeric value without type checking.
    /// </summary>
    /// <remarks>
    /// Use only when you have already verified Kind == Number.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double AsNumberUnsafe() => _numberValue;

    /// <summary>
    /// Gets the boolean value. Throws if Kind is not Boolean.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Kind is not Boolean.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AsBoolean()
    {
        if (Kind != ValueKind.Boolean)
            ThrowInvalidKind(ValueKind.Boolean);
        return _numberValue != 0.0;
    }

    /// <summary>
    /// Gets the boolean value without type checking.
    /// </summary>
    /// <remarks>
    /// Use only when you have already verified Kind == Boolean.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AsBooleanUnsafe() => _numberValue != 0.0;

    /// <summary>
    /// Gets the string value. Throws if Kind is not String.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Kind is not String.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string AsString()
    {
        if (Kind != ValueKind.String)
            ThrowInvalidKind(ValueKind.String);
        return (string)_objectValue!;
    }

    /// <summary>
    /// Gets the string value without type checking.
    /// </summary>
    /// <remarks>
    /// Use only when you have already verified Kind == String.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string AsStringUnsafe() => (string)_objectValue!;

    /// <summary>
    /// Gets the object value cast to type T. Throws if Kind is not Object or cast fails.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when Kind is not Object.</exception>
    /// <exception cref="InvalidCastException">Thrown when object cannot be cast to T.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T AsObject<T>() where T : class
    {
        if (Kind != ValueKind.Object)
            ThrowInvalidKind(ValueKind.Object);
        return (T)_objectValue!;
    }

    /// <summary>
    /// Gets the object value cast to type T without type checking.
    /// </summary>
    /// <remarks>
    /// Use only when you have already verified Kind == Object and the type.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T AsObjectUnsafe<T>() where T : class => (T)_objectValue!;

    /// <summary>
    /// Tries to get the object value cast to type T.
    /// </summary>
    /// <returns>True if the value is an object of type T.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAsObject<T>([NotNullWhen(true)] out T? value) where T : class
    {
        if (Kind == ValueKind.Object && _objectValue is T t)
        {
            value = t;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Gets the Symbol value. Throws if Kind is not Symbol.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SharpTSSymbol AsSymbol()
    {
        if (Kind != ValueKind.Symbol)
            ThrowInvalidKind(ValueKind.Symbol);
        return (SharpTSSymbol)_objectValue!;
    }

    /// <summary>
    /// Gets the BigInt value. Throws if Kind is not BigInt.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SharpTSBigInt AsBigInt()
    {
        if (Kind != ValueKind.BigInt)
            ThrowInvalidKind(ValueKind.BigInt);
        return (SharpTSBigInt)_objectValue!;
    }

    /// <summary>
    /// Gets the raw object reference regardless of Kind.
    /// </summary>
    /// <remarks>
    /// For Object, Symbol, BigInt, String kinds, returns the stored reference.
    /// For Number and Boolean kinds, returns null (values stored inline).
    /// For Null and Undefined kinds, returns null.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetObjectReference() => _objectValue;

    #endregion

    #region Boxing Interop

    /// <summary>
    /// Converts the RuntimeValue to a boxed object for interop with external .NET code.
    /// </summary>
    /// <remarks>
    /// This method should be used sparingly, only at boundaries where boxed objects
    /// are required (e.g., calling .NET APIs, Dictionary&lt;string, object?&gt; storage).
    /// </remarks>
    public object? ToObject()
    {
        return Kind switch
        {
            ValueKind.Undefined => SharpTSUndefined.Instance,
            ValueKind.Null => null,
            ValueKind.Boolean => _numberValue != 0.0,
            ValueKind.Number => _numberValue,
            ValueKind.String => _objectValue,
            ValueKind.Symbol => _objectValue,
            ValueKind.BigInt => _objectValue,
            ValueKind.Object => _objectValue,
            _ => _objectValue
        };
    }

    #endregion

    #region Type Checking

    /// <summary>
    /// Returns true if this value is null or undefined.
    /// </summary>
    public bool IsNullish => Kind <= ValueKind.Null;

    /// <summary>
    /// Returns true if this value is undefined.
    /// </summary>
    public bool IsUndefined => Kind == ValueKind.Undefined;

    /// <summary>
    /// Returns true if this value is null.
    /// </summary>
    public bool IsNull => Kind == ValueKind.Null;

    /// <summary>
    /// Returns true if this value is a boolean.
    /// </summary>
    public bool IsBoolean => Kind == ValueKind.Boolean;

    /// <summary>
    /// Returns true if this value is a number.
    /// </summary>
    public bool IsNumber => Kind == ValueKind.Number;

    /// <summary>
    /// Returns true if this value is a string.
    /// </summary>
    public bool IsString => Kind == ValueKind.String;

    /// <summary>
    /// Returns true if this value is an object.
    /// </summary>
    public bool IsObject => Kind == ValueKind.Object;

    /// <summary>
    /// Returns true if this value is a symbol.
    /// </summary>
    public bool IsSymbol => Kind == ValueKind.Symbol;

    /// <summary>
    /// Returns true if this value is a bigint.
    /// </summary>
    public bool IsBigInt => Kind == ValueKind.BigInt;

    #endregion

    #region JavaScript Semantics

    /// <summary>
    /// Determines if the value is truthy according to JavaScript semantics.
    /// </summary>
    /// <remarks>
    /// Falsy values are: undefined, null, false, 0, -0, NaN, "", 0n.
    /// All other values are truthy.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsTruthy()
    {
        return Kind switch
        {
            ValueKind.Undefined => false,
            ValueKind.Null => false,
            ValueKind.Boolean => _numberValue != 0.0,
            ValueKind.Number => _numberValue != 0.0 && !double.IsNaN(_numberValue),
            ValueKind.String => ((string)_objectValue!).Length > 0,
            ValueKind.BigInt => ((SharpTSBigInt)_objectValue!).Value != 0,
            _ => true // Objects, symbols are always truthy
        };
    }

    /// <summary>
    /// Returns the JavaScript typeof string for this value.
    /// </summary>
    public string TypeofString()
    {
        return Kind switch
        {
            ValueKind.Undefined => "undefined",
            ValueKind.Null => "object", // JavaScript quirk: typeof null === "object"
            ValueKind.Boolean => "boolean",
            ValueKind.Number => "number",
            ValueKind.String => "string",
            ValueKind.Symbol => "symbol",
            ValueKind.BigInt => "bigint",
            ValueKind.Object => _objectValue switch
            {
                ISharpTSCallable => "function",
                _ => "object"
            },
            _ => "undefined"
        };
    }

    #endregion

    #region Implicit Conversions

    /// <summary>
    /// Implicit conversion from double to RuntimeValue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RuntimeValue(double d) => new(d);

    /// <summary>
    /// Implicit conversion from bool to RuntimeValue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RuntimeValue(bool b) => FromBoolean(b);

    /// <summary>
    /// Implicit conversion from string to RuntimeValue.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RuntimeValue(string? s) => FromString(s);

    /// <summary>
    /// Implicit conversion from int to RuntimeValue (widened to double).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator RuntimeValue(int i) => new((double)i);

    #endregion

    #region Equality

    /// <summary>
    /// Implements JavaScript loose equality (==).
    /// </summary>
    public bool LooseEquals(RuntimeValue other)
    {
        // Same kind - use strict equality
        if (Kind == other.Kind)
            return StrictEquals(other);

        // null == undefined
        if (IsNullish && other.IsNullish)
            return true;

        // Type coercion rules (simplified)
        // Full JavaScript == semantics require more complex coercion
        return false;
    }

    /// <summary>
    /// Implements JavaScript strict equality (===).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool StrictEquals(RuntimeValue other)
    {
        if (Kind != other.Kind)
            return false;

        return Kind switch
        {
            ValueKind.Undefined => true,
            ValueKind.Null => true,
            ValueKind.Boolean => _numberValue == other._numberValue,
            ValueKind.Number => _numberValue == other._numberValue, // NaN != NaN handled by IEEE 754
            ValueKind.String => string.Equals((string)_objectValue!, (string)other._objectValue!, StringComparison.Ordinal),
            ValueKind.Symbol => ReferenceEquals(_objectValue, other._objectValue),
            ValueKind.BigInt => ((SharpTSBigInt)_objectValue!).Value == ((SharpTSBigInt)other._objectValue!).Value,
            ValueKind.Object => ReferenceEquals(_objectValue, other._objectValue),
            _ => false
        };
    }

    /// <inheritdoc />
    public bool Equals(RuntimeValue other) => StrictEquals(other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RuntimeValue other && StrictEquals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Kind switch
        {
            ValueKind.Undefined => 0,
            ValueKind.Null => 1,
            ValueKind.Boolean => _numberValue != 0.0 ? 2 : 3,
            ValueKind.Number => _numberValue.GetHashCode(),
            ValueKind.String => ((string)_objectValue!).GetHashCode(),
            ValueKind.Symbol => RuntimeHelpers.GetHashCode(_objectValue!),
            ValueKind.BigInt => ((SharpTSBigInt)_objectValue!).Value.GetHashCode(),
            ValueKind.Object => RuntimeHelpers.GetHashCode(_objectValue!),
            _ => 0
        };
    }

    /// <summary>
    /// Strict equality operator.
    /// </summary>
    public static bool operator ==(RuntimeValue left, RuntimeValue right) => left.StrictEquals(right);

    /// <summary>
    /// Strict inequality operator.
    /// </summary>
    public static bool operator !=(RuntimeValue left, RuntimeValue right) => !left.StrictEquals(right);

    #endregion

    #region ToString

    /// <inheritdoc />
    public override string ToString()
    {
        return Kind switch
        {
            ValueKind.Undefined => "undefined",
            ValueKind.Null => "null",
            ValueKind.Boolean => _numberValue != 0.0 ? "true" : "false",
            ValueKind.Number => FormatNumber(_numberValue),
            ValueKind.String => (string)_objectValue!,
            ValueKind.Symbol => _objectValue?.ToString() ?? "Symbol()",
            ValueKind.BigInt => ((SharpTSBigInt)_objectValue!).Value.ToString() + "n",
            ValueKind.Object => _objectValue?.ToString() ?? "null",
            _ => "undefined"
        };
    }

    /// <summary>
    /// Formats a number according to JavaScript conventions.
    /// </summary>
    private static string FormatNumber(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";

        // Remove trailing .0 for integers
        string text = d.ToString("G17");
        if (text.EndsWith(".0"))
        {
            text = text[..^2];
        }
        return text;
    }

    #endregion

    #region Error Helpers

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowInvalidKind(ValueKind expected)
    {
        throw new InvalidOperationException(
            $"RuntimeValue has Kind {Kind}, expected {expected}");
    }

    #endregion
}
