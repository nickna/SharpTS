using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>JSON parse, stringify, raw-value and cached helper declarations for one compilation.</summary>
public sealed class EmittedJsonImplementation
{
    internal EmittedJsonImplementation() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _parse;
    public MethodBuilder Parse
    {
        get => Require(_parse);
        internal set => SetHandle(ref _parse, value);
    }

    private MethodBuilder? _parseWithReviver;
    public MethodBuilder ParseWithReviver
    {
        get => Require(_parseWithReviver);
        internal set => SetHandle(ref _parseWithReviver, value);
    }

    private MethodBuilder? _stringify;
    public MethodBuilder Stringify
    {
        get => Require(_stringify);
        internal set => SetHandle(ref _stringify, value);
    }

    private MethodBuilder? _stringifyShaped;
    public MethodBuilder StringifyShaped
    {
        get => Require(_stringifyShaped);
        internal set => SetHandle(ref _stringifyShaped, value);
    }

    private MethodBuilder? _stringifyFull;
    public MethodBuilder StringifyFull
    {
        get => Require(_stringifyFull);
        internal set => SetHandle(ref _stringifyFull, value);
    }

    private TypeBuilder? _rawJsonType;
    public TypeBuilder RawJsonType
    {
        get => Require(_rawJsonType);
        internal set => SetHandle(ref _rawJsonType, value);
    }

    private ConstructorBuilder? _rawJsonConstructor;
    public ConstructorBuilder RawJsonConstructor
    {
        get => Require(_rawJsonConstructor);
        internal set => SetHandle(ref _rawJsonConstructor, value);
    }

    private MethodBuilder? _rawJsonTextGetter;
    public MethodBuilder RawJsonTextGetter
    {
        get => Require(_rawJsonTextGetter);
        internal set => SetHandle(ref _rawJsonTextGetter, value);
    }

    private MethodBuilder? _rawJson;
    public MethodBuilder RawJson
    {
        get => Require(_rawJson);
        internal set => SetHandle(ref _rawJson, value);
    }

    private MethodBuilder? _isRawJson;
    public MethodBuilder IsRawJson
    {
        get => Require(_isRawJson);
        internal set => SetHandle(ref _isRawJson, value);
    }

    private MethodBuilder? _appendValue;
    public MethodBuilder AppendValue
    {
        get => Require(_appendValue);
        internal set => SetHandle(ref _appendValue, value);
    }

    private MethodBuilder? _appendNumber;
    public MethodBuilder AppendNumber
    {
        get => Require(_appendNumber);
        internal set => SetHandle(ref _appendNumber, value);
    }

    private MethodBuilder? _canUseShape;
    public MethodBuilder CanUseShape
    {
        get => Require(_canUseShape);
        internal set => SetHandle(ref _canUseShape, value);
    }

    private MethodBuilder? _shapePrototypesSafe;
    public MethodBuilder ShapePrototypesSafe
    {
        get => Require(_shapePrototypesSafe);
        internal set => SetHandle(ref _shapePrototypesSafe, value);
    }

    private MethodBuilder? _appendShapedValue;
    public MethodBuilder AppendShapedValue
    {
        get => Require(_appendShapedValue);
        internal set => SetHandle(ref _appendShapedValue, value);
    }

    private MethodBuilder? _registerShape;
    public MethodBuilder RegisterShape
    {
        get => Require(_registerShape);
        internal set => SetHandle(ref _registerShape, value);
    }

    private MethodBuilder? _tryGetShape;
    public MethodBuilder TryGetShape
    {
        get => Require(_tryGetShape);
        internal set => SetHandle(ref _tryGetShape, value);
    }

    private MethodBuilder? _escapeString;
    public MethodBuilder EscapeString
    {
        get => Require(_escapeString);
        internal set => SetHandle(ref _escapeString, value);
    }

    private MethodBuilder? _appendEscapedString;
    public MethodBuilder AppendEscapedString
    {
        get => Require(_appendEscapedString);
        internal set => SetHandle(ref _appendEscapedString, value);
    }

    private MethodBuilder? _getDictionaryProperty;
    public MethodBuilder GetDictionaryProperty
    {
        get => Require(_getDictionaryProperty);
        internal set => SetHandle(ref _getDictionaryProperty, value);
    }

    private MethodBuilder? _getDictionaryToJson;
    public MethodBuilder GetDictionaryToJson
    {
        get => Require(_getDictionaryToJson);
        internal set => SetHandle(ref _getDictionaryToJson, value);
    }

    private MethodBuilder? _tryRentDictionaryKeys;
    public MethodBuilder TryRentDictionaryKeys
    {
        get => Require(_tryRentDictionaryKeys);
        internal set => SetHandle(ref _tryRentDictionaryKeys, value);
    }

    private MethodBuilder? _returnDictionaryKeys;
    public MethodBuilder ReturnDictionaryKeys
    {
        get => Require(_returnDictionaryKeys);
        internal set => SetHandle(ref _returnDictionaryKeys, value);
    }

    private MethodBuilder? _rentStringBuilder;
    public MethodBuilder RentStringBuilder
    {
        get => Require(_rentStringBuilder);
        internal set => SetHandle(ref _rentStringBuilder, value);
    }

    private MethodBuilder? _returnStringBuilder;
    public MethodBuilder ReturnStringBuilder
    {
        get => Require(_returnStringBuilder);
        internal set => SetHandle(ref _returnStringBuilder, value);
    }

    // Forward references and recursive helper emission can reuse a declaration
    // before its body is complete. Availability stays internal to emission.
    internal bool IsAppendValueDeclared => _appendValue is not null;
    internal bool IsAppendNumberDeclared => _appendNumber is not null;
    internal bool IsCanUseShapeDeclared => _canUseShape is not null;
    internal bool IsShapePrototypesSafeDeclared => _shapePrototypesSafe is not null;
    internal bool IsAppendShapedValueDeclared => _appendShapedValue is not null;
    internal bool IsRegisterShapeDeclared => _registerShape is not null;
    internal bool IsTryGetShapeDeclared => _tryGetShape is not null;
    internal bool IsEscapeStringDeclared => _escapeString is not null;
    internal bool IsAppendEscapedStringDeclared => _appendEscapedString is not null;
    internal bool IsGetDictionaryPropertyDeclared => _getDictionaryProperty is not null;
    internal bool IsGetDictionaryToJsonDeclared => _getDictionaryToJson is not null;
    internal bool IsTryRentDictionaryKeysDeclared => _tryRentDictionaryKeys is not null;
    internal bool IsRentStringBuilderDeclared => _rentStringBuilder is not null;

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"JSON implementation metadata '{name}' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("JSON implementation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Parse;
        _ = ParseWithReviver;
        _ = Stringify;
        _ = StringifyShaped;
        _ = StringifyFull;
        _ = RawJsonType;
        _ = RawJsonConstructor;
        _ = RawJsonTextGetter;
        _ = RawJson;
        _ = IsRawJson;
        _ = AppendValue;
        _ = AppendNumber;
        _ = CanUseShape;
        _ = ShapePrototypesSafe;
        _ = AppendShapedValue;
        _ = RegisterShape;
        _ = TryGetShape;
        _ = EscapeString;
        _ = AppendEscapedString;
        _ = GetDictionaryProperty;
        _ = GetDictionaryToJson;
        _ = TryRentDictionaryKeys;
        _ = ReturnDictionaryKeys;
        _ = RentStringBuilder;
        _ = ReturnStringBuilder;
        IsComplete = true;
    }
}
