using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required core string and prototype declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedStringRuntime
{
    internal EmittedStringRuntime() { }

    public bool IsComplete { get; private set; }

    // Prototype storage and its populate shell precede the operation and population bodies.
    private MethodBuilder? _charAt;
    public MethodBuilder CharAt
    {
        get => Require(_charAt);
        internal set => Set(ref _charAt, value);
    }

    private MethodBuilder? _substring;
    public MethodBuilder Substring
    {
        get => Require(_substring);
        internal set => Set(ref _substring, value);
    }

    private MethodBuilder? _substr;
    public MethodBuilder Substr
    {
        get => Require(_substr);
        internal set => Set(ref _substr, value);
    }

    private MethodBuilder? _indexOf;
    public MethodBuilder IndexOf
    {
        get => Require(_indexOf);
        internal set => Set(ref _indexOf, value);
    }

    private MethodBuilder? _indexOfFrom;
    public MethodBuilder IndexOfFrom
    {
        get => Require(_indexOfFrom);
        internal set => Set(ref _indexOfFrom, value);
    }

    private MethodBuilder? _indexOfPrimitive;
    public MethodBuilder IndexOfPrimitive
    {
        get => Require(_indexOfPrimitive);
        internal set => Set(ref _indexOfPrimitive, value);
    }

    private MethodBuilder? _includesPrimitive;
    public MethodBuilder IncludesPrimitive
    {
        get => Require(_includesPrimitive);
        internal set => Set(ref _includesPrimitive, value);
    }

    private MethodBuilder? _slicePrimitive;
    public MethodBuilder SlicePrimitive
    {
        get => Require(_slicePrimitive);
        internal set => Set(ref _slicePrimitive, value);
    }

    private MethodBuilder? _substringPrimitive;
    public MethodBuilder SubstringPrimitive
    {
        get => Require(_substringPrimitive);
        internal set => Set(ref _substringPrimitive, value);
    }

    private MethodBuilder? _sliceFromLengthPrimitive;
    public MethodBuilder SliceFromLengthPrimitive
    {
        get => Require(_sliceFromLengthPrimitive);
        internal set => Set(ref _sliceFromLengthPrimitive, value);
    }

    private MethodBuilder? _sliceLengthPrimitive;
    public MethodBuilder SliceLengthPrimitive
    {
        get => Require(_sliceLengthPrimitive);
        internal set => Set(ref _sliceLengthPrimitive, value);
    }

    private MethodBuilder? _substringFromLengthPrimitive;
    public MethodBuilder SubstringFromLengthPrimitive
    {
        get => Require(_substringFromLengthPrimitive);
        internal set => Set(ref _substringFromLengthPrimitive, value);
    }

    private MethodBuilder? _substringLengthPrimitive;
    public MethodBuilder SubstringLengthPrimitive
    {
        get => Require(_substringLengthPrimitive);
        internal set => Set(ref _substringLengthPrimitive, value);
    }

    private MethodBuilder? _toUpperCase;
    public MethodBuilder ToUpperCase
    {
        get => Require(_toUpperCase);
        internal set => Set(ref _toUpperCase, value);
    }

    private MethodBuilder? _toLowerCase;
    public MethodBuilder ToLowerCase
    {
        get => Require(_toLowerCase);
        internal set => Set(ref _toLowerCase, value);
    }

    private MethodBuilder? _trim;
    public MethodBuilder Trim
    {
        get => Require(_trim);
        internal set => Set(ref _trim, value);
    }

    private MethodBuilder? _replace;
    public MethodBuilder Replace
    {
        get => Require(_replace);
        internal set => Set(ref _replace, value);
    }

    private MethodBuilder? _includes;
    public MethodBuilder Includes
    {
        get => Require(_includes);
        internal set => Set(ref _includes, value);
    }

    private MethodBuilder? _startsWith;
    public MethodBuilder StartsWith
    {
        get => Require(_startsWith);
        internal set => Set(ref _startsWith, value);
    }

    private MethodBuilder? _endsWith;
    public MethodBuilder EndsWith
    {
        get => Require(_endsWith);
        internal set => Set(ref _endsWith, value);
    }

    private MethodBuilder? _slice;
    public MethodBuilder Slice
    {
        get => Require(_slice);
        internal set => Set(ref _slice, value);
    }

    private MethodBuilder? _repeat;
    public MethodBuilder Repeat
    {
        get => Require(_repeat);
        internal set => Set(ref _repeat, value);
    }

    private MethodBuilder? _padStart;
    public MethodBuilder PadStart
    {
        get => Require(_padStart);
        internal set => Set(ref _padStart, value);
    }

    private MethodBuilder? _padEnd;
    public MethodBuilder PadEnd
    {
        get => Require(_padEnd);
        internal set => Set(ref _padEnd, value);
    }

    private MethodBuilder? _charCodeAt;
    public MethodBuilder CharCodeAt
    {
        get => Require(_charCodeAt);
        internal set => Set(ref _charCodeAt, value);
    }

    private MethodBuilder? _concat;
    public MethodBuilder Concat
    {
        get => Require(_concat);
        internal set => Set(ref _concat, value);
    }

    private MethodBuilder? _lastIndexOf;
    public MethodBuilder LastIndexOf
    {
        get => Require(_lastIndexOf);
        internal set => Set(ref _lastIndexOf, value);
    }

    private MethodBuilder? _trimStart;
    public MethodBuilder TrimStart
    {
        get => Require(_trimStart);
        internal set => Set(ref _trimStart, value);
    }

    private MethodBuilder? _trimEnd;
    public MethodBuilder TrimEnd
    {
        get => Require(_trimEnd);
        internal set => Set(ref _trimEnd, value);
    }

    private MethodBuilder? _trimInline;
    public MethodBuilder TrimInline
    {
        get => Require(_trimInline);
        internal set => Set(ref _trimInline, value);
    }

    private MethodBuilder? _replaceAll;
    public MethodBuilder ReplaceAll
    {
        get => Require(_replaceAll);
        internal set => Set(ref _replaceAll, value);
    }

    private MethodBuilder? _at;
    public MethodBuilder At
    {
        get => Require(_at);
        internal set => Set(ref _at, value);
    }

    private MethodBuilder? _fromCharCode;
    public MethodBuilder FromCharCode
    {
        get => Require(_fromCharCode);
        internal set => Set(ref _fromCharCode, value);
    }

    private MethodBuilder? _codePointAt;
    public MethodBuilder CodePointAt
    {
        get => Require(_codePointAt);
        internal set => Set(ref _codePointAt, value);
    }

    private MethodBuilder? _isWellFormed;
    public MethodBuilder IsWellFormed
    {
        get => Require(_isWellFormed);
        internal set => Set(ref _isWellFormed, value);
    }

    private MethodBuilder? _toWellFormed;
    public MethodBuilder ToWellFormed
    {
        get => Require(_toWellFormed);
        internal set => Set(ref _toWellFormed, value);
    }

    private MethodBuilder? _iterator;
    public MethodBuilder Iterator
    {
        get => Require(_iterator);
        internal set => Set(ref _iterator, value);
    }

    private MethodBuilder? _fromCodePoint;
    public MethodBuilder FromCodePoint
    {
        get => Require(_fromCodePoint);
        internal set => Set(ref _fromCodePoint, value);
    }

    private MethodBuilder? _normalize;
    public MethodBuilder Normalize
    {
        get => Require(_normalize);
        internal set => Set(ref _normalize, value);
    }

    private MethodBuilder? _localeCompare;
    public MethodBuilder LocaleCompare
    {
        get => Require(_localeCompare);
        internal set => Set(ref _localeCompare, value);
    }

    // Shared string symbol dispatch is independent of the optional RegExp implementation.
    private MethodBuilder? _tryInvokeSymbolMethod;
    public MethodBuilder TryInvokeSymbolMethod
    {
        get => Require(_tryInvokeSymbolMethod);
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            if (_tryInvokeSymbolMethod is not null)
                throw new InvalidOperationException("String symbol-dispatch metadata has already been declared.");
            _tryInvokeSymbolMethod = value;
        }
    }

    private FieldBuilder? _prototypeField;
    public FieldBuilder PrototypeField
    {
        get => Require(_prototypeField);
        internal set => Set(ref _prototypeField, value);
    }

    private MethodBuilder? _prototypePopulateMethod;
    public MethodBuilder PrototypePopulateMethod
    {
        get => Require(_prototypePopulateMethod);
        internal set => Set(ref _prototypePopulateMethod, value);
    }

    private MethodBuilder? _prototypeGenericStub;
    public MethodBuilder PrototypeGenericStub
    {
        get => Require(_prototypeGenericStub);
        internal set => Set(ref _prototypeGenericStub, value);
    }

    private MethodBuilder? _protoToStringHelper;
    public MethodBuilder ProtoToStringHelper
    {
        get => Require(_protoToStringHelper);
        internal set => Set(ref _protoToStringHelper, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"string metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("string metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = CharAt;
        _ = Substring;
        _ = Substr;
        _ = IndexOf;
        _ = IndexOfFrom;
        _ = IndexOfPrimitive;
        _ = IncludesPrimitive;
        _ = SlicePrimitive;
        _ = SubstringPrimitive;
        _ = SliceFromLengthPrimitive;
        _ = SliceLengthPrimitive;
        _ = SubstringFromLengthPrimitive;
        _ = SubstringLengthPrimitive;
        _ = ToUpperCase;
        _ = ToLowerCase;
        _ = Trim;
        _ = Replace;
        _ = Includes;
        _ = StartsWith;
        _ = EndsWith;
        _ = Slice;
        _ = Repeat;
        _ = PadStart;
        _ = PadEnd;
        _ = CharCodeAt;
        _ = Concat;
        _ = LastIndexOf;
        _ = TrimStart;
        _ = TrimEnd;
        _ = TrimInline;
        _ = ReplaceAll;
        _ = At;
        _ = FromCharCode;
        _ = CodePointAt;
        _ = IsWellFormed;
        _ = ToWellFormed;
        _ = Iterator;
        _ = FromCodePoint;
        _ = Normalize;
        _ = LocaleCompare;
        _ = TryInvokeSymbolMethod;
        _ = PrototypeField;
        _ = PrototypePopulateMethod;
        _ = PrototypeGenericStub;
        _ = ProtoToStringHelper;
        IsComplete = true;
    }
}
