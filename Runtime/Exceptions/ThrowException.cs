using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control flow exception thrown when a TypeScript throw statement is executed.
/// </summary>
/// <remarks>
/// Wraps user-thrown values from TypeScript code (e.g., <c>throw new Error("msg")</c>
/// or <c>throw "error"</c>). Thrown by <see cref="Interpreter"/> when executing a throw
/// statement, and caught by try/catch blocks to handle errors. The <see cref="Value"/>
/// property holds the thrown object, which can be any TypeScript value.
///
/// <see cref="Exception.Message"/> is derived from <see cref="Value"/> so that
/// C# callers (notably test harnesses using <c>Assert.Throws&lt;Exception&gt;</c>)
/// can inspect the thrown error's textual form without unwrapping Value.
/// </remarks>
public class ThrowException : Exception
{
    public object? Value { get; }

    public ThrowException(object? value) : base(ExtractMessage(value))
    {
        Value = value;
    }

    /// <summary>
    /// Builds the appropriate host exception for an <see cref="Execution.ExecutionResult"/>
    /// Throw value at a function boundary. Object values (<see cref="SharpTSError"/>,
    /// <see cref="SharpTSObject"/>, <see cref="SharpTSInstance"/>, ...) become a
    /// <see cref="ThrowException"/> so guest <c>try/catch</c> and constructor-identity
    /// checks see the original object.
    /// <para>
    /// String values are origin-sensitive. A HOST-translated string (a plain host
    /// <see cref="Exception"/> surfaced via <c>TranslateException</c> — strict-mode
    /// violations, most internal runtime errors) stays a plain <see cref="Exception"/>
    /// so pre-existing C# callers relying on <c>catch(Exception)</c> with a stringified
    /// message (unit tests, CLI output) keep observing the old shape, and an uncaught one
    /// keeps propagating to the host as a plain <see cref="Exception"/>. A GUEST string
    /// throw (<c>throw "TypeError: x"</c>) instead becomes a <see cref="ThrowException"/>
    /// carrying the exact string as <see cref="Value"/>, so when it crosses a host frame
    /// (callback / interop / Promise executor) a downstream guest <c>catch</c> binds it
    /// verbatim rather than re-typing it (the cross-boundary residual of #694).
    /// </para>
    /// </summary>
    public static Exception FromResult(object? value) => FromResult(value, fromGuestThrow: false);

    /// <inheritdoc cref="FromResult(object?)"/>
    public static Exception FromResult(object? value, bool fromGuestThrow) => value is string s && !fromGuestThrow
        ? new Exception(s)
        : new ThrowException(value);

    /// <summary>
    /// Produces a textual form of <paramref name="value"/> suitable for
    /// <see cref="Exception.Message"/> — preferring spec-shaped "Name: message"
    /// formatting for Error-like objects, falling back to <c>ToString</c>.
    /// Lets C# callers (Test262 classifier, unit tests asserting
    /// <c>ex.Message</c>) distinguish error kinds without unwrapping Value.
    /// </summary>
    private static string ExtractMessage(object? value) => value switch
    {
        null => "null",
        SharpTSUndefined => "undefined",
        string s => s,
        SharpTSError err => err.ToString(),
        SharpTSInstance inst => ExtractFromInstance(inst),
        SharpTSObject obj => ExtractFromObject(obj),
        _ => value.ToString() ?? "",
    };

    private static string ExtractFromInstance(SharpTSInstance inst)
    {
        var name = FindInstanceProperty(inst, "name")?.ToString();
        var message = FindInstanceProperty(inst, "message")?.ToString();
        if (!string.IsNullOrEmpty(name))
            return string.IsNullOrEmpty(message) ? name : $"{name}: {message}";
        return inst.ToString() ?? "";
    }

    private static object? FindInstanceProperty(SharpTSInstance inst, string name)
    {
        if (inst.GetOwnPropertyDescriptor(name) is not null)
            return inst.GetRawField(name);

        for (SharpTSClass? klass = inst.RuntimeClass; klass is not null; klass = klass.Superclass)
        {
            if (klass.Prototype.HasExtra(name))
                return klass.Prototype.TryGetExtra(name);
        }
        return null;
    }

    private static string ExtractFromObject(SharpTSObject obj)
    {
        string? name = FindObjectProperty(obj, "name")?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            // User-defined error types (e.g. Test262Error) don't set .name on
            // each instance — read it off the constructor function instead so
            // `throw new Test262Error(msg)` surfaces as "Test262Error: msg".
            name = ExtractFunctionName(FindObjectProperty(obj, "constructor"));
        }
        string? message = FindObjectProperty(obj, "message")?.ToString();
        if (!string.IsNullOrEmpty(name))
            return string.IsNullOrEmpty(message) ? name : $"{name}: {message}";
        if (!string.IsNullOrEmpty(message))
            return message;
        return "[object Object]";
    }

    private static object? FindObjectProperty(SharpTSObject obj, string name)
    {
        object? current = obj;
        for (int depth = 0; depth < 64 && current is SharpTSObject record; depth++)
        {
            if (record.HasProperty(name)) return record.GetProperty(name);
            current = record.Prototype;
        }
        return null;
    }

    private static string? ExtractFunctionName(object? fn) => fn switch
    {
        SharpTSClass cls => cls.Name,
        SharpTSFunction f => StripFnWrapper(f.ToString()),
        _ => null,
    };

    private static string? StripFnWrapper(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        const string prefix = "<fn ";
        if (s.StartsWith(prefix) && s.EndsWith(">"))
            return s.Substring(prefix.Length, s.Length - prefix.Length - 1);
        return s;
    }
}
