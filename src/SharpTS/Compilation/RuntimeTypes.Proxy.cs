using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Proxy Support

    /// <summary>
    /// Creates a Proxy wrapping the given target with the given handler.
    /// Used by both interpreter and compiled code paths.
    /// </summary>
    public static object CreateProxy(object? target, object? handler)
    {
        if (target == null)
            throw new Exception("Runtime Error: Cannot create proxy with a non-object as target.");
        if (handler == null)
            throw new Exception("Runtime Error: Cannot create proxy with a non-object as handler.");
        return new SharpTSProxy(target, handler);
    }

    /// <summary>
    /// Creates a revocable Proxy. Returns a Dictionary with "proxy" and "revoke" keys.
    /// </summary>
    public static object CreateRevocableProxy(
        object? target, object? handler, object? undefined)
    {
        if (target == null)
            throw new Exception("Runtime Error: Cannot create proxy with a non-object as target.");
        if (handler == null)
            throw new Exception("Runtime Error: Cannot create proxy with a non-object as handler.");

        var proxy = new SharpTSProxy(target, handler);
        var revoked = false;
        Func<object?[], object?> revoke = _ =>
        {
            if (!revoked)
            {
                revoked = true;
                proxy.Revoke();
            }
            return undefined;
        };

        return new Dictionary<string, object?>
        {
            ["proxy"] = proxy,
            ["revoke"] = revoke
        };
    }

    #endregion
}
