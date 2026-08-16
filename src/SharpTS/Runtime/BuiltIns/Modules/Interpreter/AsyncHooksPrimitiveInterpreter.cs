using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of <c>primitive:async_hooks</c>. Exposes a
/// single <c>create()</c> factory that returns a <see cref="SharpTSAsyncLocalStorage"/>
/// instance. The user-facing <c>AsyncLocalStorage</c> class lives in
/// <c>stdlib/node/async_hooks.ts</c> as a TS class that wraps this instance —
/// the TS layer handles method dispatch dynamically on the instance.
/// </summary>
public static class AsyncHooksPrimitiveInterpreter
{
    private static readonly BuiltInMethod _create = new("create", 0, 0, Create);

    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["create"] = _create,
        };
    }

    private static object? Create(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSAsyncLocalStorage();
    }
}
