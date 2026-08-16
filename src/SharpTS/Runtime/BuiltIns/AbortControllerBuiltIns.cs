using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides member dispatch for AbortController instances.
/// </summary>
public static class AbortControllerBuiltIns
{
    public static object? GetMember(SharpTSAbortController receiver, string name)
    {
        return name switch
        {
            "signal" => receiver.Signal,
            "abort" => BuiltInMethod.CreateV2("abort", 0, 1, (interp, _, args) =>
            {
                var reason = args.Length > 0 ? args[0].ToObject() : null;
                receiver.Abort(reason, interp);
                return RuntimeValue.Undefined;
            }),
            _ => null
        };
    }
}
