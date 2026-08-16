using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Wrapper for the global <c>fetch</c> binding that is both callable
/// (forwards to <see cref="FetchBuiltIns.FetchMethod"/>) and exposes a
/// <c>cookieJar</c> property pointing at the process-wide
/// <see cref="SharpTSCookieJar"/>.
/// </summary>
/// <remarks>
/// Without this wrapper, <c>fetch.cookieJar</c> would not resolve in the
/// interpreter — bare <see cref="BuiltInAsyncMethod"/> instances do not advertise
/// any properties via <see cref="ISharpTSPropertyAccessor"/>. The wrapper is
/// installed in place of <c>FetchMethod</c> in the interpreter's globals lookup.
/// </remarks>
public sealed class SharpTSFetchGlobal : ISharpTSCallable, ISharpTSAsyncCallable, ISharpTSPropertyAccessor
{
    /// <summary>The singleton instance registered as <c>fetch</c>.</summary>
    public static SharpTSFetchGlobal Instance { get; } = new SharpTSFetchGlobal();

    private readonly BuiltInAsyncMethod _inner = FetchBuiltIns.FetchMethod;

    private SharpTSFetchGlobal() { }

    public int Arity() => _inner.Arity();

    public object? Call(Interpreter interpreter, List<object?> arguments)
        => _inner.Call(interpreter, arguments);

    public Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
        => _inner.CallAsync(interpreter, arguments);

    // ISharpTSPropertyAccessor

    public object? GetProperty(string name)
    {
        return name switch
        {
            "cookieJar" => SharpTSCookieJar.Instance,
            _ => null,
        };
    }

    public void SetProperty(string name, object? value)
    {
        // fetch is not a mutable namespace; ignore writes silently to match
        // JS semantics for non-strict mode property writes on built-ins.
    }

    public bool HasProperty(string name) => name == "cookieJar";

    public IEnumerable<string> PropertyNames
    {
        get { yield return "cookieJar"; }
    }

    public override string ToString() => "<built-in fetch>";
}
