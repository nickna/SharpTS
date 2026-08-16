using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides member dispatch for AbortSignal instances and static methods.
/// </summary>
public static class AbortSignalBuiltIns
{
    /// <summary>
    /// Gets an instance member from an AbortSignal.
    /// </summary>
    public static object? GetMember(SharpTSAbortSignal receiver, string name)
    {
        return receiver.GetMember(name);
    }

    /// <summary>
    /// Gets a static method from the AbortSignal namespace.
    /// </summary>
    public static ISharpTSCallable? GetStaticMethod(string name)
    {
        return name switch
        {
            "abort" => BuiltInMethod.CreateV2("abort", 0, 1, static (_, _, args) =>
            {
                var reason = args.Length > 0 ? args[0].ToObject() : null;
                return RuntimeValue.FromObject(SharpTSAbortSignal.Abort(reason));
            }),
            "timeout" => BuiltInMethod.CreateV2("timeout", 1, static (_, _, args) =>
            {
                var ms = args[0].IsNumber ? args[0].AsNumberUnsafe() : throw new Exception("Runtime Error: AbortSignal.timeout requires a number argument");
                return RuntimeValue.FromObject(SharpTSAbortSignal.Timeout(ms));
            }),
            "any" => BuiltInMethod.CreateV2("any", 1, static (_, _, args) =>
            {
                if (args[0].ToObject() is not SharpTSArray signalArray)
                    throw new Exception("Runtime Error: AbortSignal.any requires an array of AbortSignal instances");

                var signals = signalArray
                    .Where(e => e is SharpTSAbortSignal)
                    .Cast<SharpTSAbortSignal>();
                return RuntimeValue.FromObject(SharpTSAbortSignal.Any(signals));
            }),
            _ => null
        };
    }
}
