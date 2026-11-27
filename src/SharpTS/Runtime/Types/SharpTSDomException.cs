using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

internal sealed class SharpTSDomException : SharpTSError
{
    internal const string TypeName = "DOMException";
    internal const string DefaultName = "Error";

    // Indices plus one are the Web IDL legacy codes. Obsolete constants
    // still exist, but their former names do not produce a nonzero code.
    internal static readonly (string Constant, string? Name)[] LegacyCodes =
    [
        ("INDEX_SIZE_ERR", "IndexSizeError"),
        ("DOMSTRING_SIZE_ERR", null),
        ("HIERARCHY_REQUEST_ERR", "HierarchyRequestError"),
        ("WRONG_DOCUMENT_ERR", "WrongDocumentError"),
        ("INVALID_CHARACTER_ERR", "InvalidCharacterError"),
        ("NO_DATA_ALLOWED_ERR", null),
        ("NO_MODIFICATION_ALLOWED_ERR", "NoModificationAllowedError"),
        ("NOT_FOUND_ERR", "NotFoundError"),
        ("NOT_SUPPORTED_ERR", "NotSupportedError"),
        ("INUSE_ATTRIBUTE_ERR", "InUseAttributeError"),
        ("INVALID_STATE_ERR", "InvalidStateError"),
        ("SYNTAX_ERR", "SyntaxError"),
        ("INVALID_MODIFICATION_ERR", "InvalidModificationError"),
        ("NAMESPACE_ERR", "NamespaceError"),
        ("INVALID_ACCESS_ERR", "InvalidAccessError"),
        ("VALIDATION_ERR", null),
        ("TYPE_MISMATCH_ERR", "TypeMismatchError"),
        ("SECURITY_ERR", "SecurityError"),
        ("NETWORK_ERR", "NetworkError"),
        ("ABORT_ERR", "AbortError"),
        ("URL_MISMATCH_ERR", "URLMismatchError"),
        ("QUOTA_EXCEEDED_ERR", "QuotaExceededError"),
        ("TIMEOUT_ERR", "TimeoutError"),
        ("INVALID_NODE_TYPE_ERR", "InvalidNodeTypeError"),
        ("DATA_CLONE_ERR", "DataCloneError"),
    ];

    internal SharpTSDomException(string message, string name) : base(TypeName, message)
        => Name = DomName = name;

    internal string DomName { get; }

    internal int LegacyCode
    {
        get
        {
            var codes = LegacyCodes;
            for (int i = 0; i < codes.Length; i++)
                if (codes[i].Name == DomName) return i + 1;
            return 0;
        }
    }

    public override string ToString() => Message.Length == 0 ? DomName : $"{DomName}: {Message}";
}

internal sealed class SharpTSDomExceptionClass : SharpTSErrorClass
{
    internal SharpTSDomExceptionClass(SharpTSErrorClass errorClass)
        : base(SharpTSDomException.TypeName, errorClass)
    {
        AddGetter("name", static e => RuntimeValue.FromString(e.DomName));
        AddGetter("message", static e => RuntimeValue.FromString(e.Message));
        AddGetter("code", static e => RuntimeValue.FromNumber(e.LegacyCode));
        Prototype.DefineSymbolProperty(SharpTSSymbol.ToStringTag,
            new SharpTSPropertyDescriptor(value: SharpTSDomException.TypeName, configurable: true));
        var codes = SharpTSDomException.LegacyCodes;
        for (int i = 0; i < codes.Length; i++)
        {
            var descriptor = new SharpTSPropertyDescriptor(value: (double)(i + 1), enumerable: true);
            DefineStaticProperty(codes[i].Constant, descriptor);
            Prototype.DefineExtraProperty(codes[i].Constant, descriptor);
        }
    }

    internal override bool RequiresNew => true;

    public override object? Call(Interpreter interpreter, List<object?> arguments)
        => throw new ThrowException(new SharpTSTypeError("DOMException must be constructed with new"));

    internal override object Construct(Interpreter interpreter, List<object?> arguments, object? newTarget)
    {
        string message = GetArgument(0, "");
        string name = GetArgument(1, SharpTSDomException.DefaultName);
        return new SharpTSDomException(message, name);

        string GetArgument(int index, string fallback) => index < arguments.Count && arguments[index] is not SharpTSUndefined
            ? interpreter.ToStringForBuiltInArgument(arguments[index])
            : fallback;
    }

    private void AddGetter(string key, Func<SharpTSDomException, RuntimeValue> read)
    {
        var getter = BuiltInMethod.CreateV2("get " + key, 0, (_, receiver, _) =>
            receiver.ToObject() is SharpTSDomException error
                ? read(error)
                : throw new ThrowException(new SharpTSTypeError($"DOMException {key} called on an incompatible receiver")));
        Prototype.DefineExtraProperty(key, SharpTSPropertyDescriptor.ForGetter(getter));
    }
}
