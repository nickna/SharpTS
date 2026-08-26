using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Exceptions

    public static Exception CreateException(object? value)
    {
        return new Exception(Stringify(value));
    }

    public static object WrapException(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException && ex.InnerException is not null)
            ex = ex.InnerException;

        var type = ex.GetType();
        if (ManagedEmittedShapeReflection.IsShape(
                type, ManagedEmittedShape.ThrownValueException))
        {
            var valueProperty = ManagedEmittedShapeReflection.GetPublicProperty(
                type, ManagedEmittedShape.ThrownValueException, "Value")
                ?? throw new InvalidOperationException(
                    "Compiler-emitted $ThrownValueException has no Value property.");
            return valueProperty.GetValue(ex)!;
        }

        // Promise rejection exceptions carry the original rejection value
        // (for example, a raw string from Promise.reject("msg")).
        if (ex is SharpTSPromiseRejectedException runtimeRejection)
        {
            return runtimeRejection.Reason ?? new SharpTSError(ex.Message);
        }

        if (ManagedEmittedShapeReflection.IsShape(
                type, ManagedEmittedShape.PromiseRejectedException))
        {
            var reasonProperty = ManagedEmittedShapeReflection.GetPublicProperty(
                type, ManagedEmittedShape.PromiseRejectedException, "Reason");
            if (reasonProperty?.GetValue(ex) is { } reason)
                return reason;
        }

        // Wrap a host-originated exception as a real Error so guest `catch` sees a
        // proper Error instance (`e instanceof Error`, `e.name === "Error"`) rather than
        // a bare { message, name=<.NET type> } object. Mirrors the emitted
        // $Runtime.WrapException standard fallback. (#700)
        string message = ex.Message;
        const string runtimePrefix = "Runtime Error: ";
        if (message.StartsWith(runtimePrefix, StringComparison.Ordinal))
            message = message[runtimePrefix.Length..];

        return message switch
        {
            var m when m.StartsWith("TypeError:", StringComparison.Ordinal) => new SharpTSTypeError(m),
            var m when m.StartsWith("RangeError:", StringComparison.Ordinal) => new SharpTSRangeError(m),
            var m when m.StartsWith("ReferenceError:", StringComparison.Ordinal) => new SharpTSReferenceError(m),
            var m when m.StartsWith("SyntaxError:", StringComparison.Ordinal) => new SharpTSSyntaxError(m),
            var m when m.StartsWith("URIError:", StringComparison.Ordinal) => new SharpTSURIError(m),
            var m when m.StartsWith("EvalError:", StringComparison.Ordinal) => new SharpTSEvalError(m),
            _ => new SharpTSError(message)
        };
    }

    #endregion
}
