using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Callback for Promise resolve function passed to the executor.
/// Implements ISharpTSCallable so it can be called from TypeScript code.
/// </summary>
public class PromiseResolveCallback : ISharpTSCallable, IBuiltInFunctionMetadata
{
    private readonly Action<object?> _resolve;
    private readonly BuiltInFunctionMetadata _metadata = new();

    public PromiseResolveCallback(Action<object?> resolve)
    {
        _resolve = resolve;
    }

    public int Arity() => 1;
    public string FunctionName => "";
    public bool HasMetadataProperty(string name) => _metadata.Has(name);
    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var value = arguments.Count > 0 ? arguments[0] : null;
        _resolve(value);
        return SharpTSUndefined.Instance;
    }
}

/// <summary>
/// Callback for Promise reject function passed to the executor.
/// Implements ISharpTSCallable so it can be called from TypeScript code.
/// </summary>
public class PromiseRejectCallback : ISharpTSCallable, IBuiltInFunctionMetadata
{
    private readonly Action<object?> _reject;
    private readonly BuiltInFunctionMetadata _metadata = new();

    public PromiseRejectCallback(Action<object?> reject)
    {
        _reject = reject;
    }

    public int Arity() => 1;
    public string FunctionName => "";
    public bool HasMetadataProperty(string name) => _metadata.Has(name);
    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var reason = arguments.Count > 0 ? arguments[0] : null;
        _reject(reason);
        return SharpTSUndefined.Instance;
    }
}
