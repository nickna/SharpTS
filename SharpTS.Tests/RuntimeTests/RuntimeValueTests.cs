using System.Numerics;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

/// <summary>
/// Unit tests for the RuntimeValue struct - validates discriminated union behavior,
/// factory methods, type-safe accessors, and JavaScript semantics.
/// </summary>
public class RuntimeValueTests
{
    #region Factory Methods

    [Fact]
    public void FromNumber_CreatesNumberValue()
    {
        var value = RuntimeValue.FromNumber(42.5);
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.Equal(42.5, value.AsNumber());
    }

    [Fact]
    public void FromNumber_HandlesNaN()
    {
        var value = RuntimeValue.FromNumber(double.NaN);
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.True(double.IsNaN(value.AsNumber()));
    }

    [Fact]
    public void FromNumber_HandlesInfinity()
    {
        var posInf = RuntimeValue.FromNumber(double.PositiveInfinity);
        var negInf = RuntimeValue.FromNumber(double.NegativeInfinity);

        Assert.True(double.IsPositiveInfinity(posInf.AsNumber()));
        Assert.True(double.IsNegativeInfinity(negInf.AsNumber()));
    }

    [Fact]
    public void FromNumber_HandlesNegativeZero()
    {
        var negZero = RuntimeValue.FromNumber(-0.0);
        Assert.Equal(ValueKind.Number, negZero.Kind);
        // -0.0 == 0.0 in IEEE 754
        Assert.Equal(0.0, negZero.AsNumber());
    }

    [Fact]
    public void FromBoolean_True_CreatesCorrectValue()
    {
        var value = RuntimeValue.FromBoolean(true);
        Assert.Equal(ValueKind.Boolean, value.Kind);
        Assert.True(value.AsBoolean());
    }

    [Fact]
    public void FromBoolean_False_CreatesCorrectValue()
    {
        var value = RuntimeValue.FromBoolean(false);
        Assert.Equal(ValueKind.Boolean, value.Kind);
        Assert.False(value.AsBoolean());
    }

    [Fact]
    public void FromBoolean_ReturnsCachedInstances()
    {
        var true1 = RuntimeValue.FromBoolean(true);
        var true2 = RuntimeValue.FromBoolean(true);
        var false1 = RuntimeValue.FromBoolean(false);
        var false2 = RuntimeValue.FromBoolean(false);

        // Same struct values (they're value types, so we compare by value)
        Assert.Equal(true1, true2);
        Assert.Equal(false1, false2);
        Assert.NotEqual(true1, false1);
    }

    [Fact]
    public void FromString_CreatesStringValue()
    {
        var value = RuntimeValue.FromString("hello");
        Assert.Equal(ValueKind.String, value.Kind);
        Assert.Equal("hello", value.AsString());
    }

    [Fact]
    public void FromString_Null_ReturnsNullValue()
    {
        var value = RuntimeValue.FromString(null);
        Assert.Equal(ValueKind.Null, value.Kind);
        Assert.True(value.IsNull);
    }

    [Fact]
    public void FromString_Empty_ReturnsCachedEmptyString()
    {
        var value = RuntimeValue.FromString("");
        Assert.Equal(ValueKind.String, value.Kind);
        Assert.Equal("", value.AsString());
    }

    [Fact]
    public void FromObject_WithArray_CreatesObjectValue()
    {
        var array = new SharpTSArray();
        var value = RuntimeValue.FromObject(array);

        Assert.Equal(ValueKind.Object, value.Kind);
        Assert.Same(array, value.AsObject<SharpTSArray>());
    }

    [Fact]
    public void FromObject_Null_ReturnsNullValue()
    {
        var value = RuntimeValue.FromObject(null);
        Assert.Equal(ValueKind.Null, value.Kind);
    }

    [Fact]
    public void FromSymbol_CreatesSymbolValue()
    {
        var symbol = new SharpTSSymbol("test");
        var value = RuntimeValue.FromSymbol(symbol);

        Assert.Equal(ValueKind.Symbol, value.Kind);
        Assert.Same(symbol, value.AsSymbol());
    }

    [Fact]
    public void FromBigInt_CreatesBigIntValue()
    {
        var bigInt = new SharpTSBigInt(BigInteger.Parse("12345678901234567890"));
        var value = RuntimeValue.FromBigInt(bigInt);

        Assert.Equal(ValueKind.BigInt, value.Kind);
        Assert.Equal(BigInteger.Parse("12345678901234567890"), value.AsBigInt().Value);
    }

    [Fact]
    public void FromBigInt_WithBigInteger_CreatesBigIntValue()
    {
        var bigInteger = BigInteger.Parse("98765432109876543210");
        var value = RuntimeValue.FromBigInt(bigInteger);

        Assert.Equal(ValueKind.BigInt, value.Kind);
        Assert.Equal(bigInteger, value.AsBigInt().Value);
    }

    #endregion

    #region FromBoxed

    [Fact]
    public void FromBoxed_Double_CreatesNumberValue()
    {
        object boxed = 42.0;
        var value = RuntimeValue.FromBoxed(boxed);
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.Equal(42.0, value.AsNumber());
    }

    [Fact]
    public void FromBoxed_Int_CreatesNumberValue()
    {
        object boxed = 42;
        var value = RuntimeValue.FromBoxed(boxed);
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.Equal(42.0, value.AsNumber());
    }

    [Fact]
    public void FromBoxed_Long_CreatesNumberValue()
    {
        object boxed = 42L;
        var value = RuntimeValue.FromBoxed(boxed);
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.Equal(42.0, value.AsNumber());
    }

    [Fact]
    public void FromBoxed_Bool_CreatesBooleanValue()
    {
        object boxed = true;
        var value = RuntimeValue.FromBoxed(boxed);
        Assert.Equal(ValueKind.Boolean, value.Kind);
        Assert.True(value.AsBoolean());
    }

    [Fact]
    public void FromBoxed_String_CreatesStringValue()
    {
        object boxed = "test";
        var value = RuntimeValue.FromBoxed(boxed);
        Assert.Equal(ValueKind.String, value.Kind);
        Assert.Equal("test", value.AsString());
    }

    [Fact]
    public void FromBoxed_Null_CreatesNullValue()
    {
        var value = RuntimeValue.FromBoxed(null);
        Assert.Equal(ValueKind.Null, value.Kind);
    }

    [Fact]
    public void FromBoxed_SharpTSUndefined_CreatesUndefinedValue()
    {
        var value = RuntimeValue.FromBoxed(SharpTSUndefined.Instance);
        Assert.Equal(ValueKind.Undefined, value.Kind);
    }

    [Fact]
    public void FromBoxed_RuntimeValue_ReturnsItself()
    {
        var original = RuntimeValue.FromNumber(42);
        object boxed = original;
        var result = RuntimeValue.FromBoxed(boxed);

        Assert.Equal(original, result);
    }

    [Fact]
    public void FromBoxed_Object_CreatesObjectValue()
    {
        var obj = new SharpTSObject(new Dictionary<string, object?>());
        var value = RuntimeValue.FromBoxed(obj);

        Assert.Equal(ValueKind.Object, value.Kind);
        Assert.Same(obj, value.AsObject<SharpTSObject>());
    }

    #endregion

    #region Singleton Instances

    [Fact]
    public void Undefined_HasCorrectKind()
    {
        Assert.Equal(ValueKind.Undefined, RuntimeValue.Undefined.Kind);
        Assert.True(RuntimeValue.Undefined.IsUndefined);
        Assert.True(RuntimeValue.Undefined.IsNullish);
    }

    [Fact]
    public void Null_HasCorrectKind()
    {
        Assert.Equal(ValueKind.Null, RuntimeValue.Null.Kind);
        Assert.True(RuntimeValue.Null.IsNull);
        Assert.True(RuntimeValue.Null.IsNullish);
    }

    [Fact]
    public void True_HasCorrectValue()
    {
        Assert.Equal(ValueKind.Boolean, RuntimeValue.True.Kind);
        Assert.True(RuntimeValue.True.AsBoolean());
    }

    [Fact]
    public void False_HasCorrectValue()
    {
        Assert.Equal(ValueKind.Boolean, RuntimeValue.False.Kind);
        Assert.False(RuntimeValue.False.AsBoolean());
    }

    [Fact]
    public void Zero_HasCorrectValue()
    {
        Assert.Equal(ValueKind.Number, RuntimeValue.Zero.Kind);
        Assert.Equal(0.0, RuntimeValue.Zero.AsNumber());
    }

    [Fact]
    public void One_HasCorrectValue()
    {
        Assert.Equal(ValueKind.Number, RuntimeValue.One.Kind);
        Assert.Equal(1.0, RuntimeValue.One.AsNumber());
    }

    [Fact]
    public void NaN_HasCorrectValue()
    {
        Assert.Equal(ValueKind.Number, RuntimeValue.NaN.Kind);
        Assert.True(double.IsNaN(RuntimeValue.NaN.AsNumber()));
    }

    [Fact]
    public void EmptyString_HasCorrectValue()
    {
        Assert.Equal(ValueKind.String, RuntimeValue.EmptyString.Kind);
        Assert.Equal("", RuntimeValue.EmptyString.AsString());
    }

    #endregion

    #region Type-Safe Accessors

    [Fact]
    public void AsNumber_OnNonNumber_Throws()
    {
        var value = RuntimeValue.FromBoolean(true);
        Assert.Throws<InvalidOperationException>(() => value.AsNumber());
    }

    [Fact]
    public void AsBoolean_OnNonBoolean_Throws()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Throws<InvalidOperationException>(() => value.AsBoolean());
    }

    [Fact]
    public void AsString_OnNonString_Throws()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Throws<InvalidOperationException>(() => value.AsString());
    }

    [Fact]
    public void AsObject_OnNonObject_Throws()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Throws<InvalidOperationException>(() => value.AsObject<SharpTSArray>());
    }

    [Fact]
    public void AsSymbol_OnNonSymbol_Throws()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Throws<InvalidOperationException>(() => value.AsSymbol());
    }

    [Fact]
    public void AsBigInt_OnNonBigInt_Throws()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Throws<InvalidOperationException>(() => value.AsBigInt());
    }

    [Fact]
    public void TryAsObject_OnMatchingType_ReturnsTrue()
    {
        var array = new SharpTSArray();
        var value = RuntimeValue.FromObject(array);

        Assert.True(value.TryAsObject<SharpTSArray>(out var result));
        Assert.Same(array, result);
    }

    [Fact]
    public void TryAsObject_OnNonMatchingType_ReturnsFalse()
    {
        var array = new SharpTSArray();
        var value = RuntimeValue.FromObject(array);

        Assert.False(value.TryAsObject<SharpTSObject>(out var result));
        Assert.Null(result);
    }

    [Fact]
    public void TryAsObject_OnNonObject_ReturnsFalse()
    {
        var value = RuntimeValue.FromNumber(42);

        Assert.False(value.TryAsObject<SharpTSArray>(out var result));
        Assert.Null(result);
    }

    #endregion

    #region Unsafe Accessors

    [Fact]
    public void AsNumberUnsafe_ReturnsValue()
    {
        var value = RuntimeValue.FromNumber(42.5);
        Assert.Equal(42.5, value.AsNumberUnsafe());
    }

    [Fact]
    public void AsBooleanUnsafe_ReturnsValue()
    {
        var value = RuntimeValue.FromBoolean(true);
        Assert.True(value.AsBooleanUnsafe());
    }

    [Fact]
    public void AsStringUnsafe_ReturnsValue()
    {
        var value = RuntimeValue.FromString("test");
        Assert.Equal("test", value.AsStringUnsafe());
    }

    [Fact]
    public void AsObjectUnsafe_ReturnsValue()
    {
        var array = new SharpTSArray();
        var value = RuntimeValue.FromObject(array);
        Assert.Same(array, value.AsObjectUnsafe<SharpTSArray>());
    }

    #endregion

    #region ToObject

    [Fact]
    public void ToObject_Undefined_ReturnsSharpTSUndefined()
    {
        var result = RuntimeValue.Undefined.ToObject();
        Assert.IsType<SharpTSUndefined>(result);
    }

    [Fact]
    public void ToObject_Null_ReturnsNull()
    {
        var result = RuntimeValue.Null.ToObject();
        Assert.Null(result);
    }

    [Fact]
    public void ToObject_Boolean_ReturnsBoxedBool()
    {
        var result = RuntimeValue.True.ToObject();
        Assert.IsType<bool>(result);
        Assert.True((bool)result!);
    }

    [Fact]
    public void ToObject_Number_ReturnsBoxedDouble()
    {
        var value = RuntimeValue.FromNumber(42.5);
        var result = value.ToObject();

        Assert.IsType<double>(result);
        Assert.Equal(42.5, (double)result!);
    }

    [Fact]
    public void ToObject_String_ReturnsString()
    {
        var value = RuntimeValue.FromString("test");
        var result = value.ToObject();

        Assert.IsType<string>(result);
        Assert.Equal("test", result);
    }

    [Fact]
    public void ToObject_Object_ReturnsSameObject()
    {
        var array = new SharpTSArray();
        var value = RuntimeValue.FromObject(array);
        var result = value.ToObject();

        Assert.Same(array, result);
    }

    #endregion

    #region IsTruthy

    [Fact]
    public void IsTruthy_Undefined_ReturnsFalse()
    {
        Assert.False(RuntimeValue.Undefined.IsTruthy());
    }

    [Fact]
    public void IsTruthy_Null_ReturnsFalse()
    {
        Assert.False(RuntimeValue.Null.IsTruthy());
    }

    [Fact]
    public void IsTruthy_False_ReturnsFalse()
    {
        Assert.False(RuntimeValue.False.IsTruthy());
    }

    [Fact]
    public void IsTruthy_True_ReturnsTrue()
    {
        Assert.True(RuntimeValue.True.IsTruthy());
    }

    [Fact]
    public void IsTruthy_Zero_ReturnsFalse()
    {
        Assert.False(RuntimeValue.Zero.IsTruthy());
    }

    [Fact]
    public void IsTruthy_NegativeZero_ReturnsFalse()
    {
        var negZero = RuntimeValue.FromNumber(-0.0);
        Assert.False(negZero.IsTruthy());
    }

    [Fact]
    public void IsTruthy_NaN_ReturnsFalse()
    {
        Assert.False(RuntimeValue.NaN.IsTruthy());
    }

    [Fact]
    public void IsTruthy_PositiveNumber_ReturnsTrue()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.True(value.IsTruthy());
    }

    [Fact]
    public void IsTruthy_NegativeNumber_ReturnsTrue()
    {
        var value = RuntimeValue.FromNumber(-42);
        Assert.True(value.IsTruthy());
    }

    [Fact]
    public void IsTruthy_EmptyString_ReturnsFalse()
    {
        Assert.False(RuntimeValue.EmptyString.IsTruthy());
    }

    [Fact]
    public void IsTruthy_NonEmptyString_ReturnsTrue()
    {
        var value = RuntimeValue.FromString("hello");
        Assert.True(value.IsTruthy());
    }

    [Fact]
    public void IsTruthy_Object_ReturnsTrue()
    {
        var value = RuntimeValue.FromObject(new SharpTSArray());
        Assert.True(value.IsTruthy());
    }

    [Fact]
    public void IsTruthy_Symbol_ReturnsTrue()
    {
        var value = RuntimeValue.FromSymbol(new SharpTSSymbol("test"));
        Assert.True(value.IsTruthy());
    }

    [Fact]
    public void IsTruthy_BigInt_Zero_ReturnsFalse()
    {
        var value = RuntimeValue.FromBigInt(BigInteger.Zero);
        Assert.False(value.IsTruthy());
    }

    [Fact]
    public void IsTruthy_BigInt_NonZero_ReturnsTrue()
    {
        var value = RuntimeValue.FromBigInt(BigInteger.One);
        Assert.True(value.IsTruthy());
    }

    #endregion

    #region TypeofString

    [Fact]
    public void TypeofString_Undefined_ReturnsUndefined()
    {
        Assert.Equal("undefined", RuntimeValue.Undefined.TypeofString());
    }

    [Fact]
    public void TypeofString_Null_ReturnsObject()
    {
        // JavaScript quirk: typeof null === "object"
        Assert.Equal("object", RuntimeValue.Null.TypeofString());
    }

    [Fact]
    public void TypeofString_Boolean_ReturnsBoolean()
    {
        Assert.Equal("boolean", RuntimeValue.True.TypeofString());
        Assert.Equal("boolean", RuntimeValue.False.TypeofString());
    }

    [Fact]
    public void TypeofString_Number_ReturnsNumber()
    {
        Assert.Equal("number", RuntimeValue.FromNumber(42).TypeofString());
        Assert.Equal("number", RuntimeValue.NaN.TypeofString());
    }

    [Fact]
    public void TypeofString_String_ReturnsString()
    {
        Assert.Equal("string", RuntimeValue.FromString("test").TypeofString());
    }

    [Fact]
    public void TypeofString_Symbol_ReturnsSymbol()
    {
        var value = RuntimeValue.FromSymbol(new SharpTSSymbol("test"));
        Assert.Equal("symbol", value.TypeofString());
    }

    [Fact]
    public void TypeofString_BigInt_ReturnsBigint()
    {
        var value = RuntimeValue.FromBigInt(BigInteger.One);
        Assert.Equal("bigint", value.TypeofString());
    }

    [Fact]
    public void TypeofString_Object_ReturnsObject()
    {
        var value = RuntimeValue.FromObject(new SharpTSArray());
        Assert.Equal("object", value.TypeofString());
    }

    #endregion

    #region Equality

    [Fact]
    public void StrictEquals_SameNumber_ReturnsTrue()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(42);
        Assert.True(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_DifferentNumbers_ReturnsFalse()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(43);
        Assert.False(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_NaN_ReturnsFalse()
    {
        // NaN !== NaN in JavaScript
        Assert.False(RuntimeValue.NaN.StrictEquals(RuntimeValue.NaN));
    }

    [Fact]
    public void StrictEquals_SameBoolean_ReturnsTrue()
    {
        Assert.True(RuntimeValue.True.StrictEquals(RuntimeValue.True));
        Assert.True(RuntimeValue.False.StrictEquals(RuntimeValue.False));
    }

    [Fact]
    public void StrictEquals_DifferentBooleans_ReturnsFalse()
    {
        Assert.False(RuntimeValue.True.StrictEquals(RuntimeValue.False));
    }

    [Fact]
    public void StrictEquals_SameString_ReturnsTrue()
    {
        var a = RuntimeValue.FromString("hello");
        var b = RuntimeValue.FromString("hello");
        Assert.True(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_DifferentStrings_ReturnsFalse()
    {
        var a = RuntimeValue.FromString("hello");
        var b = RuntimeValue.FromString("world");
        Assert.False(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_NullAndNull_ReturnsTrue()
    {
        Assert.True(RuntimeValue.Null.StrictEquals(RuntimeValue.Null));
    }

    [Fact]
    public void StrictEquals_UndefinedAndUndefined_ReturnsTrue()
    {
        Assert.True(RuntimeValue.Undefined.StrictEquals(RuntimeValue.Undefined));
    }

    [Fact]
    public void StrictEquals_NullAndUndefined_ReturnsFalse()
    {
        // null !== undefined in JavaScript
        Assert.False(RuntimeValue.Null.StrictEquals(RuntimeValue.Undefined));
    }

    [Fact]
    public void StrictEquals_DifferentKinds_ReturnsFalse()
    {
        var num = RuntimeValue.FromNumber(1);
        var str = RuntimeValue.FromString("1");
        Assert.False(num.StrictEquals(str));
    }

    [Fact]
    public void StrictEquals_SameObject_ReturnsTrue()
    {
        var array = new SharpTSArray();
        var a = RuntimeValue.FromObject(array);
        var b = RuntimeValue.FromObject(array);
        Assert.True(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_DifferentObjects_ReturnsFalse()
    {
        var a = RuntimeValue.FromObject(new SharpTSArray());
        var b = RuntimeValue.FromObject(new SharpTSArray());
        Assert.False(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_SameSymbol_ReturnsTrue()
    {
        var symbol = new SharpTSSymbol("test");
        var a = RuntimeValue.FromSymbol(symbol);
        var b = RuntimeValue.FromSymbol(symbol);
        Assert.True(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_DifferentSymbols_ReturnsFalse()
    {
        var a = RuntimeValue.FromSymbol(new SharpTSSymbol("test"));
        var b = RuntimeValue.FromSymbol(new SharpTSSymbol("test"));
        Assert.False(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_SameBigInt_ReturnsTrue()
    {
        var a = RuntimeValue.FromBigInt(BigInteger.Parse("12345"));
        var b = RuntimeValue.FromBigInt(BigInteger.Parse("12345"));
        Assert.True(a.StrictEquals(b));
    }

    [Fact]
    public void StrictEquals_DifferentBigInts_ReturnsFalse()
    {
        var a = RuntimeValue.FromBigInt(BigInteger.Parse("12345"));
        var b = RuntimeValue.FromBigInt(BigInteger.Parse("54321"));
        Assert.False(a.StrictEquals(b));
    }

    [Fact]
    public void LooseEquals_NullAndUndefined_ReturnsTrue()
    {
        // null == undefined in JavaScript
        Assert.True(RuntimeValue.Null.LooseEquals(RuntimeValue.Undefined));
        Assert.True(RuntimeValue.Undefined.LooseEquals(RuntimeValue.Null));
    }

    [Fact]
    public void LooseEquals_SameKind_UsesStrictEquality()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(42);
        Assert.True(a.LooseEquals(b));
    }

    #endregion

    #region GetHashCode

    [Fact]
    public void GetHashCode_SameValues_ReturnSameHash()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(42);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentValues_ReturnDifferentHash()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(43);
        // Not strictly required, but should generally be different
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_Null_ReturnsDeterministicHash()
    {
        Assert.Equal(RuntimeValue.Null.GetHashCode(), RuntimeValue.Null.GetHashCode());
    }

    [Fact]
    public void GetHashCode_Undefined_ReturnsDeterministicHash()
    {
        Assert.Equal(RuntimeValue.Undefined.GetHashCode(), RuntimeValue.Undefined.GetHashCode());
    }

    #endregion

    #region Operators

    [Fact]
    public void EqualityOperator_ReturnsSameAsStrictEquals()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(42);
        var c = RuntimeValue.FromNumber(43);

        Assert.True(a == b);
        Assert.False(a == c);
    }

    [Fact]
    public void InequalityOperator_ReturnsSameAsNotStrictEquals()
    {
        var a = RuntimeValue.FromNumber(42);
        var b = RuntimeValue.FromNumber(42);
        var c = RuntimeValue.FromNumber(43);

        Assert.False(a != b);
        Assert.True(a != c);
    }

    #endregion

    #region Implicit Conversions

    [Fact]
    public void ImplicitConversion_FromDouble()
    {
        RuntimeValue value = 42.5;
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.Equal(42.5, value.AsNumber());
    }

    [Fact]
    public void ImplicitConversion_FromBool()
    {
        RuntimeValue value = true;
        Assert.Equal(ValueKind.Boolean, value.Kind);
        Assert.True(value.AsBoolean());
    }

    [Fact]
    public void ImplicitConversion_FromString()
    {
        RuntimeValue value = "hello";
        Assert.Equal(ValueKind.String, value.Kind);
        Assert.Equal("hello", value.AsString());
    }

    [Fact]
    public void ImplicitConversion_FromInt()
    {
        RuntimeValue value = 42;
        Assert.Equal(ValueKind.Number, value.Kind);
        Assert.Equal(42.0, value.AsNumber());
    }

    [Fact]
    public void ImplicitConversion_FromNullString()
    {
        string? s = null;
        RuntimeValue value = s;
        Assert.Equal(ValueKind.Null, value.Kind);
    }

    #endregion

    #region ToString

    [Fact]
    public void ToString_Undefined_ReturnsUndefined()
    {
        Assert.Equal("undefined", RuntimeValue.Undefined.ToString());
    }

    [Fact]
    public void ToString_Null_ReturnsNull()
    {
        Assert.Equal("null", RuntimeValue.Null.ToString());
    }

    [Fact]
    public void ToString_True_ReturnsTrue()
    {
        Assert.Equal("true", RuntimeValue.True.ToString());
    }

    [Fact]
    public void ToString_False_ReturnsFalse()
    {
        Assert.Equal("false", RuntimeValue.False.ToString());
    }

    [Fact]
    public void ToString_Integer_ReturnsWithoutDecimal()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Equal("42", value.ToString());
    }

    [Fact]
    public void ToString_Float_ReturnsWithDecimal()
    {
        var value = RuntimeValue.FromNumber(42.5);
        Assert.Equal("42.5", value.ToString());
    }

    [Fact]
    public void ToString_NaN_ReturnsNaN()
    {
        Assert.Equal("NaN", RuntimeValue.NaN.ToString());
    }

    [Fact]
    public void ToString_PositiveInfinity_ReturnsInfinity()
    {
        var value = RuntimeValue.FromNumber(double.PositiveInfinity);
        Assert.Equal("Infinity", value.ToString());
    }

    [Fact]
    public void ToString_NegativeInfinity_ReturnsNegativeInfinity()
    {
        var value = RuntimeValue.FromNumber(double.NegativeInfinity);
        Assert.Equal("-Infinity", value.ToString());
    }

    [Fact]
    public void ToString_String_ReturnsString()
    {
        var value = RuntimeValue.FromString("hello");
        Assert.Equal("hello", value.ToString());
    }

    [Fact]
    public void ToString_BigInt_EndsWithN()
    {
        var value = RuntimeValue.FromBigInt(BigInteger.Parse("12345"));
        Assert.Equal("12345n", value.ToString());
    }

    #endregion

    #region Type Checking Properties

    [Fact]
    public void IsNullish_NullOrUndefined_ReturnsTrue()
    {
        Assert.True(RuntimeValue.Null.IsNullish);
        Assert.True(RuntimeValue.Undefined.IsNullish);
    }

    [Fact]
    public void IsNullish_OtherValues_ReturnsFalse()
    {
        Assert.False(RuntimeValue.Zero.IsNullish);
        Assert.False(RuntimeValue.False.IsNullish);
        Assert.False(RuntimeValue.EmptyString.IsNullish);
    }

    [Fact]
    public void IsNumber_Number_ReturnsTrue()
    {
        Assert.True(RuntimeValue.FromNumber(42).IsNumber);
    }

    [Fact]
    public void IsNumber_NonNumber_ReturnsFalse()
    {
        Assert.False(RuntimeValue.FromString("42").IsNumber);
    }

    [Fact]
    public void IsBoolean_Boolean_ReturnsTrue()
    {
        Assert.True(RuntimeValue.True.IsBoolean);
        Assert.True(RuntimeValue.False.IsBoolean);
    }

    [Fact]
    public void IsBoolean_NonBoolean_ReturnsFalse()
    {
        Assert.False(RuntimeValue.FromNumber(1).IsBoolean);
    }

    [Fact]
    public void IsString_String_ReturnsTrue()
    {
        Assert.True(RuntimeValue.FromString("test").IsString);
    }

    [Fact]
    public void IsString_NonString_ReturnsFalse()
    {
        Assert.False(RuntimeValue.FromNumber(42).IsString);
    }

    [Fact]
    public void IsObject_Object_ReturnsTrue()
    {
        Assert.True(RuntimeValue.FromObject(new SharpTSArray()).IsObject);
    }

    [Fact]
    public void IsObject_NonObject_ReturnsFalse()
    {
        Assert.False(RuntimeValue.FromNumber(42).IsObject);
    }

    [Fact]
    public void IsSymbol_Symbol_ReturnsTrue()
    {
        Assert.True(RuntimeValue.FromSymbol(new SharpTSSymbol("test")).IsSymbol);
    }

    [Fact]
    public void IsBigInt_BigInt_ReturnsTrue()
    {
        Assert.True(RuntimeValue.FromBigInt(BigInteger.One).IsBigInt);
    }

    #endregion

    #region GetObjectReference

    [Fact]
    public void GetObjectReference_ForObject_ReturnsReference()
    {
        var array = new SharpTSArray();
        var value = RuntimeValue.FromObject(array);
        Assert.Same(array, value.GetObjectReference());
    }

    [Fact]
    public void GetObjectReference_ForString_ReturnsReference()
    {
        var value = RuntimeValue.FromString("test");
        Assert.Equal("test", value.GetObjectReference());
    }

    [Fact]
    public void GetObjectReference_ForNumber_ReturnsNull()
    {
        var value = RuntimeValue.FromNumber(42);
        Assert.Null(value.GetObjectReference());
    }

    [Fact]
    public void GetObjectReference_ForBoolean_ReturnsNull()
    {
        var value = RuntimeValue.FromBoolean(true);
        Assert.Null(value.GetObjectReference());
    }

    #endregion
}
