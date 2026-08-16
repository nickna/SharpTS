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
        // Promise rejection exceptions carry the original rejection value
        // (for example, a raw string from Promise.reject("msg")).
        if (ex is SharpTSPromiseRejectedException runtimeRejection)
        {
            return runtimeRejection.Reason ?? new SharpTSError(ex.Message);
        }

        var type = ex.GetType();
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
        return new SharpTSError(ex.Message);
    }

    #endregion
}
