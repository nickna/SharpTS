using System.Collections.ObjectModel;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional TypedArray types, storage, operations, and constructor/accessor registries.
/// Declarations support forward calls; completion validates and freezes all metadata.
/// </summary>
public sealed class EmittedTypedArrayImplementation
{
    internal EmittedTypedArrayImplementation()
    {
        GetUnboxedByElement = new ReadOnlyDictionary<string, MethodBuilder>(_getUnboxedByElement);
        SetUnboxedByElement = new ReadOnlyDictionary<string, MethodBuilder>(_setUnboxedByElement);
        FromObjectHelpers = new ReadOnlyDictionary<string, MethodBuilder>(_fromObjectHelpers);
        FromBufferHelpers = new ReadOnlyDictionary<string, MethodBuilder>(_fromBufferHelpers);
    }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _baseType;
    public TypeBuilder BaseType
    {
        get => Require(_baseType);
        internal set => Set(ref _baseType, value);
    }

    private ConstructorBuilder? _baseCtor;
    public ConstructorBuilder BaseCtor
    {
        get => Require(_baseCtor);
        internal set => Set(ref _baseCtor, value);
    }

    private MethodBuilder? _lengthGetter;
    public MethodBuilder LengthGetter
    {
        get => Require(_lengthGetter);
        internal set => Set(ref _lengthGetter, value);
    }

    private MethodBuilder? _byteOffsetGetter;
    public MethodBuilder ByteOffsetGetter
    {
        get => Require(_byteOffsetGetter);
        internal set => Set(ref _byteOffsetGetter, value);
    }

    private MethodBuilder? _byteLengthGetter;
    public MethodBuilder ByteLengthGetter
    {
        get => Require(_byteLengthGetter);
        internal set => Set(ref _byteLengthGetter, value);
    }

    private MethodBuilder? _bufferGetter;
    public MethodBuilder BufferGetter
    {
        get => Require(_bufferGetter);
        internal set => Set(ref _bufferGetter, value);
    }

    private MethodBuilder? _getBuffer;
    public MethodBuilder GetBuffer
    {
        get => Require(_getBuffer);
        internal set => Set(ref _getBuffer, value);
    }

    private MethodBuilder? _elementGet;
    public MethodBuilder ElementGet
    {
        get => Require(_elementGet);
        internal set => Set(ref _elementGet, value);
    }

    private MethodBuilder? _elementSet;
    public MethodBuilder ElementSet
    {
        get => Require(_elementSet);
        internal set => Set(ref _elementSet, value);
    }

    private MethodBuilder? _fill;
    public MethodBuilder Fill
    {
        get => Require(_fill);
        internal set => Set(ref _fill, value);
    }

    private MethodBuilder? _copyWithin;
    public MethodBuilder CopyWithin
    {
        get => Require(_copyWithin);
        internal set => Set(ref _copyWithin, value);
    }

    private MethodBuilder? _reverse;
    public MethodBuilder Reverse
    {
        get => Require(_reverse);
        internal set => Set(ref _reverse, value);
    }

    private MethodBuilder? _setFrom;
    public MethodBuilder SetFrom
    {
        get => Require(_setFrom);
        internal set => Set(ref _setFrom, value);
    }

    private MethodBuilder? _slice;
    public MethodBuilder Slice
    {
        get => Require(_slice);
        internal set => Set(ref _slice, value);
    }

    private MethodBuilder? _subarray;
    public MethodBuilder Subarray
    {
        get => Require(_subarray);
        internal set => Set(ref _subarray, value);
    }

    private MethodBuilder? _indexOf;
    public MethodBuilder IndexOf
    {
        get => Require(_indexOf);
        internal set => Set(ref _indexOf, value);
    }

    private MethodBuilder? _lastIndexOf;
    public MethodBuilder LastIndexOf
    {
        get => Require(_lastIndexOf);
        internal set => Set(ref _lastIndexOf, value);
    }

    private MethodBuilder? _includes;
    public MethodBuilder Includes
    {
        get => Require(_includes);
        internal set => Set(ref _includes, value);
    }

    private MethodBuilder? _join;
    public MethodBuilder Join
    {
        get => Require(_join);
        internal set => Set(ref _join, value);
    }

    private MethodBuilder? _toStringJoin;
    public MethodBuilder ToStringJoin
    {
        get => Require(_toStringJoin);
        internal set => Set(ref _toStringJoin, value);
    }

    private TypeBuilder? _boundMethodType;
    public TypeBuilder BoundMethodType
    {
        get => Require(_boundMethodType);
        internal set => Set(ref _boundMethodType, value);
    }

    private FieldBuilder? _boundMethodArrayField;
    public FieldBuilder BoundMethodArrayField
    {
        get => Require(_boundMethodArrayField);
        internal set => Set(ref _boundMethodArrayField, value);
    }

    private FieldBuilder? _boundMethodNameField;
    public FieldBuilder BoundMethodNameField
    {
        get => Require(_boundMethodNameField);
        internal set => Set(ref _boundMethodNameField, value);
    }

    private ConstructorBuilder? _boundMethodCtor;
    public ConstructorBuilder BoundMethodCtor
    {
        get => Require(_boundMethodCtor);
        internal set => Set(ref _boundMethodCtor, value);
    }

    private MethodBuilder? _boundMethodInvoke;
    public MethodBuilder BoundMethodInvoke
    {
        get => Require(_boundMethodInvoke);
        internal set => Set(ref _boundMethodInvoke, value);
    }

    private TypeBuilder? _int8ArrayType;
    public TypeBuilder Int8ArrayType
    {
        get => Require(_int8ArrayType);
        internal set => Set(ref _int8ArrayType, value);
    }

    private ConstructorBuilder? _int8ArrayLengthCtor;
    public ConstructorBuilder Int8ArrayLengthCtor
    {
        get => Require(_int8ArrayLengthCtor);
        internal set => Set(ref _int8ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _int8ArrayBufferCtor;
    public ConstructorBuilder Int8ArrayBufferCtor
    {
        get => Require(_int8ArrayBufferCtor);
        internal set => Set(ref _int8ArrayBufferCtor, value);
    }

    private TypeBuilder? _uint8ArrayType;
    public TypeBuilder Uint8ArrayType
    {
        get => Require(_uint8ArrayType);
        internal set => Set(ref _uint8ArrayType, value);
    }

    private ConstructorBuilder? _uint8ArrayLengthCtor;
    public ConstructorBuilder Uint8ArrayLengthCtor
    {
        get => Require(_uint8ArrayLengthCtor);
        internal set => Set(ref _uint8ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _uint8ArrayBufferCtor;
    public ConstructorBuilder Uint8ArrayBufferCtor
    {
        get => Require(_uint8ArrayBufferCtor);
        internal set => Set(ref _uint8ArrayBufferCtor, value);
    }

    private TypeBuilder? _uint8ClampedArrayType;
    public TypeBuilder Uint8ClampedArrayType
    {
        get => Require(_uint8ClampedArrayType);
        internal set => Set(ref _uint8ClampedArrayType, value);
    }

    private ConstructorBuilder? _uint8ClampedArrayLengthCtor;
    public ConstructorBuilder Uint8ClampedArrayLengthCtor
    {
        get => Require(_uint8ClampedArrayLengthCtor);
        internal set => Set(ref _uint8ClampedArrayLengthCtor, value);
    }

    private ConstructorBuilder? _uint8ClampedArrayBufferCtor;
    public ConstructorBuilder Uint8ClampedArrayBufferCtor
    {
        get => Require(_uint8ClampedArrayBufferCtor);
        internal set => Set(ref _uint8ClampedArrayBufferCtor, value);
    }

    private TypeBuilder? _int16ArrayType;
    public TypeBuilder Int16ArrayType
    {
        get => Require(_int16ArrayType);
        internal set => Set(ref _int16ArrayType, value);
    }

    private ConstructorBuilder? _int16ArrayLengthCtor;
    public ConstructorBuilder Int16ArrayLengthCtor
    {
        get => Require(_int16ArrayLengthCtor);
        internal set => Set(ref _int16ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _int16ArrayBufferCtor;
    public ConstructorBuilder Int16ArrayBufferCtor
    {
        get => Require(_int16ArrayBufferCtor);
        internal set => Set(ref _int16ArrayBufferCtor, value);
    }

    private TypeBuilder? _uint16ArrayType;
    public TypeBuilder Uint16ArrayType
    {
        get => Require(_uint16ArrayType);
        internal set => Set(ref _uint16ArrayType, value);
    }

    private ConstructorBuilder? _uint16ArrayLengthCtor;
    public ConstructorBuilder Uint16ArrayLengthCtor
    {
        get => Require(_uint16ArrayLengthCtor);
        internal set => Set(ref _uint16ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _uint16ArrayBufferCtor;
    public ConstructorBuilder Uint16ArrayBufferCtor
    {
        get => Require(_uint16ArrayBufferCtor);
        internal set => Set(ref _uint16ArrayBufferCtor, value);
    }

    private TypeBuilder? _int32ArrayType;
    public TypeBuilder Int32ArrayType
    {
        get => Require(_int32ArrayType);
        internal set => Set(ref _int32ArrayType, value);
    }

    private ConstructorBuilder? _int32ArrayLengthCtor;
    public ConstructorBuilder Int32ArrayLengthCtor
    {
        get => Require(_int32ArrayLengthCtor);
        internal set => Set(ref _int32ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _int32ArrayBufferCtor;
    public ConstructorBuilder Int32ArrayBufferCtor
    {
        get => Require(_int32ArrayBufferCtor);
        internal set => Set(ref _int32ArrayBufferCtor, value);
    }

    private TypeBuilder? _uint32ArrayType;
    public TypeBuilder Uint32ArrayType
    {
        get => Require(_uint32ArrayType);
        internal set => Set(ref _uint32ArrayType, value);
    }

    private ConstructorBuilder? _uint32ArrayLengthCtor;
    public ConstructorBuilder Uint32ArrayLengthCtor
    {
        get => Require(_uint32ArrayLengthCtor);
        internal set => Set(ref _uint32ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _uint32ArrayBufferCtor;
    public ConstructorBuilder Uint32ArrayBufferCtor
    {
        get => Require(_uint32ArrayBufferCtor);
        internal set => Set(ref _uint32ArrayBufferCtor, value);
    }

    private TypeBuilder? _float32ArrayType;
    public TypeBuilder Float32ArrayType
    {
        get => Require(_float32ArrayType);
        internal set => Set(ref _float32ArrayType, value);
    }

    private ConstructorBuilder? _float32ArrayLengthCtor;
    public ConstructorBuilder Float32ArrayLengthCtor
    {
        get => Require(_float32ArrayLengthCtor);
        internal set => Set(ref _float32ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _float32ArrayBufferCtor;
    public ConstructorBuilder Float32ArrayBufferCtor
    {
        get => Require(_float32ArrayBufferCtor);
        internal set => Set(ref _float32ArrayBufferCtor, value);
    }

    private TypeBuilder? _float64ArrayType;
    public TypeBuilder Float64ArrayType
    {
        get => Require(_float64ArrayType);
        internal set => Set(ref _float64ArrayType, value);
    }

    private ConstructorBuilder? _float64ArrayLengthCtor;
    public ConstructorBuilder Float64ArrayLengthCtor
    {
        get => Require(_float64ArrayLengthCtor);
        internal set => Set(ref _float64ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _float64ArrayBufferCtor;
    public ConstructorBuilder Float64ArrayBufferCtor
    {
        get => Require(_float64ArrayBufferCtor);
        internal set => Set(ref _float64ArrayBufferCtor, value);
    }

    private TypeBuilder? _bigInt64ArrayType;
    public TypeBuilder BigInt64ArrayType
    {
        get => Require(_bigInt64ArrayType);
        internal set => Set(ref _bigInt64ArrayType, value);
    }

    private ConstructorBuilder? _bigInt64ArrayLengthCtor;
    public ConstructorBuilder BigInt64ArrayLengthCtor
    {
        get => Require(_bigInt64ArrayLengthCtor);
        internal set => Set(ref _bigInt64ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _bigInt64ArrayBufferCtor;
    public ConstructorBuilder BigInt64ArrayBufferCtor
    {
        get => Require(_bigInt64ArrayBufferCtor);
        internal set => Set(ref _bigInt64ArrayBufferCtor, value);
    }

    private TypeBuilder? _bigUint64ArrayType;
    public TypeBuilder BigUint64ArrayType
    {
        get => Require(_bigUint64ArrayType);
        internal set => Set(ref _bigUint64ArrayType, value);
    }

    private ConstructorBuilder? _bigUint64ArrayLengthCtor;
    public ConstructorBuilder BigUint64ArrayLengthCtor
    {
        get => Require(_bigUint64ArrayLengthCtor);
        internal set => Set(ref _bigUint64ArrayLengthCtor, value);
    }

    private ConstructorBuilder? _bigUint64ArrayBufferCtor;
    public ConstructorBuilder BigUint64ArrayBufferCtor
    {
        get => Require(_bigUint64ArrayBufferCtor);
        internal set => Set(ref _bigUint64ArrayBufferCtor, value);
    }

    private MethodBuilder? _getElement;
    public MethodBuilder GetElement
    {
        get => Require(_getElement);
        internal set => Set(ref _getElement, value);
    }

    private MethodBuilder? _setElement;
    public MethodBuilder SetElement
    {
        get => Require(_setElement);
        internal set => Set(ref _setElement, value);
    }

    private MethodBuilder? _getMember;
    public MethodBuilder GetMember
    {
        get => Require(_getMember);
        internal set => Set(ref _getMember, value);
    }

    private FieldBuilder? _bufferField;
    public FieldBuilder BufferField
    {
        get => Require(_bufferField);
        internal set => Set(ref _bufferField, value);
    }

    private FieldBuilder? _byteOffsetField;
    public FieldBuilder ByteOffsetField
    {
        get => Require(_byteOffsetField);
        internal set => Set(ref _byteOffsetField, value);
    }

    private FieldBuilder? _lengthField;
    public FieldBuilder LengthField
    {
        get => Require(_lengthField);
        internal set => Set(ref _lengthField, value);
    }

    private FieldBuilder? _arrayBufferField;
    public FieldBuilder ArrayBufferField
    {
        get => Require(_arrayBufferField);
        internal set => Set(ref _arrayBufferField, value);
    }

    private MethodBuilder? _bytesPerElementGetter;
    public MethodBuilder BytesPerElementGetter
    {
        get => Require(_bytesPerElementGetter);
        internal set => Set(ref _bytesPerElementGetter, value);
    }

    private MethodBuilder? _createOfLength;
    public MethodBuilder CreateOfLength
    {
        get => Require(_createOfLength);
        internal set => Set(ref _createOfLength, value);
    }

    private MethodBuilder? _createView;
    public MethodBuilder CreateView
    {
        get => Require(_createView);
        internal set => Set(ref _createView, value);
    }

    // Clamped and BigInt kinds deliberately retain the boxed element path.
    private static readonly string[] NumericElements =
        ["Int8", "Uint8", "Int16", "Uint16", "Int32", "Uint32", "Float32", "Float64"];
    private static readonly string[] ConcreteNames =
        ["Int8Array", "Uint8Array", "Uint8ClampedArray", "Int16Array", "Uint16Array",
         "Int32Array", "Uint32Array", "Float32Array", "Float64Array", "BigInt64Array", "BigUint64Array"];

    private readonly Dictionary<string, MethodBuilder> _getUnboxedByElement = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> GetUnboxedByElement { get; }

    internal void RegisterGetUnboxed(string name, MethodBuilder method) =>
        Register(_getUnboxedByElement, NumericElements, name, method);

    private readonly Dictionary<string, MethodBuilder> _setUnboxedByElement = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> SetUnboxedByElement { get; }

    internal void RegisterSetUnboxed(string name, MethodBuilder method) =>
        Register(_setUnboxedByElement, NumericElements, name, method);

    private readonly Dictionary<string, MethodBuilder> _fromObjectHelpers = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> FromObjectHelpers { get; }

    internal void RegisterFromObject(string name, MethodBuilder method) =>
        Register(_fromObjectHelpers, ConcreteNames, name, method);

    private readonly Dictionary<string, MethodBuilder> _fromBufferHelpers = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> FromBufferHelpers { get; }

    internal void RegisterFromBuffer(string name, MethodBuilder method) =>
        Register(_fromBufferHelpers, ConcreteNames, name, method);

    private void Register(Dictionary<string, MethodBuilder> registry, string[] allowedNames, string name, MethodBuilder method)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(method);
        if (!allowedNames.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException($"Unsupported TypedArray metadata key '{name}'.", nameof(name));
        registry.Add(name, method);
    }

    /// <summary>Numeric concrete type, or null for BigInt/unknown kinds that use a fallback.</summary>
    public Type? GetNumericType(string elementType) => elementType switch
    {
        "Int8" => Int8ArrayType,
        "Uint8" => Uint8ArrayType,
        "Uint8Clamped" => Uint8ClampedArrayType,
        "Int16" => Int16ArrayType,
        "Uint16" => Uint16ArrayType,
        "Int32" => Int32ArrayType,
        "Uint32" => Uint32ArrayType,
        "Float32" => Float32ArrayType,
        "Float64" => Float64ArrayType,
        _ => null
    };

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"TypedArray metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("TypedArray implementation emission is already complete.");
    }

    private static void RequireEntries(IReadOnlyDictionary<string, MethodBuilder> registry, string[] requiredNames, string registryName)
    {
        foreach (string name in requiredNames)
        {
            if (!registry.ContainsKey(name))
                throw new InvalidOperationException($"TypedArray metadata '{registryName}.{name}' has not been declared.");
        }
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = BaseType;
        _ = BaseCtor;
        _ = LengthGetter;
        _ = ByteOffsetGetter;
        _ = ByteLengthGetter;
        _ = BufferGetter;
        _ = GetBuffer;
        _ = ElementGet;
        _ = ElementSet;
        _ = Fill;
        _ = CopyWithin;
        _ = Reverse;
        _ = SetFrom;
        _ = Slice;
        _ = Subarray;
        _ = IndexOf;
        _ = LastIndexOf;
        _ = Includes;
        _ = Join;
        _ = ToStringJoin;
        _ = BoundMethodType;
        _ = BoundMethodArrayField;
        _ = BoundMethodNameField;
        _ = BoundMethodCtor;
        _ = BoundMethodInvoke;
        _ = Int8ArrayType;
        _ = Int8ArrayLengthCtor;
        _ = Int8ArrayBufferCtor;
        _ = Uint8ArrayType;
        _ = Uint8ArrayLengthCtor;
        _ = Uint8ArrayBufferCtor;
        _ = Uint8ClampedArrayType;
        _ = Uint8ClampedArrayLengthCtor;
        _ = Uint8ClampedArrayBufferCtor;
        _ = Int16ArrayType;
        _ = Int16ArrayLengthCtor;
        _ = Int16ArrayBufferCtor;
        _ = Uint16ArrayType;
        _ = Uint16ArrayLengthCtor;
        _ = Uint16ArrayBufferCtor;
        _ = Int32ArrayType;
        _ = Int32ArrayLengthCtor;
        _ = Int32ArrayBufferCtor;
        _ = Uint32ArrayType;
        _ = Uint32ArrayLengthCtor;
        _ = Uint32ArrayBufferCtor;
        _ = Float32ArrayType;
        _ = Float32ArrayLengthCtor;
        _ = Float32ArrayBufferCtor;
        _ = Float64ArrayType;
        _ = Float64ArrayLengthCtor;
        _ = Float64ArrayBufferCtor;
        _ = BigInt64ArrayType;
        _ = BigInt64ArrayLengthCtor;
        _ = BigInt64ArrayBufferCtor;
        _ = BigUint64ArrayType;
        _ = BigUint64ArrayLengthCtor;
        _ = BigUint64ArrayBufferCtor;
        _ = GetElement;
        _ = SetElement;
        _ = GetMember;
        _ = BufferField;
        _ = ByteOffsetField;
        _ = LengthField;
        _ = ArrayBufferField;
        _ = BytesPerElementGetter;
        _ = CreateOfLength;
        _ = CreateView;
        RequireEntries(GetUnboxedByElement, NumericElements, nameof(GetUnboxedByElement));
        RequireEntries(SetUnboxedByElement, NumericElements, nameof(SetUnboxedByElement));
        RequireEntries(FromObjectHelpers, ConcreteNames, nameof(FromObjectHelpers));
        RequireEntries(FromBufferHelpers, ConcreteNames, nameof(FromBufferHelpers));
        IsComplete = true;
    }
}
