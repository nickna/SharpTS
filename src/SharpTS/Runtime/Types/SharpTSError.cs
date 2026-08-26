using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript Error objects.
/// </summary>
/// <remarks>
/// Follows JavaScript Error semantics with name, message, and stack properties.
/// Can be thrown and caught in try/catch blocks.
/// </remarks>
public class SharpTSError : ITypeCategorized
{
    private readonly SharpTSObject _properties = new([]);
    private string? _stack;
    private System.Diagnostics.StackTrace? _capturedStack;

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Error;

    /// <summary>
    /// The immutable built-in brand used for constructor/prototype identity.
    /// Unlike the guest-visible <see cref="Name"/>, assigning <c>error.name</c>
    /// must not change which intrinsic constructor created the error.
    /// </summary>
    internal string ErrorTypeName { get; }

    /// <summary>
    /// The name of the error type (e.g., "Error", "TypeError", "RangeError").
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The error message describing what went wrong.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// The stack trace at the point the error was created.
    /// </summary>
    public string Stack
    {
        get
        {
            if (_stack is not null)
                return _stack;

            var captured = _capturedStack;
            if (captured is null)
                return "";

            _stack = FormatStackTrace(captured);
            _capturedStack = null;
            return _stack;
        }
        set
        {
            _stack = value;
            _capturedStack = null;
        }
    }

    /// <summary>
    /// The Node.js error code (e.g., "ENOENT", "ECONNREFUSED").
    /// Set for system errors from built-in modules (fs, net, etc.).
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// The system call that failed (e.g., "connect", "listen").
    /// Set for system errors from built-in modules.
    /// </summary>
    public string? Syscall { get; set; }

    /// <summary>
    /// The cause of this error (ES2022). Set via the options parameter:
    /// <c>new Error("message", { cause: someValue })</c>
    /// </summary>
    public object? Cause { get; set; }

    /// <summary>
    /// Whether the cause property was explicitly set (even to undefined/null).
    /// </summary>
    public bool HasCause { get; internal set; }

    internal IEnumerable<string> OwnEnumerableKeys() => _properties.OwnEnumerableKeys();
    internal IEnumerable<string> OwnPropertyNames => _properties.PropertyNames;
    internal bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _properties.DefineProperty(name, descriptor);
    internal SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _properties.GetOwnPropertyDescriptor(name);
    internal bool TryGetProperty(string name, out object? value)
        => _properties.Fields.TryGetValue(name, out value);
    internal bool TryGetAccessor(
        string name, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        getter = _properties.GetGetter(name);
        setter = _properties.GetSetter(name);
        return getter is not null || setter is not null;
    }
    internal void SetProperty(string name, object? value) => _properties.SetProperty(name, value);

    /// <summary>
    /// Creates a new Error with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SharpTSError(string? message = null)
        : this("Error", message)
    {
    }

    /// <summary>
    /// Creates a new Error with the specified name and message.
    /// Used by subclasses to set their specific error type name.
    /// </summary>
    /// <param name="name">The error type name.</param>
    /// <param name="message">The error message.</param>
    protected SharpTSError(string name, string? message)
    {
        ErrorTypeName = name;
        Name = name;
        Message = message ?? "";
        _capturedStack = CaptureStackTrace();
    }

    /// <summary>
    /// Sets the cause property from an options object argument.
    /// </summary>
    internal void SetCauseFromOptions(object? options)
    {
        if (options is SharpTSObject obj && obj.HasProperty("cause"))
        {
            Cause = obj.GetProperty("cause");
            HasCause = true;
        }
    }

    /// <summary>
    /// Captures a stack trace at the current point.
    /// </summary>
    private static System.Diagnostics.StackTrace CaptureStackTrace()
    {
        // Preserve the creation-time call chain but leave its public string
        // representation unbuilt until Stack is observed.
        return new System.Diagnostics.StackTrace(skipFrames: 3, fNeedFileInfo: true);
    }

    private static string FormatStackTrace(System.Diagnostics.StackTrace captured)
    {
        var frames = captured.GetFrames();
        if (frames == null || frames.Length == 0)
            return "";

        var sb = new System.Text.StringBuilder();
        foreach (var frame in frames)
        {
            var fileName = frame.GetFileName();
            var lineNumber = frame.GetFileLineNumber();

            if (StackFrameDisplay.GetMethodName(frame) is not { } displayName) continue;
            var (typeName, methodName) = displayName;

            if (!string.IsNullOrEmpty(typeName))
                sb.Append($"    at {typeName}.{methodName}");
            else
                sb.Append($"    at {methodName}");

            if (!string.IsNullOrEmpty(fileName))
                sb.Append($" ({fileName}:{lineNumber})");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns a string representation of the error.
    /// </summary>
    public override string ToString()
    {
        if (string.IsNullOrEmpty(Message))
        {
            return Name;
        }
        return $"{Name}: {Message}";
    }
}

/// <summary>
/// TypeError: Represents an error when a value is not of the expected type.
/// </summary>
public class SharpTSTypeError : SharpTSError
{
    public SharpTSTypeError(string? message = null)
        : base("TypeError", message)
    {
    }
}

/// <summary>
/// RangeError: Represents an error when a value is not in the expected range.
/// </summary>
public class SharpTSRangeError : SharpTSError
{
    public SharpTSRangeError(string? message = null)
        : base("RangeError", message)
    {
    }
}

/// <summary>
/// ReferenceError: Represents an error when a non-existent variable is referenced.
/// </summary>
public class SharpTSReferenceError : SharpTSError
{
    public SharpTSReferenceError(string? message = null)
        : base("ReferenceError", message)
    {
    }
}

/// <summary>
/// SyntaxError: Represents an error when parsing syntactically invalid code.
/// </summary>
public class SharpTSSyntaxError : SharpTSError
{
    public SharpTSSyntaxError(string? message = null)
        : base("SyntaxError", message)
    {
    }
}

/// <summary>
/// URIError: Represents an error when encoding/decoding URI functions are used incorrectly.
/// </summary>
public class SharpTSURIError : SharpTSError
{
    public SharpTSURIError(string? message = null)
        : base("URIError", message)
    {
    }
}

/// <summary>
/// EvalError: Represents an error regarding the eval() function.
/// </summary>
public class SharpTSEvalError : SharpTSError
{
    public SharpTSEvalError(string? message = null)
        : base("EvalError", message)
    {
    }
}

/// <summary>
/// AggregateError: Represents a collection of errors wrapped in a single error.
/// Used by Promise.any() when all promises reject.
/// </summary>
public class SharpTSAggregateError : SharpTSError
{
    /// <summary>
    /// The array of errors wrapped by this AggregateError.
    /// </summary>
    public SharpTSArray Errors { get; }

    public SharpTSAggregateError(SharpTSArray errors, string? message = null)
        : base("AggregateError", message ?? "")
    {
        Errors = errors;
    }
}
